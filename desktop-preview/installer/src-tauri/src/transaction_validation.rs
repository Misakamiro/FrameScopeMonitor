//! Optional receipt-bound candidate acceptance harness. It uses the exact
//! embedded ZIP and production fs core, with explicit TEMP system boundaries.
use crate::{
    install_transaction::{self as tx, Request, Result, Runtime},
    installer,
};
use sha2::{Digest, Sha256};
use std::{
    fs,
    path::{Path, PathBuf},
};
struct FixtureRuntime {
    state: PathBuf,
    roots: Vec<PathBuf>,
    stops: usize,
    uninstall: bool,
}
impl Runtime for FixtureRuntime {
    fn checkpoint(&mut self, phase: &str, index: usize) -> Result<()> {
        eprintln!("validation-checkpoint:{phase}:{index}");
        Ok(())
    }
    fn space(&mut self, _: &Path) -> Result<u64> {
        Ok(u64::MAX)
    }
    fn stop(&mut self, target: &Path, helper: &Path) -> Result<()> {
        eprintln!("validation-stop:{}:{}", target.display(), helper.display());
        tx::assert_path(target)?;
        if !helper.is_file() {
            return Err("Verified candidate has no normal-stop helper".into());
        }
        self.stops += 1;
        Ok(())
    }
    fn snapshot(&mut self) -> Result<serde_json::Value> {
        eprintln!("validation-snapshot:{}", self.state.display());
        Ok(serde_json::json!(
            fs::read(&self.state).map_err(|e| e.to_string())?
        ))
    }
    fn apply(&mut self) -> Result<()> {
        eprintln!("validation-apply:{}", self.state.display());
        fs::write(
            &self.state,
            if self.uninstall {
                b"uninstalled".as_slice()
            } else {
                b"installed: fake ARP raw bytes / shortcut bytes / Run".as_slice()
            },
        )
        .map_err(|e| e.to_string())
    }
    fn restore(&mut self, value: &serde_json::Value) -> Result<()> {
        let bytes: Vec<u8> = serde_json::from_value(value.clone()).map_err(|e| e.to_string())?;
        fs::write(&self.state, bytes).map_err(|e| e.to_string())
    }
    fn allowed_data_roots(&self) -> Vec<PathBuf> {
        self.roots.clone()
    }
}
fn argument(args: &[String], key: &str) -> Result<String> {
    let matches: Vec<_> = args
        .iter()
        .enumerate()
        .filter(|(_, a)| a.as_str() == key)
        .collect();
    if matches.len() != 1 {
        return Err(format!("Expected exactly one {key}"));
    }
    args.get(matches[0].0 + 1)
        .cloned()
        .ok_or(format!("Missing {key} value"))
}
fn verify_files(target: &Path) -> Result<()> {
    let mut bytes = fs::read(target.join(tx::MANIFEST)).map_err(|e| e.to_string())?;
    if bytes.starts_with(&[0xef, 0xbb, 0xbf]) {
        bytes.drain(..3);
    }
    let manifest: serde_json::Value = serde_json::from_slice(&bytes).map_err(|e| e.to_string())?;
    for file in manifest["files"]
        .as_array()
        .ok_or("Missing manifest files")?
    {
        let path = target.join(file["path"].as_str().ok_or("Missing payload path")?);
        tx::assert_path(&path)?;
        if fs::metadata(&path).map_err(|e| e.to_string())?.len()
            != file["length"].as_u64().ok_or("Missing length")?
            || !tx::hash(&path)?.eq_ignore_ascii_case(file["sha256"].as_str().ok_or("Missing SHA")?)
        {
            return Err(format!("Candidate file mismatch {}", path.display()));
        }
    }
    Ok(())
}
fn invoke(
    root: &Path,
    candidate: &Path,
    runtime: &mut FixtureRuntime,
    uninstall: bool,
    delete_data: bool,
) -> Result<()> {
    runtime.uninstall = uninstall;
    tx::execute(
        Request {
            target: &root.join("install"),
            payload: installer::PAYLOAD_ZIP,
            self_exe: candidate,
            required: &installer::required_payload_files()?,
            uninstall,
            delete_data,
            deletions: vec![],
            data_roots: if delete_data {
                runtime.roots.clone()
            } else {
                vec![]
            },
            progress: &|_, _| {},
        },
        runtime,
    )
}
pub fn run(args: Vec<String>) -> Result<()> {
    let root = PathBuf::from(argument(&args, "--fixture-root")?);
    let candidate = PathBuf::from(argument(&args, "--candidate")?);
    tx::assert_path(&root).map_err(|error| format!("fixture root validation: {error}"))?;
    tx::assert_path(&candidate).map_err(|error| format!("candidate path validation: {error}"))?;
    if !root.file_name().is_some_and(|n| {
        n.to_string_lossy()
            .starts_with("framescope-installer-candidate-")
    }) {
        return Err("Explicit uniquely named candidate TEMP fixture required".into());
    }
    let temp = tx::normalize(&std::env::temp_dir()).map_err(|error| format!("TEMP normalization: {error}"))?;
    if !tx::normalize(&root).map_err(|error| format!("fixture normalization: {error}"))?.starts_with(&temp) || candidate.starts_with(&root) {
        return Err("Candidate fixture escapes TEMP or candidate is mutable within fixture".into());
    }
    let candidate_sha = tx::hash(&candidate)?;
    let payload_sha = format!("{:x}", Sha256::digest(installer::PAYLOAD_ZIP));
    if !candidate_sha.eq_ignore_ascii_case(&argument(&args, "--candidate-sha256")?)
        || !payload_sha.eq_ignore_ascii_case(&argument(&args, "--payload-sha256")?)
    {
        return Err("Candidate/payload provenance SHA mismatch".into());
    }
    if root.exists() {
        return Err("Candidate fixture must be new; previous evidence is preserved".into());
    }
    fs::create_dir(&root).map_err(|e| format!("fixture creation: {e}"))?;
    let state = root.join("fake-system.bin");
    fs::write(&state, b"original fake registry/link bytes").map_err(|e| format!("fake state creation: {e}"))?;
    let data = root.join("profile/FrameScopeMonitorData");
    fs::create_dir_all(&data).map_err(|e| format!("fake data creation: {e}"))?;
    let mut runtime = FixtureRuntime {
        state,
        roots: vec![data.clone()],
        stops: 0,
        uninstall: false,
    };
    invoke(&root, &candidate, &mut runtime, false, false).map_err(|error| format!("first install: {error}"))?;
    verify_files(&root.join("install")).map_err(|error| format!("first install verification: {error}"))?;
    fs::write(
        root.join("install/framescope-config.json"),
        b"retained config",
    )
    .map_err(|e| e.to_string())?;
    fs::write(root.join("install/personal.txt"), b"unknown sentinel").map_err(|e| e.to_string())?;
    fs::write(data.join("run.csv"), b"retained telemetry").map_err(|e| e.to_string())?;
    fs::write(
        root.join("install/FrameScope.exe"),
        b"old candidate program fixture",
    )
    .map_err(|e| e.to_string())?;
    invoke(&root, &candidate, &mut runtime, false, false).map_err(|error| format!("upgrade: {error}"))?;
    verify_files(&root.join("install"))?;
    if fs::read(root.join("install/framescope-config.json")).map_err(|e| e.to_string())?
        != b"retained config"
    {
        return Err("Upgrade changed config".into());
    }
    invoke(&root, &candidate, &mut runtime, true, false).map_err(|error| format!("keep-data uninstall: {error}"))?;
    if root.join("install/FrameScope.exe").exists()
        || root.join("install/FrameScope Installer.exe").exists()
        || fs::read(data.join("run.csv")).map_err(|e| e.to_string())? != b"retained telemetry"
    {
        return Err("Keep-data uninstall contract failed".into());
    }
    invoke(&root, &candidate, &mut runtime, false, false).map_err(|error| format!("reinstall for delete-data: {error}"))?;
    invoke(&root, &candidate, &mut runtime, true, true).map_err(|error| format!("delete-data uninstall: {error}"))?;
    if root.join("install/framescope-config.json").exists()
        || data.exists()
        || fs::read(root.join("install/personal.txt")).map_err(|e| e.to_string())?
            != b"unknown sentinel"
    {
        return Err("Delete-data uninstall contract failed".into());
    }
    let result = serde_json::json!({"status":"PASS","candidateSha256":candidate_sha,"embeddedPayloadSha256":payload_sha,"scenarios":["first-install","upgrade","keep-data-uninstall","delete-data-uninstall"],"normalStopAdapterCalls":runtime.stops,"systemAdapter":"explicit TEMP fake Registry/Shortcut/Run; no live product processes","root":root});
    fs::write(
        root.join("result.json"),
        serde_json::to_vec_pretty(&result).map_err(|e| e.to_string())?,
    )
    .map_err(|e| e.to_string())?;
    println!(
        "{}",
        serde_json::to_string(&result).map_err(|e| e.to_string())?
    );
    Ok(())
}
