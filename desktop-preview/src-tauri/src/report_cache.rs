//! Generated snapshots are parsed once per artifact revision. Raw-input integrity is
//! checked separately; a metadata stamp only invalidates this cache, never proves it.
use serde_json::{json, Value};
use std::{collections::HashMap, path::{Path, PathBuf}, sync::{Arc, Mutex, OnceLock}, time::SystemTime};

type Stamp = Vec<(u64, Option<SystemTime>)>;
type Entry = Arc<Mutex<Option<(Stamp, Arc<Value>)>>>;

#[derive(Debug)]
pub enum ReadError { Unavailable(String), Invalid(String) }
impl From<String> for ReadError { fn from(value:String)->Self { Self::Unavailable(value) } }
impl std::fmt::Display for ReadError {
    fn fmt(&self, f:&mut std::fmt::Formatter<'_>)->std::fmt::Result {
        match self { Self::Unavailable(value) | Self::Invalid(value) => f.write_str(value) }
    }
}

fn entries() -> &'static Mutex<HashMap<PathBuf, Entry>> {
    static ENTRIES: OnceLock<Mutex<HashMap<PathBuf, Entry>>> = OnceLock::new();
    ENTRIES.get_or_init(|| Mutex::new(HashMap::new()))
}

fn stamp(run: &Path) -> Result<Stamp, String> {
    ["framescope-interactive-data.js", "framescope-interactive-manifest.json", "framescope-interactive-report.html"]
        .iter().map(|name| run.join("charts").join(name).metadata()
            .map(|m| (m.len(), m.modified().ok())).map_err(|e| e.to_string())).collect()
}

pub fn load<T>(run: &Path, lock: impl FnOnce() -> Result<T, String>) -> Result<Arc<Value>, ReadError> {
    let entry = {
        let mut all = entries().lock().unwrap_or_else(|p| p.into_inner());
        if !all.contains_key(run) && all.len() >= 3 {
            // Readers keep their Arc alive. Never evict a parse still in flight.
            if let Some(key) = all.iter().find(|(_, v)| Arc::strong_count(v) == 1).map(|(k, _)| k.clone()) {
                all.remove(&key);
            }
        }
        all.entry(run.to_path_buf()).or_insert_with(|| Arc::new(Mutex::new(None))).clone()
    };
    let mut cached = entry.lock().unwrap_or_else(|p| p.into_inner());
    let before = stamp(run)?;
    if let Some((key, value)) = cached.as_ref().filter(|(key, _)| key == &before) {
        let _ = key;
        return Ok(value.clone());
    }
    let _guard = lock()?;
    let source = std::fs::read_to_string(run.join("charts/framescope-interactive-data.js")).map_err(|e| e.to_string())?;
    let value = Arc::new(crate::report_data::parse(&source).map_err(ReadError::Invalid)?);
    if before != stamp(run)? { return Err(ReadError::Unavailable("报告正在更新，请稍后重试".into())); }
    *cached = Some((before, value.clone()));
    let _ = save_index(run, &value);
    Ok(value)
}

pub fn summary(value: &Value) -> Value {
    let stats = &value["frameStats"];
    let average = stats["average"].as_f64();
    let rounded = |key: &str| stats[key].as_f64().map(|v| (v * 10.0).round() / 10.0);
    json!({"createdAt":value["run"]["startLabel"], "durationSeconds":value["run"]["durationSeconds"],
        "frameCount":value["counts"]["frames"], "averageFps":rounded("average"), "low1Fps":rounded("low1"),
        "low01Fps":rounded("low01"), "frameTimeMs":average.filter(|v| *v > 0.).map(|v| (100000. / v).round() / 100.)})
}

fn index_path(run: &Path) -> PathBuf {
    use sha2::{Digest, Sha256};
    let key = format!("{:x}", Sha256::digest(run.to_string_lossy().as_bytes()));
    #[cfg(not(test))]
    let root = PathBuf::from(std::env::var_os("LOCALAPPDATA").unwrap_or_default()).join("FrameScopeMonitor/report-cache-v1");
    #[cfg(test)]
    let root = std::env::temp_dir().join(format!("framescope-report-index-tests-{}",std::process::id()));
    root.join(format!("{key}.json"))
}

fn index_stamp(run: &Path) -> Result<String, String> { Ok(format!("{:?}", stamp(run)?)) }

fn save_index(run: &Path, data: &Value) -> Result<(), String> {
    let path = index_path(run);
    std::fs::create_dir_all(path.parent().unwrap()).map_err(|e| e.to_string())?;
    std::fs::write(path, serde_json::to_vec(&json!({"stamp":index_stamp(run)?, "summary":summary(data)})).map_err(|e|e.to_string())?)
        .map_err(|e| e.to_string())
}

pub fn index(run: &Path) -> Option<Value> {
    let data: Value = serde_json::from_slice(&std::fs::read(index_path(run)).ok()?).ok()?;
    (data["stamp"].as_str()? == index_stamp(run).ok()?).then(|| data["summary"].clone())
}

pub fn overview(data: &Value) -> Value {
    // Copy only small metadata; do not clone large arrays across IPC.
    let mut object = serde_json::Map::new();
    for key in ["statisticsRevision", "renderVersion", "statisticsVersion", "inputFingerprint", "brand", "colors",
        "run", "target", "hardware", "hardwareDerived", "counts", "presentMon", "frameStats", "fpsSampleStats", "systemStats", "capture", "notes"] {
        if let Some(value) = data.get(key) { object.insert(key.into(), value.clone()); }
    }
    let mut fps = serde_json::Map::new();
    for key in ["statisticsVersion", "rawSummary", "filteredSummary", "filter", "source", "sampled"] {
        if let Some(value) = data["fps"].get(key) { fps.insert(key.into(), value.clone()); }
    }
    object.insert("fps".into(), Value::Object(fps));
    object.insert("system".into(), json!({}));
    let process_stats: Vec<_> = data["process"]["stats"].as_array().into_iter().flatten().take(16).cloned().collect();
    object.insert("process".into(), json!({"t":[],"names":[],"cpu":[],"mem":[],"stats":process_stats}));
    Value::Object(object)
}

#[cfg(test)]
#[test]
#[ignore = "Explicit local report fixture; writes only the named test output directory"]
fn export_local_ai_snapshot_fixture() {
    use sha2::{Digest,Sha256};
    let source=std::env::var("FRAMESCOPE_TEST_REPORT").expect("explicit report path");
    let output=PathBuf::from(std::env::var("FRAMESCOPE_TEST_REPORT_OUTPUT").expect("explicit test output"));
    std::fs::create_dir_all(&output).unwrap();
    let data=crate::report_data::parse(&std::fs::read_to_string(source).unwrap()).unwrap();
    let summary=overview(&data);
    std::fs::write(output.join("overview.json"),serde_json::to_vec(&summary).unwrap()).unwrap();
    let bytes=serde_json::to_vec(&json!({"overview":summary,"data":data})).unwrap();
    let hash=format!("{:x}",Sha256::digest(&bytes));
    std::fs::write(output.join(format!("{hash}.json")),&bytes).unwrap();
    std::fs::write(output.join("hash.txt"),hash).unwrap();
}

pub fn section(data: &Value, section: &str) -> Result<Value, String> {
    let keys: &[&str] = match section {
        "fps" => &["fps"], "cpuCore" => &["cpuCore"], "cpuVoltage" => &["cpuVoltage"],
        "cpuVid" => &["cpuVid", "cpuOsdVoltage"], "perf" | "system" | "io" => &["system"],
        "process" => &["process"], _ => return Err("未知报告分区".into()),
    };
    Ok(Value::Object(keys.iter().filter_map(|key| data.get(*key).map(|v| ((*key).into(), v.clone()))).collect()))
}

#[cfg(test)]
mod tests {
    use super::*;
    fn fixture() -> PathBuf {
        let run = std::env::temp_dir().join(format!("framescope-cache-{}-{}",std::process::id(),SystemTime::now().duration_since(SystemTime::UNIX_EPOCH).unwrap().as_nanos()));
        std::fs::create_dir_all(run.join("charts")).unwrap();
        for name in ["framescope-interactive-report.html","framescope-interactive-manifest.json"] {
            std::fs::write(run.join("charts").join(name),"{}").unwrap();
        }
        std::fs::write(run.join("charts/framescope-interactive-data.js"),r#"window.FRAMESCOPE_DATA = {"target":{},"run":{},"notes":{},"counts":{},"fps":{},"system":{},"process":{},"frameStats":{"average":60}};"#).unwrap();
        run
    }
    #[test]
    fn overview_keeps_statistics_but_defers_series() {
        let data = json!({"frameStats":{"average":123.456},"fps":{"t":[1,2],"filteredSummary":{"average":123.456}},
            "process":{"names":["game"],"cpu":[[20]]},"system":{"t":[1]}});
        let first = overview(&data);
        assert_eq!(first["frameStats"],data["frameStats"]);
        assert!(first["fps"].get("t").is_none());
        assert_eq!(section(&data,"process").unwrap()["process"],data["process"]);
        assert!(section(&data,"arbitrary-path").is_err());
    }
    #[test]
    fn concurrent_readers_share_one_parse_and_invalidate_changed_artifacts() {
        use std::sync::atomic::{AtomicUsize,Ordering};
        let run=fixture(); let count=Arc::new(AtomicUsize::new(0));
        let barrier=Arc::new(std::sync::Barrier::new(8));
        let threads:Vec<_>=(0..8).map(|_| {
            let run=run.clone();let count=count.clone();let barrier=barrier.clone();
            std::thread::spawn(move || {barrier.wait();load(&run,||{count.fetch_add(1,Ordering::SeqCst);Ok(())}).unwrap()})
        }).collect();
        let values:Vec<_>=threads.into_iter().map(|t|t.join().unwrap()).collect();
        assert_eq!(count.load(Ordering::SeqCst),1);
        assert!(values.iter().all(|v|Arc::ptr_eq(v,&values[0])));
        assert_eq!(index(&run).unwrap()["averageFps"],60.);
        let path=run.join("charts/framescope-interactive-data.js");
        let changed=std::fs::read_to_string(&path).unwrap().replace(":60",":144");
        std::fs::write(&path,changed).unwrap();
        assert!(index(&run).is_none());
        assert_eq!(load(&run,||Ok(())).unwrap()["frameStats"]["average"],144);
        std::fs::write(&path,"window.FRAMESCOPE_DATA = {};alert(1);").unwrap();
        assert!(matches!(load(&run,||Ok(())),Err(ReadError::Invalid(_))));
        std::fs::remove_dir_all(run).unwrap();
    }
}
