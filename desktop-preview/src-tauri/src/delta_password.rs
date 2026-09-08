//! Delta Force (三角洲行动) password-door data for the overlay's "三角洲密码"
//! row: fetched from the same public community APIs the NexBox upstream uses
//! (primary JSON, fallback plain text), cached for 60s. Minimal port of the
//! upstream delta_force.rs password subset - no image cache / DLSS / maps.

use serde::{Deserialize, Serialize};
use std::sync::Mutex;
use std::time::{Duration, Instant};

#[derive(Serialize, Deserialize, Clone, Debug)]
pub struct DeltaPasswordItem {
    pub name: String,
    pub password: String,
}

#[derive(Deserialize)]
struct DeltaPasswordResponse {
    status: String,
    data: Vec<DeltaPasswordItem>,
}

static CACHE: Mutex<Option<(Vec<DeltaPasswordItem>, Instant)>> = Mutex::new(None);

fn http_client() -> reqwest::blocking::Client {
    reqwest::blocking::Client::builder()
        .timeout(Duration::from_secs(10))
        .build()
        .expect("reqwest client build")
}

fn fetch_primary() -> Option<Vec<DeltaPasswordItem>> {
    let url = "https://i.elaina.vin/api/%E4%B8%89%E8%A7%92%E6%B4%B2/%E5%AF%86%E7%A0%81/";
    let response = http_client()
        .get(url)
        .send()
        .ok()?
        .json::<DeltaPasswordResponse>()
        .ok()?;
    if response.status == "success" { Some(response.data) } else { None }
}

// Backup API returns plain text shaped like:
//   1. 潮汐监狱
//   具体点位：监狱行政区1楼大厅楼梯拐角处
//   每日密码：0557
// "N." starts a map entry; the 每日密码 line fills its password.
fn fetch_backup() -> Option<Vec<DeltaPasswordItem>> {
    let url = "https://api.s0o1.com/API/sjz/mm/";
    let text = http_client().get(url).send().ok()?.text().ok()?;

    let mut items: Vec<DeltaPasswordItem> = Vec::new();
    let mut current: Option<(String, String)> = None;
    for line in text.lines() {
        let line = line.trim();
        if let Some(dot) = line.find('.') {
            let head = &line[..dot];
            if !head.is_empty() && head.chars().all(|c| c.is_ascii_digit()) {
                let name = line[dot + 1..].trim();
                if !name.is_empty() {
                    if let Some((name, password)) = current.take() {
                        if !password.is_empty() {
                            items.push(DeltaPasswordItem { name, password });
                        }
                    }
                    current = Some((name.to_string(), String::new()));
                    continue;
                }
            }
        }
        if let Some(index) = line.find("每日密码") {
            let after = line[index + "每日密码".len()..]
                .trim_start_matches(|c| c == ':' || c == '：');
            if let Some((_, password)) = current.as_mut() {
                if password.is_empty() {
                    *password = after.split_whitespace().next().unwrap_or("").to_string();
                }
            }
        }
    }
    if let Some((name, password)) = current.take() {
        if !password.is_empty() {
            items.push(DeltaPasswordItem { name, password });
        }
    }
    if items.is_empty() { None } else { Some(items) }
}

fn fetch_all() -> Option<Vec<DeltaPasswordItem>> {
    fetch_primary().filter(|items| !items.is_empty()).or_else(fetch_backup)
}

/// Network fetch off the async worker via spawn_blocking (preview_native is
/// an async command and must not block its worker thread on the 10s HTTP
/// timeout).
pub async fn get_delta_passwords() -> Result<Vec<DeltaPasswordItem>, String> {
    {
        let cache = CACHE.lock().map_err(|_| "password cache poisoned".to_string())?;
        if let Some((data, at)) = cache.as_ref() {
            if at.elapsed() < Duration::from_secs(60) {
                return Ok(data.clone());
            }
        }
    }
    let fetched = tauri::async_runtime::spawn_blocking(fetch_all)
        .await
        .map_err(|e| format!("password fetch join: {e}"))?
        .ok_or_else(|| "无法获取三角洲密码数据".to_string())?;
    {
        let mut cache = CACHE.lock().map_err(|_| "password cache poisoned".to_string())?;
        *cache = Some((fetched.clone(), Instant::now()));
    }
    Ok(fetched)
}
