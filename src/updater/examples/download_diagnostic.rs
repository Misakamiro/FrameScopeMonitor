// Read-only release diagnostic: downloads and verifies to an explicit isolated
// cache. It never executes an installer or changes the installed application.
use framescope_update::{Source, public_key};
use std::{path::PathBuf, sync::{atomic::AtomicBool, Mutex}, time::Instant};

fn main() -> Result<(), String> {
    let cache = PathBuf::from(std::env::args().nth(1).ok_or("isolated cache path required")?);
    let source = Source::github(public_key(), true)?;
    let cancel = AtomicBool::new(false);
    // The public beta.2 is only a diagnostic input, not an offered downgrade.
    let candidate = source.check("2.0.1-beta.1", true, false, None, &cancel)?.into_iter()
        .find(|candidate| candidate.tag == "v2.0.1-beta.2").ok_or("diagnostic release missing")?;
    let started = Instant::now();
    let last = Mutex::new(Instant::now());
    let receipt = source.download(&candidate, &cache, &cancel, |done,total| {
        let mut last = last.lock().unwrap();
        if last.elapsed().as_secs() >= 3 || done == total {
            eprintln!("progress {done}/{total}"); *last = Instant::now();
        }
    })?;
    let path = source.verified_cache(&cache, &receipt)?;
    let seconds = started.elapsed().as_secs_f64();
    let result = serde_json::json!({"verified":true,"tag":candidate.tag,"path":path,
        "bytes":candidate.manifest.installer.size,"seconds":seconds,
        "bytesPerSecond":candidate.manifest.installer.size as f64 / seconds,
        "sha256":candidate.manifest.installer.sha256,"installed":false});
    std::fs::write(cache.join("diagnostic-result.json"), serde_json::to_vec_pretty(&result).unwrap()).map_err(|e|e.to_string())?;
    println!("{result}");
    Ok(())
}
