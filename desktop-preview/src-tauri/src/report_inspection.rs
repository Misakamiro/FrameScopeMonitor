//! One background reader, independent per-report budgets, and shared in-flight
//! results. Polling a page never starts a duplicate CSV scan.
use serde_json::{json, Value};
use std::{collections::HashMap, path::{Path, PathBuf}, sync::{mpsc, Mutex, OnceLock}, time::{Duration, Instant}};

struct Entry { stamp: String, value: Value, at: Instant }
struct Task { run: PathBuf, root: PathBuf, stamp: String, report: Value }
fn states() -> &'static Mutex<HashMap<PathBuf, Entry>> {
    static STATES: OnceLock<Mutex<HashMap<PathBuf, Entry>>> = OnceLock::new();
    STATES.get_or_init(|| Mutex::new(HashMap::new()))
}
fn publish(task: &Task, value: Value) {
    let mut states = states().lock().unwrap_or_else(|p|p.into_inner());
    if let Some(entry) = states.get_mut(&task.run).filter(|e| e.stamp == task.stamp) {
        entry.value = value; entry.at = Instant::now();
    }
}
fn sender() -> &'static mpsc::Sender<Task> {
    static SENDER: OnceLock<mpsc::Sender<Task>> = OnceLock::new();
    SENDER.get_or_init(|| {
        let (tx, rx) = mpsc::channel::<Task>();
        std::thread::spawn(move || for task in rx {
            let mut report = task.report.clone();
            report["inspectionStatus"] = json!("checking");
            publish(&task, report.clone());
            match crate::backend::load_report_snapshot(&report) {
                Ok(data) => {
                    let summary = crate::report_cache::summary(&data);
                    for (key, value) in summary.as_object().unwrap() { report[key] = value.clone(); }
                    publish(&task, report.clone());
                },
                Err(error) => {
                    let invalid = matches!(error, crate::report_cache::ReadError::Invalid(_));
                    report["inspectionStatus"] = json!(if invalid {"checked"} else {"unavailable"});
                    if invalid {report["artifactStatus"] = json!("incomplete");}
                    report["artifactError"] = json!(error.to_string());
                    report["canOpenReport"] = json!(false); report["canOpen"] = json!(false);
                    publish(&task, report); continue;
                }
            }
            let result = crate::backend::inspect_report_runs(&task.root, &[&task.run.to_string_lossy()], Duration::from_secs(180))
                .and_then(|response| crate::backend::apply_report_inspections(std::slice::from_mut(&mut report), &response));
            if let Err(error) = result {
                report["inspectionStatus"] = json!("unavailable"); report["artifactError"] = json!(error);
            }
            if crate::backend::report_inspection_stamp(&task.run, &task.root).as_ref() != Some(&task.stamp) {
                report["inspectionStatus"] = json!("pending");
                report["artifactError"] = json!("报告正在更新，等待稳定后检查");
            }
            publish(&task, report);
        });
        tx
    })
}

pub fn apply(reports: &mut [Value], root: &Path, retry: bool) -> Result<(), String> {
    for report in reports {
        if report["artifactError"].as_str().is_some_and(|e| !e.is_empty()) { continue; }
        let run = PathBuf::from(report["runDir"].as_str().ok_or("报告目录不存在")?);
        let stamp = crate::backend::report_inspection_stamp(&run, root).ok_or("无法读取报告时间戳")?;
        let mut all = states().lock().unwrap_or_else(|p|p.into_inner());
        let cached = all.get(&run).filter(|e| e.stamp == stamp && !(e.value["inspectionStatus"] == "unavailable" && (retry || e.at.elapsed() > Duration::from_secs(60))));
        if let Some(entry) = cached {
            for key in ["artifactStatus","inspectionStatus","artifactError","canOpen","canOpenReport","canRegenerate",
                "kind","createdAt","durationSeconds","frameCount","averageFps","low1Fps","low01Fps","frameTimeMs"] {
                if let Some(value) = entry.value.get(key) { report[key] = value.clone(); }
            }
            continue;
        }
        if all.len() >= 512 { all.retain(|_, e| e.value["inspectionStatus"] == "pending" || e.value["inspectionStatus"] == "checking"); }
        all.insert(run.clone(), Entry { stamp: stamp.clone(), value: report.clone(), at: Instant::now() });
        sender().send(Task { run, root:root.to_path_buf(), stamp, report:report.clone() }).map_err(|_| "报告检查队列已关闭")?;
    }
    Ok(())
}
