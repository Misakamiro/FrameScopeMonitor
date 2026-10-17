use super::*;
use std::io::Write;
#[test]
fn existing_installer_executable_normalizes_to_a_readable_file() {
    let executable = std::env::current_exe().unwrap();
    let normalized = normalize(&executable).expect("existing installer executable must normalize");
    assert_eq!(hash(&normalized).unwrap(), hash(&executable).unwrap());
}

fn fixture() -> PathBuf {
    let root = std::env::temp_dir().join(format!(
        "framescope-transaction-{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    ));
    fs::create_dir(&root).unwrap();
    eprintln!("Retained transaction fixture: {}", root.display());
    root
}
fn payload(corrupt: bool) -> Vec<u8> {
    let mut zip = zip::ZipWriter::new(std::io::Cursor::new(Vec::new()));
    let mut files = Vec::new();
    for name in [
        "FrameScope.exe",
        "FrameScopeMonitor.exe",
        "FrameScopeUninstaller.exe",
        "new.dll",
    ] {
        let contents = format!("new {name}").into_bytes();
        zip.start_file(name, zip::write::SimpleFileOptions::default())
            .unwrap();
        zip.write_all(&contents).unwrap();
        files.push(serde_json::json!({"path": name, "length": contents.len(), "sha256": if corrupt { "0".repeat(64) } else { format!("{:x}", Sha256::digest(&contents)) }}));
    }
    zip.start_file(MANIFEST, zip::write::SimpleFileOptions::default())
        .unwrap();
    zip.write_all(&serde_json::to_vec(&serde_json::json!({"schema":1,"files":files})).unwrap())
        .unwrap();
    zip.finish().unwrap().into_inner()
}
#[derive(Default)]
struct Fake {
    stops: usize,
    system: serde_json::Value,
    fail: Option<(&'static str, usize)>,
    restore_fails: bool,
    space: Option<u64>,
    final_data: Option<PathBuf>,
    roots: Vec<PathBuf>,
}
impl Runtime for Fake {
    fn space(&mut self, _: &Path) -> Result<u64> {
        Ok(self.space.unwrap_or(u64::MAX))
    }
    fn stop(&mut self, _: &Path, _: &Path) -> Result<()> {
        self.stops += 1;
        if let Some(root) = &self.final_data {
            let mut csv = fs::OpenOptions::new()
                .append(true)
                .open(root.join("run.csv"))
                .map_err(|e| e.to_string())?;
            csv.write_all(b"final sample\n")
                .map_err(|e| e.to_string())?;
            fs::write(root.join("new-report.html"), b"final report").map_err(|e| e.to_string())?;
        }
        Ok(())
    }
    fn snapshot(&mut self) -> Result<serde_json::Value> {
        Ok(self.system.clone())
    }
    fn allowed_data_roots(&self) -> Vec<PathBuf> {
        self.roots.clone()
    }
    fn apply(&mut self) -> Result<()> {
        self.system = serde_json::json!({"installed":true});
        self.checkpoint("apply", 0)
    }
    fn restore(&mut self, value: &serde_json::Value) -> Result<()> {
        if self.restore_fails {
            return Err("restore failed".into());
        }
        self.system = value.clone();
        Ok(())
    }
    fn checkpoint(&mut self, phase: &str, index: usize) -> Result<()> {
        if self.fail == Some((phase, index)) {
            Err(format!("injected {phase} {index}"))
        } else {
            Ok(())
        }
    }
}
fn run(
    root: &Path,
    bytes: &[u8],
    fake: &mut Fake,
    uninstall: bool,
    delete: Vec<PathBuf>,
    data: Vec<PathBuf>,
) -> Result<()> {
    fake.roots = data.clone();
    let self_exe = root.join("setup.exe");
    if !self_exe.exists() {
        fs::write(&self_exe, b"installer").unwrap();
    }
    execute(
        Request {
            target: &root.join("install"),
            payload: bytes,
            self_exe: &self_exe,
            required: &["FrameScope.exe".into(), "FrameScopeMonitor.exe".into()],
            uninstall,
            delete_data: !delete.is_empty() || !data.is_empty(),
            deletions: delete,
            data_roots: data,
            progress: &|_, _| {},
        },
        fake,
    )
}
fn baseline(root: &Path) {
    fs::create_dir(root.join("install")).unwrap();
    for name in ["FrameScope.exe", "FrameScopeMonitor.exe"] {
        fs::write(root.join("install").join(name), format!("old {name}")).unwrap();
    }
    fs::write(
        root.join("install/framescope-config.json"),
        b"configuration",
    )
    .unwrap();
    fs::write(root.join("install/personal.txt"), b"unknown").unwrap();
}
fn old_unchanged(root: &Path) {
    for name in ["FrameScope.exe", "FrameScopeMonitor.exe"] {
        assert_eq!(
            fs::read(root.join("install").join(name)).unwrap(),
            format!("old {name}").as_bytes()
        );
    }
    assert_eq!(
        fs::read(root.join("install/framescope-config.json")).unwrap(),
        b"configuration"
    );
    assert_eq!(
        fs::read(root.join("install/personal.txt")).unwrap(),
        b"unknown"
    );
    assert!(!root.join("install/new.dll").exists());
    assert!(!root.join("install/FrameScope Installer.exe").exists());
}
#[test]
fn first_upgrade_keep_and_delete_data_run_full_core() {
    let root = fixture();
    let bytes = payload(false);
    let mut fake = Fake::default();
    run(&root, &bytes, &mut fake, false, vec![], vec![]).unwrap();
    assert_eq!(
        fs::read(root.join("install/FrameScope.exe")).unwrap(),
        b"new FrameScope.exe"
    );
    assert_eq!(fake.stops, 1);
    fs::write(root.join("install/framescope-config.json"), b"settings").unwrap();
    fs::write(root.join("install/personal.txt"), b"unknown").unwrap();
    run(&root, &bytes, &mut fake, false, vec![], vec![]).unwrap();
    run(&root, &bytes, &mut fake, true, vec![], vec![]).unwrap();
    assert!(!root.join("install/FrameScope.exe").exists());
    assert_eq!(
        fs::read(root.join("install/framescope-config.json")).unwrap(),
        b"settings"
    );
    run(&root, &bytes, &mut fake, false, vec![], vec![]).unwrap();
    let external = root.join("profile/FrameScopeMonitorData");
    fs::create_dir_all(&external).unwrap();
    fs::write(external.join("run.csv"), b"data").unwrap();
    run(
        &root,
        &bytes,
        &mut fake,
        true,
        vec![
            root.join("install/framescope-config.json"),
            external.join("run.csv"),
        ],
        vec![external.clone()],
    )
    .unwrap();
    assert!(!root.join("install/framescope-config.json").exists());
    assert!(!external.join("run.csv").exists());
    assert_eq!(
        fs::read(root.join("install/personal.txt")).unwrap(),
        b"unknown"
    );
}

#[test]
fn upgrade_removes_only_allowlisted_legacy_injected_osd_files() {
    let root = fixture();
    baseline(&root);
    let legacy = [
        "FrameScopeInProcessOSD-D3D11.dll",
        "FrameScopeInProcessOSD-D3D12.dll",
        "framescope-osd-inject.exe",
        "THIRD-PARTY-NOTICES-hudhook.md",
    ];
    for name in legacy {
        fs::write(root.join("install").join(name), format!("old {name}")).unwrap();
    }
    fs::write(root.join("install/user-note.txt"), b"preserve me").unwrap();
    let deletions = crate::installer::legacy_injected_osd_paths(&root.join("install"));
    assert_eq!(deletions.len(), legacy.len());
    let mut fake = Fake::default();

    run(&root, &payload(false), &mut fake, false, deletions, vec![]).unwrap();

    for name in legacy {
        assert!(!root.join("install").join(name).exists(), "legacy file remains: {name}");
    }
    assert_eq!(fs::read(root.join("install/user-note.txt")).unwrap(), b"preserve me");
    assert!(root.join("install/FrameScope.exe").exists());
}

#[test]
fn installer_payload_contract_no_longer_requires_injected_osd_artifacts() {
    let required = crate::installer::required_payload_files().unwrap();
    for name in [
        "FrameScopeInProcessOSD-D3D11.dll",
        "FrameScopeInProcessOSD-D3D12.dll",
        "framescope-osd-inject.exe",
        "THIRD-PARTY-NOTICES-hudhook.md",
    ] {
        assert!(!required.iter().any(|path| path == name), "obsolete injected OSD artifact remains required: {name}");
    }
}
#[test]
fn corrupt_stage_and_insufficient_space_never_stop_or_mutate() {
    for bad_hash in [true, false] {
        let root = fixture();
        baseline(&root);
        let mut fake = Fake::default();
        if !bad_hash {
            fake.space = Some(0)
        };
        assert!(run(&root, &payload(bad_hash), &mut fake, false, vec![], vec![]).is_err());
        assert_eq!(fake.stops, 0);
        old_unchanged(&root);
    }
}
fn custom_payload(files: Vec<(&str, Vec<u8>)>, extra: bool) -> Vec<u8> {
    let mut zip = zip::ZipWriter::new(std::io::Cursor::new(Vec::new()));
    let mut manifest = Vec::new();
    for (name, contents) in files {
        zip.start_file(name, zip::write::SimpleFileOptions::default())
            .unwrap();
        zip.write_all(&contents).unwrap();
        manifest.push(serde_json::json!({"path":name,"length":contents.len(),"sha256":format!("{:x}",Sha256::digest(&contents))}));
    }
    if extra {
        zip.start_file("unlisted.dll", zip::write::SimpleFileOptions::default())
            .unwrap();
        zip.write_all(b"unlisted").unwrap();
    }
    zip.start_file(MANIFEST, zip::write::SimpleFileOptions::default())
        .unwrap();
    let mut bom = vec![0xef, 0xbb, 0xbf];
    bom.extend(serde_json::to_vec(&serde_json::json!({"schema":1,"files":manifest})).unwrap());
    zip.write_all(&bom).unwrap();
    zip.finish().unwrap().into_inner()
}
#[test]
fn manifest_required_set_extra_zip_data_paths_crc_and_host_capability_are_pre_stop_guards() {
    let valid = || {
        vec![
            ("FrameScope.exe", b"host".to_vec()),
            ("FrameScopeMonitor.exe", b"monitor".to_vec()),
        ]
    };
    let mut cases = vec![
        custom_payload(vec![("FrameScope.exe", b"host".to_vec())], false),
        custom_payload(valid(), true),
    ];
    for protected in [
        "framescope-config.json",
        "framescope-runs/run.csv",
        "FrameScope Installer.exe",
        "../outside.dll",
    ] {
        let mut files = valid();
        files.push((protected, b"forbidden".to_vec()));
        cases.push(custom_payload(files, false));
    }
    let mut files = valid();
    files.push((
        "FrameScopeHostCapabilities.json",
        br#"{"schema":1,"requestExitProtocol":1,"hostSha256":"incorrect"}"#.to_vec(),
    ));
    cases.push(custom_payload(files, false));
    let mut corrupt = payload(false);
    let offset = 30 + "FrameScope.exe".len();
    corrupt[offset + 1] ^= 0x7f;
    cases.push(corrupt);
    for bytes in cases {
        let root = fixture();
        baseline(&root);
        let mut fake = Fake::default();
        assert!(run(&root, &bytes, &mut fake, false, vec![], vec![]).is_err());
        assert_eq!(fake.stops, 0);
        old_unchanged(&root);
    }
    let mut files = valid();
    let mut marker = vec![0xef, 0xbb, 0xbf];
    marker.extend(serde_json::to_vec(&serde_json::json!({"schema":1,"requestExitProtocol":1,"hostSha256":format!("{:x}",Sha256::digest(b"host"))})).unwrap());
    files.push(("FrameScopeHostCapabilities.json", marker));
    let root = fixture();
    let mut fake = Fake::default();
    run(
        &root,
        &custom_payload(files, false),
        &mut fake,
        false,
        vec![],
        vec![],
    )
    .unwrap();
    assert_eq!(fake.stops, 1);
}
#[test]
fn every_commit_checkpoint_restores_old_snapshot_and_new_files() {
    for phase in [
        "intent",
        "preflight",
        "before-backup",
        "after-backup",
        "before-replace",
        "after-replace",
        "before-side-effects",
        "apply",
        "after-side-effects",
        "before-committed",
        "commit-marker-write",
        "commit-marker-sync",
        "commit-marker-rename",
    ] {
        for index in if phase.contains("backup") || phase.contains("replace") {
            0..6
        } else {
            0..1
        } {
            let root = fixture();
            baseline(&root);
            let original = serde_json::json!({"registryType":7,"bytes":[1,2],"shortcut":[4,5]});
            let mut fake = Fake {
                system: original.clone(),
                fail: Some((phase, index)),
                ..Default::default()
            };
            assert!(
                run(&root, &payload(false), &mut fake, false, vec![], vec![]).is_err(),
                "{phase} {index}"
            );
            old_unchanged(&root);
            assert_eq!(fake.system, original);
            recover(&root.join("install"), &mut fake).unwrap();
            recover(&root.join("install"), &mut fake).unwrap();
        }
    }
}
#[test]
fn failed_restore_keeps_evidence_and_next_install_recovers() {
    let root = fixture();
    baseline(&root);
    let mut fake = Fake {
        fail: Some(("after-side-effects", 0)),
        restore_fails: true,
        ..Default::default()
    };
    let error = run(&root, &payload(false), &mut fake, false, vec![], vec![]).unwrap_err();
    assert!(error.contains("rollback incomplete"));
    let home = transaction_home(&root.join("install")).unwrap();
    assert!(home.join("journal.jsonl").exists());
    fake.restore_fails = false;
    fake.fail = None;
    recover(&root.join("install"), &mut fake).unwrap();
    old_unchanged(&root);
    run(&root, &payload(false), &mut fake, false, vec![], vec![]).unwrap();
}
#[test]
fn simultaneous_target_lock_refuses_without_mutation() {
    let root = fixture();
    baseline(&root);
    let home = transaction_home(&root.join("install")).unwrap();
    let lock = Lock::acquire(&home).unwrap();
    let mut fake = Fake::default();
    assert!(
        run(&root, &payload(false), &mut fake, false, vec![], vec![])
            .unwrap_err()
            .contains("locked")
    );
    old_unchanged(&root);
    assert_eq!(fake.stops, 0);
    drop(lock);
}
#[test]
fn canonical_aliases_share_one_lock_and_recovery_identity() {
    let root = fixture();
    baseline(&root);
    let target = root.join("install");
    let home = transaction_home(&target).unwrap();
    let lock = Lock::acquire(&home).unwrap();
    for alias in [
        target.to_string_lossy().to_uppercase(),
        target.to_string_lossy().replace('\\', "/"),
        format!("{}\\.\\install", root.display()),
        fs::canonicalize(&target)
            .unwrap()
            .to_string_lossy()
            .into_owned(),
    ] {
        let mut fake = Fake::default();
        let self_exe = root.join("setup.exe");
        fs::write(&self_exe, b"installer").unwrap();
        let error = execute(
            Request {
                target: Path::new(&alias),
                payload: &payload(false),
                self_exe: &self_exe,
                required: &[],
                uninstall: false,
                delete_data: false,
                deletions: vec![],
                data_roots: vec![],
                progress: &|_, _| {},
            },
            &mut fake,
        )
        .unwrap_err();
        assert!(
            error.contains("locked"),
            "alias escaped shared lock: {alias}: {error}"
        );
        assert_eq!(fake.stops, 0);
        old_unchanged(&root);
    }
    drop(lock);
}
#[test]
fn tampered_recovery_roots_and_backup_paths_refuse_external_mutation() {
    for attack in ["root", "backup"] {
        let root = fixture();
        baseline(&root);
        let mut fake = Fake {
            fail: Some(("after-side-effects", 0)),
            restore_fails: true,
            ..Default::default()
        };
        run(&root, &payload(false), &mut fake, false, vec![], vec![]).unwrap_err();
        let outside = root.join("foreign");
        fs::create_dir(&outside).unwrap();
        let sentinel = outside.join("sentinel.txt");
        fs::write(&sentinel, b"foreign file").unwrap();
        let original = hash(&sentinel).unwrap();
        let home = transaction_home(&root.join("install")).unwrap();
        let path = home.join("journal.jsonl");
        let bytes = fs::read(&path).unwrap();
        let newline = bytes.iter().position(|b| *b == b'\n').unwrap();
        let mut j: serde_json::Value = serde_json::from_slice(&bytes[..newline]).unwrap();
        if attack == "root" {
            j["roots"]
                .as_array_mut()
                .unwrap()
                .push(serde_json::json!(normalize(&outside).unwrap()));
        } else {
            j["changes"][0]["backup"] = serde_json::json!(normalize(&sentinel).unwrap());
        }
        let mut forged = serde_json::to_vec(&j).unwrap();
        forged.extend_from_slice(&bytes[newline..]);
        fs::write(&path, forged).unwrap();
        fake.restore_fails = false;
        fake.fail = None;
        assert!(recover(&root.join("install"), &mut fake).is_err());
        assert_eq!(hash(&sentinel).unwrap(), original);
        assert!(path.exists());
    }
}
#[test]
fn journal_directory_junction_is_rejected_without_external_writes() {
    use std::os::windows::process::CommandExt;
    let root = fixture();
    baseline(&root);
    let outside = root.join("foreign");
    fs::create_dir(&outside).unwrap();
    let sentinel = outside.join("sentinel.txt");
    fs::write(&sentinel, b"foreign").unwrap();
    let home = transaction_home(&root.join("install")).unwrap();
    fs::create_dir(&home).unwrap();
    let output = std::process::Command::new("cmd.exe")
        .args(["/C", "mklink", "/J"])
        .arg(home.join("journal.jsonl"))
        .arg(&outside)
        .creation_flags(0x08000000)
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let mut fake = Fake::default();
    assert!(
        run(&root, &payload(false), &mut fake, false, vec![], vec![])
            .unwrap_err()
            .contains("reparse")
    );
    assert_eq!(fake.stops, 0);
    assert_eq!(fs::read(sentinel).unwrap(), b"foreign");
    old_unchanged(&root);
}
#[test]
fn durable_header_and_each_journal_write_sync_failure_roll_back() {
    for phase in [
        "journal-header-write",
        "journal-header-sync",
        "journal-write",
        "journal-sync",
    ] {
        for index in if phase.starts_with("journal-header") {
            0..1
        } else {
            0..20
        } {
            let root = fixture();
            baseline(&root);
            let mut fake = Fake {
                fail: Some((phase, index)),
                ..Default::default()
            };
            assert!(
                run(&root, &payload(false), &mut fake, false, vec![], vec![]).is_err(),
                "{phase} {index}"
            );
            old_unchanged(&root);
            recover(&root.join("install"), &mut fake).unwrap();
        }
    }
}
#[test]
fn newly_created_nested_directories_are_rolled_back_at_each_directory_intent() {
    let bytes = custom_payload(
        vec![
            ("FrameScope.exe", b"new host".to_vec()),
            ("FrameScopeMonitor.exe", b"new monitor".to_vec()),
            ("new/deep/fresh.dll", b"DLL".to_vec()),
        ],
        false,
    );
    for phase in ["directory-before-create", "directory-after-create"] {
        for index in 0..2 {
            let root = fixture();
            baseline(&root);
            let mut fake = Fake {
                fail: Some((phase, index)),
                ..Default::default()
            };
            assert!(run(&root, &bytes, &mut fake, false, vec![], vec![]).is_err());
            old_unchanged(&root);
            assert!(!root.join("install/new").exists());
        }
    }
}
#[test]
fn no_delete_sharing_dll_refuses_before_first_target_change() {
    let root = fixture();
    baseline(&root);
    fs::write(root.join("install/new.dll"), b"old DLL").unwrap();
    let log = fs::File::create(root.join("game-child.log")).unwrap();
    let mut game = std::process::Command::new(std::env::current_exe().unwrap())
        .args([
            "--exact",
            "install_transaction::tests::locked_dll_child",
            "--ignored",
            "--nocapture",
        ])
        .env("FRAMESCOPE_TRANSACTION_TEST_ROOT", &root)
        .stdout(log.try_clone().unwrap())
        .stderr(log)
        .spawn()
        .unwrap();
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
    while !root.join("lock-ready").exists() {
        assert!(
            std::time::Instant::now() < deadline,
            "fixture readiness timeout"
        );
        assert!(
            game.try_wait().unwrap().is_none(),
            "fixture exited before ready"
        );
        std::thread::sleep(std::time::Duration::from_millis(10));
    }
    let mut fake = Fake::default();
    assert!(
        run(&root, &payload(false), &mut fake, false, vec![], vec![])
            .unwrap_err()
            .contains("occupied")
    );
    assert_eq!(
        fs::read(root.join("install/FrameScope.exe")).unwrap(),
        b"old FrameScope.exe"
    );
    assert_eq!(fs::read(root.join("install/new.dll")).unwrap(), b"old DLL");
    assert!(
        game.try_wait().unwrap().is_none(),
        "game fixture must remain alive"
    );
    fs::write(root.join("release-lock"), b"release").unwrap();
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
    loop {
        if let Some(status) = game.try_wait().unwrap() {
            assert!(status.success());
            break;
        }
        assert!(std::time::Instant::now() < deadline, "fixture exit timeout");
        std::thread::sleep(std::time::Duration::from_millis(10));
    }
}
#[test]
#[ignore = "Only spawned by the guarded DLL lock parent fixture"]
fn locked_dll_child() {
    use std::os::windows::fs::OpenOptionsExt;
    let root = PathBuf::from(std::env::var_os("FRAMESCOPE_TRANSACTION_TEST_ROOT").unwrap());
    assert!(fs::canonicalize(&root)
        .unwrap()
        .starts_with(fs::canonicalize(std::env::temp_dir()).unwrap()));
    assert!(root
        .file_name()
        .unwrap()
        .to_string_lossy()
        .starts_with("framescope-transaction-"));
    let handle = fs::OpenOptions::new()
        .read(true)
        .share_mode(1)
        .open(root.join("install/new.dll"))
        .unwrap();
    fs::write(root.join("lock-ready"), b"ready").unwrap();
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(15);
    while !root.join("release-lock").exists() {
        assert!(
            std::time::Instant::now() < deadline,
            "fixture holder timed out"
        );
        std::thread::sleep(std::time::Duration::from_millis(10));
    }
    drop(handle);
}
#[test]
fn stop_finalization_is_preserved_or_deleted_from_post_stop_plan() {
    for delete in [false, true] {
        let root = fixture();
        let bytes = payload(false);
        let mut fake = Fake::default();
        run(&root, &bytes, &mut fake, false, vec![], vec![]).unwrap();
        let data = root.join("install/framescope-runs");
        fs::create_dir(&data).unwrap();
        fs::write(data.join("run.csv"), b"first sample\n").unwrap();
        fake.final_data = Some(data.clone());
        run(
            &root,
            &bytes,
            &mut fake,
            true,
            vec![],
            if delete { vec![data.clone()] } else { vec![] },
        )
        .unwrap();
        if delete {
            assert!(!data.join("run.csv").exists());
            assert!(!data.join("new-report.html").exists());
        } else {
            assert_eq!(
                fs::read(data.join("run.csv")).unwrap(),
                b"first sample\nfinal sample\n"
            );
            assert_eq!(
                fs::read(data.join("new-report.html")).unwrap(),
                b"final report"
            );
        }
    }
}
#[test]
fn uninstall_failure_restores_finalized_data_including_report_created_during_stop() {
    let root = fixture();
    let bytes = payload(false);
    let mut fake = Fake::default();
    run(&root, &bytes, &mut fake, false, vec![], vec![]).unwrap();
    let data = root.join("profile/FrameScopeMonitorData");
    fs::create_dir_all(&data).unwrap();
    fs::write(data.join("run.csv"), b"first sample\n").unwrap();
    fake.final_data = Some(data.clone());
    fake.fail = Some(("after-side-effects", 0));
    assert!(run(&root, &bytes, &mut fake, true, vec![], vec![data.clone()]).is_err());
    assert_eq!(
        fs::read(data.join("run.csv")).unwrap(),
        b"first sample\nfinal sample\n"
    );
    assert_eq!(
        fs::read(data.join("new-report.html")).unwrap(),
        b"final report"
    );
    assert_eq!(
        fs::read(root.join("install/FrameScope.exe")).unwrap(),
        b"new FrameScope.exe"
    );
}

struct PersistentFake {
    path: PathBuf,
    crash: Option<(String, usize)>,
}
impl Runtime for PersistentFake {
    fn space(&mut self, _: &Path) -> Result<u64> {
        Ok(u64::MAX)
    }
    fn stop(&mut self, _: &Path, _: &Path) -> Result<()> {
        Ok(())
    }
    fn snapshot(&mut self) -> Result<serde_json::Value> {
        Ok(serde_json::json!(fs::read(&self.path).unwrap()))
    }
    fn apply(&mut self) -> Result<()> {
        fs::write(&self.path, b"new registry/shortcut bytes").map_err(|e| e.to_string())
    }
    fn restore(&mut self, value: &serde_json::Value) -> Result<()> {
        let bytes: Vec<u8> = serde_json::from_value(value.clone()).map_err(|e| e.to_string())?;
        fs::write(&self.path, bytes).map_err(|e| e.to_string())
    }
    fn checkpoint(&mut self, phase: &str, index: usize) -> Result<()> {
        if self
            .crash
            .as_ref()
            .is_some_and(|(p, i)| p == phase && *i == index)
        {
            std::process::exit(91);
        }
        Ok(())
    }
}
#[test]
#[ignore = "Only spawned by the guarded hard interruption parent fixture"]
fn hard_interruption_child() {
    let root = PathBuf::from(std::env::var_os("FRAMESCOPE_TRANSACTION_TEST_ROOT").unwrap());
    let canonical = fs::canonicalize(&root).unwrap();
    let temp = fs::canonicalize(std::env::temp_dir()).unwrap();
    assert!(canonical.starts_with(temp));
    assert!(root
        .file_name()
        .unwrap()
        .to_string_lossy()
        .starts_with("framescope-transaction-"));
    let phase = std::env::var("FRAMESCOPE_TRANSACTION_TEST_PHASE").unwrap();
    let index = std::env::var("FRAMESCOPE_TRANSACTION_TEST_INDEX")
        .unwrap()
        .parse()
        .unwrap();
    let mut fake = PersistentFake {
        path: root.join("fake-system.bin"),
        crash: Some((phase, index)),
    };
    let bytes = if fake.crash.as_ref().unwrap().0.starts_with("directory-") {
        custom_payload(
            vec![
                ("FrameScope.exe", b"new host".to_vec()),
                ("FrameScopeMonitor.exe", b"new monitor".to_vec()),
                ("new/deep/fresh.dll", b"DLL".to_vec()),
            ],
            false,
        )
    } else {
        payload(false)
    };
    execute(
        Request {
            target: &root.join("install"),
            payload: &bytes,
            self_exe: &root.join("setup.exe"),
            required: &["FrameScope.exe".into()],
            uninstall: false,
            delete_data: false,
            deletions: vec![],
            data_roots: vec![],
            progress: &|_, _| {},
        },
        &mut fake,
    )
    .unwrap();
    panic!("Expected a hard exit before completion");
}
#[test]
fn hard_interruption_recovers_every_file_window_and_side_effects_idempotently() {
    for phase in [
        "intent",
        "before-backup",
        "after-backup",
        "before-replace",
        "after-replace",
        "before-side-effects",
        "after-side-effects",
        "before-committed",
        "commit-marker-write",
        "commit-marker-sync",
        "commit-marker-rename",
        "journal-header-write",
        "journal-header-sync",
        "journal-write",
        "journal-sync",
        "directory-before-create",
        "directory-after-create",
    ] {
        for index in if phase.contains("backup") || phase.contains("replace") {
            0..6
        } else if phase == "journal-write" || phase == "journal-sync" {
            0..20
        } else if phase.starts_with("directory-") {
            0..2
        } else {
            0..1
        } {
            let root = fixture();
            baseline(&root);
            fs::write(root.join("setup.exe"), b"installer").unwrap();
            fs::write(root.join("fake-system.bin"), b"old registry/shortcut bytes").unwrap();
            let output = fs::File::create(root.join("child.log")).unwrap();
            let mut child = std::process::Command::new(std::env::current_exe().unwrap())
                .args([
                    "--exact",
                    "install_transaction::tests::hard_interruption_child",
                    "--ignored",
                    "--nocapture",
                ])
                .env("FRAMESCOPE_TRANSACTION_TEST_ROOT", &root)
                .env("FRAMESCOPE_TRANSACTION_TEST_PHASE", phase)
                .env("FRAMESCOPE_TRANSACTION_TEST_INDEX", index.to_string())
                .stdout(output.try_clone().unwrap())
                .stderr(output)
                .spawn()
                .unwrap();
            let deadline = std::time::Instant::now() + std::time::Duration::from_secs(15);
            let status = loop {
                if let Some(status) = child.try_wait().unwrap() {
                    break status;
                }
                assert!(
                    std::time::Instant::now() < deadline,
                    "fixture child timeout (no real process termination)"
                );
                std::thread::sleep(std::time::Duration::from_millis(10));
            };
            assert_eq!(
                status.code(),
                Some(91),
                "{phase} {index}; {}",
                fs::read_to_string(root.join("child.log")).unwrap()
            );
            let mut fake = PersistentFake {
                path: root.join("fake-system.bin"),
                crash: None,
            };
            recover(&root.join("install"), &mut fake).unwrap();
            recover(&root.join("install"), &mut fake).unwrap();
            old_unchanged(&root);
            assert!(!root.join("install/new").exists());
            assert_eq!(
                fs::read(root.join("fake-system.bin")).unwrap(),
                b"old registry/shortcut bytes"
            );
        }
    }
}
