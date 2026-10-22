use super::*;
use std::{sync::atomic::AtomicU64, time::Instant};

const SAMPLE_BYTES: u64 = 256 * 1024;
const SAMPLE_TIMEOUT: Duration = Duration::from_secs(6);
const SPEED_WINDOW: Duration = Duration::from_secs(8);
const MIN_BYTES_PER_SECOND: u64 = 64 * 1024;

fn client() -> Result<reqwest::Client> {
    reqwest::Client::builder().connect_timeout(Duration::from_secs(4))
        .read_timeout(Duration::from_secs(15)).timeout(Duration::from_secs(1800))
        .redirect(reqwest::redirect::Policy::limited(5))
        .user_agent("FrameScope/2 updater").build().map_err(request_error)
}

async fn sample(client: &reqwest::Client, url: String, expected: u64) -> Result<f64> {
    let limit = expected.min(SAMPLE_BYTES);
    let started = Instant::now();
    let mut response = client.get(url).header(reqwest::header::RANGE, format!("bytes=0-{}", limit - 1))
        .timeout(SAMPLE_TIMEOUT).send().await.and_then(|r| r.error_for_status()).map_err(request_error)?;
    // Some mirrors ignore Range. Read only the bounded prefix and drop that
    // response; never download a second whole installer as a speed test.
    if response.status() == reqwest::StatusCode::OK
        && response.content_length().is_some_and(|length| length != expected) {
        return Err("下载线路返回的文件大小与清单不一致".into());
    }
    let mut received = 0;
    while received < limit {
        let Some(chunk) = response.chunk().await.map_err(request_error)? else { break; };
        received += (chunk.len() as u64).min(limit - received);
    }
    if received != limit { return Err("下载线路探测未收到完整样本".into()); }
    Ok(received as f64 / started.elapsed().as_secs_f64().max(0.000001))
}

impl Source {
    pub(super) fn download_routes(&self, original: &str, expected: u64, cancel: &AtomicBool) -> Result<Vec<bool>> {
        cancelled(cancel)?;
        if self.proxy.is_none() { return Ok(vec![false]); }
        let preferred = !self.origin_first.load(Ordering::Relaxed);
        let runtime = tokio::runtime::Builder::new_current_thread().enable_all().build().map_err(|e| e.to_string())?;
        runtime.block_on(async {
            let client = client()?;
            let probes = async {
                let (first, second) = tokio::join!(
                    sample(&client, self.url(original, preferred), expected),
                    sample(&client, self.url(original, !preferred), expected));
                let mut scores = vec![(preferred, first.unwrap_or(0.0)), (!preferred, second.unwrap_or(0.0))];
                for (proxy, speed) in &scores {
                    eprintln!("FrameScope updater asset sample: {} {:.0} B/s",
                        if *proxy { "accelerator" } else { "origin/system-proxy" }, speed);
                }
                scores.sort_by(|a, b| b.1.total_cmp(&a.1));
                // A failed/unsupported probe does not disable its full download:
                // it remains the fallback if the other route later fails.
                Ok(scores.into_iter().map(|(route, _)| route).collect())
            };
            tokio::select! { result = probes => result, _ = cancellation(cancel) => Err("下载已取消".into()) }
        })
    }

    pub(super) fn transfer_with_speed_check(&self, url: &str, path: &Path, expected: u64,
        cancel: &AtomicBool, progress: &impl Fn(u64,u64), watch_speed: bool) -> Result<()> {
        let runtime = tokio::runtime::Builder::new_current_thread().enable_all().build().map_err(|e| e.to_string())?;
        runtime.block_on(async {
            let received = AtomicU64::new(0);
            let transfer = async {
                let mut response = client()?.get(url).send().await.and_then(|r| r.error_for_status()).map_err(request_error)?;
                let mut file = fs::File::create(path).map_err(|e| e.to_string())?;
                let mut size = 0;
                while let Some(chunk) = response.chunk().await.map_err(request_error)? {
                    cancelled(cancel)?;
                    size += chunk.len() as u64;
                    if size > expected { return Err("安装包大小超出清单".into()); }
                    file.write_all(&chunk).map_err(|e| e.to_string())?;
                    received.store(size, Ordering::Relaxed);
                    progress(size, expected);
                }
                file.sync_all().map_err(|e| e.to_string())
            };
            let slow = async {
                let mut previous = 0;
                loop {
                    tokio::time::sleep(SPEED_WINDOW).await;
                    let current = received.load(Ordering::Relaxed);
                    if current.saturating_sub(previous) < MIN_BYTES_PER_SECOND * SPEED_WINDOW.as_secs() { break; }
                    previous = current;
                }
            };
            // Both probes and transfers live in this runtime. Cancellation or a
            // slow-route fallback drops the old socket, with no detached job.
            tokio::select! {
                result = transfer => result,
                _ = cancellation(cancel) => Err("下载已取消".into()),
                _ = slow, if watch_speed => Err("下载线路持续低速（连续 8 秒低于 64 KiB/s），尝试其他线路".into()),
            }
        })
    }
}

async fn cancellation(cancel: &AtomicBool) {
    while !cancel.load(Ordering::SeqCst) { tokio::time::sleep(Duration::from_millis(100)).await; }
}
