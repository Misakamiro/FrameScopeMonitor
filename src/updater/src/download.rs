use super::*;
use std::{sync::atomic::AtomicU64, time::Instant};

pub(super) const SPEED_WINDOW: Duration = Duration::from_secs(8);
pub(super) const MIN_BYTES_PER_SECOND: u64 = 64 * 1024;

pub(super) fn client() -> Result<reqwest::Client> {
    reqwest::Client::builder().connect_timeout(Duration::from_secs(4))
        .read_timeout(Duration::from_secs(15)).timeout(Duration::from_secs(1800))
        .redirect(reqwest::redirect::Policy::limited(5))
        .user_agent("FrameScope/2 updater").build().map_err(request_error)
}

impl Source {
    #[cfg(test)]
    pub(super) fn download_routes(&self, original: &str, expected: u64, cancel: &AtomicBool) -> Result<Vec<bool>> {
        let runtime=tokio::runtime::Builder::new_current_thread().enable_all().build().map_err(|e|e.to_string())?;
        runtime.block_on(self.asset_probes(&client()?,original,expected,cancel,&|_|{}))
            .map(|probes|probes.into_iter().map(|p|p.proxy).collect())
    }

    #[cfg(test)]
    pub(super) fn transfer_with_speed_check(&self, url: &str, path: &Path, expected: u64,
        cancel: &AtomicBool, progress: &impl Fn(u64,u64), watch_speed: bool) -> Result<()> {
        self.transfer_observed(url,path,expected,cancel,progress,watch_speed,MIN_BYTES_PER_SECOND,false,&|_|{})
    }

    pub(super) fn transfer_observed(&self, url: &str, path: &Path, expected: u64,
        cancel: &AtomicBool, progress: &impl Fn(u64,u64), watch_speed: bool, minimum:u64,
        proxy:bool, observer:&impl Fn(DownloadTelemetry)) -> Result<()> {
        let runtime = tokio::runtime::Builder::new_current_thread().enable_all().build().map_err(|e|e.to_string())?;
        runtime.block_on(async {
            let received = AtomicU64::new(0);
            let transfer = async {
                let mut response = client()?.get(url).send().await.and_then(|r|r.error_for_status()).map_err(request_error)?;
                if response.content_length().is_some_and(|length|length!=expected){return Err("下载线路返回的文件大小与清单不一致".into());}
                let mut file = fs::File::create(path).map_err(|e|e.to_string())?;
                let mut size = 0;
                let mut previous=0;
                let mut stamp=Instant::now();
                while let Some(chunk) = response.chunk().await.map_err(request_error)? {
                    cancelled(cancel)?;
                    size += chunk.len() as u64;
                    if size > expected { return Err("安装包大小超出清单".into()); }
                    file.write_all(&chunk).map_err(|e|e.to_string())?;
                    received.store(size, Ordering::Relaxed);
                    progress(size, expected);
                    if stamp.elapsed()>=Duration::from_millis(250)||size==expected {
                        observer(DownloadTelemetry::event("progress",proxy,((size-previous) as f64/stamp.elapsed().as_secs_f64().max(0.000001)) as u64));
                        previous=size;stamp=Instant::now();
                    }
                }
                if size!=expected{return Err("下载文件不完整".into());}
                file.sync_all().map_err(|e|e.to_string())
            };
            let slow = async {
                let mut previous = 0;
                loop {
                    tokio::time::sleep(SPEED_WINDOW).await;
                    let current = received.load(Ordering::Relaxed);
                    let rate=current.saturating_sub(previous)/SPEED_WINDOW.as_secs();
                    observer(DownloadTelemetry::event("progress",proxy,rate));
                    if watch_speed && rate<minimum { break; }
                    previous = current;
                }
            };
            tokio::select! {
                result = transfer => result,
                _ = cancellation(cancel) => Err("下载已取消".into()),
                _ = slow => Err("下载线路持续低速（连续 8 秒），尝试其他线路".into()),
            }
        })
    }
}

pub(super) async fn cancellation(cancel: &AtomicBool) {
    while !cancel.load(Ordering::SeqCst) { tokio::time::sleep(Duration::from_millis(100)).await; }
}
