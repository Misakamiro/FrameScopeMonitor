//! Shared install/uninstall transaction. Only OS operations cross Runtime;
//! staging, durable intents, rollback and recovery always use the real filesystem.
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    collections::BTreeSet,
    fs,
    io::{Read, Write},
    path::{Path, PathBuf},
};

pub type Result<T> = std::result::Result<T, String>;
pub const MANIFEST: &str = "FrameScopePayloadManifest.json";

pub trait Runtime {
    fn retain_previous(&self) -> bool { false }
    fn confirm_startup(&mut self) -> Result<()> { Ok(()) }
    fn abort_startup(&mut self) -> Result<()> { Ok(()) }
    fn space(&mut self, path: &Path) -> Result<u64>;
    fn stop(&mut self, target: &Path, helper: &Path) -> Result<()>;
    fn snapshot(&mut self) -> Result<serde_json::Value>;
    fn apply(&mut self) -> Result<()>;
    fn restore(&mut self, snapshot: &serde_json::Value) -> Result<()>;
    fn allowed_data_roots(&self) -> Vec<PathBuf> {
        vec![]
    }
    fn checkpoint(&mut self, _phase: &str, _index: usize) -> Result<()> {
        Ok(())
    }
}

#[derive(Deserialize)]
struct Manifest {
    schema: u32,
    files: Vec<ManifestFile>,
}
#[derive(Deserialize)]
struct ManifestFile {
    path: String,
    length: u64,
    sha256: String,
}

#[derive(Clone, Serialize, Deserialize)]
struct Change {
    destination: PathBuf,
    backup: PathBuf,
    source: Option<PathBuf>,
    old_hash: Option<String>,
    new_hash: Option<String>,
}
#[derive(Serialize, Deserialize)]
struct Journal {
    #[serde(default)]
    previous_build: Option<serde_json::Value>,
    schema: u32,
    target: PathBuf,
    work: PathBuf,
    roots: Vec<PathBuf>,
    changes: Vec<Change>,
    created_dirs: Vec<PathBuf>,
    snapshot: serde_json::Value,
    uninstall: bool,
}

pub fn hash(path: &Path) -> Result<String> {
    let mut file = fs::File::open(path).map_err(|e| e.to_string())?;
    let mut sha = Sha256::new();
    let mut buffer = [0u8; 65536];
    loop {
        let n = file.read(&mut buffer).map_err(|e| e.to_string())?;
        if n == 0 {
            break;
        }
        sha.update(&buffer[..n]);
    }
    Ok(format!("{:x}", sha.finalize()))
}

pub fn assert_path(path: &Path) -> Result<()> {
    crate::installer::validate_install_target(path)
}
pub fn normalize(path: &Path) -> Result<PathBuf> {
    assert_path(path)?;
    for ancestor in path.ancestors() {
        if ancestor.exists() {
            let canonical = io(fs::canonicalize(ancestor))?;
            let text = canonical.to_string_lossy();
            let canonical = if let Some(unc) = text.strip_prefix(r"\\?\UNC\") {
                PathBuf::from(format!(r"\\{unc}"))
            } else if let Some(plain) = text.strip_prefix(r"\\?\") {
                PathBuf::from(plain)
            } else {
                canonical
            };
            let relative = path.strip_prefix(ancestor).map_err(|e| e.to_string())?;
            let result = if relative.as_os_str().is_empty() {
                canonical
            } else {
                canonical.join(relative)
            };
            assert_path(&result)?;
            return Ok(result);
        }
    }
    Err("No canonical target ancestor".into())
}
fn io<T>(value: std::io::Result<T>) -> Result<T> {
    value.map_err(|e| e.to_string())
}
fn existing_parent(path: &Path) -> Result<PathBuf> {
    for parent in path.ancestors().skip(1) {
        if parent.is_dir() {
            assert_path(path)?;
            return Ok(parent.to_path_buf());
        }
    }
    Err("No safe existing parent directory".into())
}
fn transaction_home(target: &Path) -> Result<PathBuf> {
    let normalized = normalize(target)?;
    let target = normalized.as_path();
    let key = format!(
        "{:x}",
        Sha256::digest(target.to_string_lossy().to_lowercase().as_bytes())
    );
    let name = format!(".framescope-transaction-{}", &key[..24]);
    for parent in target.ancestors().skip(1) {
        if parent.join(&name).is_dir() {
            assert_path(&parent.join(&name))?;
            return Ok(parent.join(&name));
        }
    }
    Ok(existing_parent(target)?.join(name))
}

struct Lock(fs::File);
impl Lock {
    fn acquire(home: &Path) -> Result<Self> {
        assert_path(home)?;
        io(fs::create_dir_all(home))?;
        let mut options = fs::OpenOptions::new();
        options.read(true).write(true).create(true);
        assert_path(&home.join("lock"))?;
        #[cfg(windows)]
        {
            use std::os::windows::fs::OpenOptionsExt;
            options.share_mode(0);
        }
        Ok(Self(options.open(home.join("lock")).map_err(|e| {
            format!("Installation transaction is locked: {e}")
        })?))
    }
}

fn append(path: &Path, event: &str, runtime: &mut dyn Runtime, sequence: &mut usize) -> Result<()> {
    assert_path(path)?;
    let mut file = io(fs::OpenOptions::new().append(true).open(path))?;
    runtime.checkpoint("journal-write", *sequence)?;
    io(writeln!(file, "{event}"))?;
    runtime.checkpoint("journal-sync", *sequence)?;
    io(file.sync_all())?;
    *sequence += 1;
    Ok(())
}
fn read_journal(path: &Path) -> Result<(Journal, bool)> {
    assert_path(path)?;
    let bytes = io(fs::read(path))?;
    let first = bytes
        .iter()
        .position(|b| *b == b'\n')
        .ok_or("Incomplete transaction header")?;
    let journal: Journal = serde_json::from_slice(&bytes[..first]).map_err(|e| e.to_string())?;
    // A torn tail cannot contain a committed record. Durable header owns every
    // intent, so recovery never depends on a completion record being present.
    let marker = path.with_extension("committed");
    assert_path(&marker)?;
    let committed = if marker.exists() {
        io(fs::read(marker))? == format!("{:x}", Sha256::digest(&bytes[..first])).as_bytes()
    } else {
        false
    };
    Ok((journal, committed))
}
pub(crate) fn is_transaction_target(target: &Path) -> Result<bool> {
    let normalized = normalize(target)?;
    let path = transaction_home(&normalized)?.join("journal.jsonl");
    assert_path(&path)?;
    if !path.exists() {
        return Ok(false);
    }
    let (journal, _) = read_journal(&path)?;
    Ok(journal.schema == 1 && same_path(&journal.target, &normalized))
}
fn durable_journal(path: &Path, journal: &Journal, runtime: &mut dyn Runtime) -> Result<()> {
    assert_path(path)?;
    let temporary = journal.work.join("journal-header.tmp");
    let mut file = io(fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temporary))?;
    runtime.checkpoint("journal-header-write", 0)?;
    serde_json::to_writer(&mut file, journal).map_err(|e| e.to_string())?;
    io(file.write_all(b"\n"))?;
    runtime.checkpoint("journal-header-sync", 0)?;
    io(file.sync_all())?;
    drop(file);
    io(fs::rename(temporary, path))
}

fn path_key(path: &Path) -> String {
    path.to_string_lossy().replace('/', "\\").to_lowercase()
}
fn same_path(path: &Path, other: &Path) -> bool {
    path_key(path) == path_key(other)
}
fn within(path: &Path, root: &Path) -> bool {
    let path = path_key(path);
    let root = path_key(root);
    path == root || path.starts_with(&(root.trim_end_matches('\\').to_string() + "\\"))
}
fn validate_journal(
    journal: &Journal,
    target: &Path,
    home: &Path,
    runtime: &dyn Runtime,
) -> Result<()> {
    if journal.schema != 1
        || !same_path(&journal.target, target)
        || journal.work.parent() != Some(home)
    {
        return Err("Transaction journal identity mismatch".into());
    }
    assert_path(&journal.work)?;
    let allowed = runtime
        .allowed_data_roots()
        .into_iter()
        .map(|r| normalize(&r))
        .collect::<Result<Vec<_>>>()?;
    for root in &journal.roots {
        assert_path(root)?;
        if !same_path(root, target)
            && !within(root, target)
            && !allowed.iter().any(|r| same_path(r, root))
        {
            return Err("Journal data root is outside current explicit product-data plan".into());
        }
    }
    for (index, change) in journal.changes.iter().enumerate() {
        assert_path(&change.destination)?;
        assert_path(&change.backup)?;
        if !journal.roots.iter().any(|r| within(&change.destination, r)) {
            return Err("Transaction destination escapes declared roots".into());
        }
        let expected_backup = if within(&change.destination, target) {
            journal.work.join(format!("backup-{index}"))
        } else {
            change
                .destination
                .parent()
                .ok_or("Invalid backup parent")?
                .join(format!(
                    ".framescope-backup-{}-{index}",
                    journal
                        .work
                        .file_name()
                        .ok_or("Missing transaction identity")?
                        .to_string_lossy()
                ))
        };
        if change.backup != expected_backup {
            return Err("Transaction backup is outside its safe volume".into());
        }
        if let Some(source) = &change.source {
            assert_path(source)?;
            if !within(&change.destination, target)
                || *source
                    != journal.work.join("stage").join(
                        change
                            .destination
                            .strip_prefix(&journal.target)
                            .map_err(|e| e.to_string())?,
                    )
            {
                return Err("Transaction stage escapes work directory".into());
            }
        }
    }
    for dir in &journal.created_dirs {
        assert_path(dir)?;
        if !within(dir, target) {
            return Err("Directory intent escapes target".into());
        }
    }
    Ok(())
}

fn matches(path: &Path, expected: &Option<String>) -> Result<bool> {
    match expected {
        Some(expected) => Ok(path.is_file() && hash(path)? == *expected),
        None => Ok(!path.exists()),
    }
}
fn rollback(journal: &Journal, log: &Path, runtime: &mut dyn Runtime) -> Result<()> {
    // Side effects may have partially applied even when no completion was logged.
    let events = io(fs::read(log))?;
    if events
        .split_inclusive(|b| *b == b'\n')
        .any(|line| line == b"side-effects-intent\n")
    {
        runtime.restore(&journal.snapshot)?;
    }
    for change in journal.changes.iter().rev() {
        assert_path(&change.destination)?;
        assert_path(&change.backup)?;
        if change.backup.exists() {
            if !matches(&change.backup, &change.old_hash)? {
                return Err("Backup identity changed; recovery requires intervention".into());
            }
            if change.destination.exists() {
                if !matches(&change.destination, &change.new_hash)? {
                    return Err("Destination identity changed; recovery preserves evidence".into());
                }
                io(fs::remove_file(&change.destination))?;
            }
            io(fs::rename(&change.backup, &change.destination))?;
        } else if !matches(&change.destination, &change.old_hash)? {
            if change.old_hash.is_none() && matches(&change.destination, &change.new_hash)? {
                io(fs::remove_file(&change.destination))?;
            } else {
                return Err(format!(
                    "Missing backup for {}",
                    change.destination.display()
                ));
            }
        }
    }
    for dir in journal.created_dirs.iter().rev() {
        if dir.exists() {
            io(fs::remove_dir(dir))?;
        }
    }
    Ok(())
}
fn clean_committed(journal: &Journal) -> Result<()> {
    for change in &journal.changes {
        if change.backup.exists() {
            io(fs::remove_file(&change.backup))?;
        }
    }
    if journal.work.exists() {
        io(fs::remove_dir_all(&journal.work))?;
    }
    if journal.uninstall {
        let mut directories = BTreeSet::new();
        for change in &journal.changes {
            for parent in change.destination.ancestors().skip(1) {
                if journal.roots.iter().any(|root| within(parent, root)) {
                    directories.insert(parent.to_path_buf());
                } else {
                    break;
                }
            }
        }
        let mut directories: Vec<_> = directories.into_iter().collect();
        directories.sort_by_key(|p| std::cmp::Reverse(p.components().count()));
        for directory in directories {
            assert_path(&directory)?;
            match fs::remove_dir(directory) {
                Ok(()) => (),
                Err(e)
                    if e.kind() == std::io::ErrorKind::NotFound
                        || e.raw_os_error() == Some(145) =>
                {
                    ()
                }
                Err(e) => return Err(e.to_string()),
            }
        }
    }
    Ok(())
}
fn finish_committed(journal:&Journal,home:&Path,runtime:&dyn Runtime)->Result<()> {
    if journal.previous_build.is_none() || journal.uninstall {return clean_committed(journal);}
    // Publish the new pointer before retiring any older recovery material.
    let previous_path=home.join("previous.json");
    let old=if previous_path.exists(){Some(serde_json::from_slice::<Journal>(&io(fs::read(&previous_path))?).map_err(|e|e.to_string())?)}else{None};
    if let Some(old)=&old {validate_journal(old,&journal.target,home,runtime)?;}
    for change in &journal.changes {
        if change.old_hash.is_some() && !matches(&change.backup,&change.old_hash)? {return Err("Previous program backup failed verification".into());}
    }
    framescope_update::atomic_json(&previous_path,journal)?;
    framescope_update::atomic_json(&journal.target.join("FrameScopePreviousVersion.json"),journal.previous_build.as_ref().unwrap())?;
    let helper=journal.target.join("FrameScope Installer.exe");
    framescope_update::atomic_json(&journal.target.join("FrameScopeRecoveryHost.json"),&framescope_update::Package {
        name:"FrameScope Installer.exe".into(),size:io(fs::metadata(&helper))?.len(),sha256:hash(&helper)?})?;
    if let Some(old)=old {if old.work!=journal.work {clean_committed(&old)?;}}
    Ok(())
}

pub struct PreviousPayload {pub payload:Vec<u8>,pub installer:PathBuf,pub deletions:Vec<PathBuf>,pub build:serde_json::Value}
pub fn previous_payload(target:&Path,runtime:&dyn Runtime)->Result<PreviousPayload> {
    let target=normalize(target)?;
    let home=transaction_home(&target)?;
    let journal:Journal=serde_json::from_slice(&io(fs::read(home.join("previous.json")))?).map_err(|e|e.to_string())?;
    validate_journal(&journal,&target,&home,runtime)?;
    if journal.uninstall {return Err("Uninstall journal cannot be restored as a version".into());}
    let build=journal.previous_build.clone().ok_or("No previous 2.x version")?;
    let mut zip=zip::ZipWriter::new(std::io::Cursor::new(Vec::new()));
    let mut files=Vec::new();let mut installer=None;let mut deletions=Vec::new();
    for change in &journal.changes {
        let relative=change.destination.strip_prefix(&target).map_err(|_|"Recovery entry escapes program directory")?;
        let name=relative.to_string_lossy().replace('\\',"/");
        if change.old_hash.is_none(){deletions.push(change.destination.clone());continue;}
        if !matches(&change.backup,&change.old_hash)? {return Err(format!("Previous backup hash mismatch: {name}"));}
        if name=="FrameScope Installer.exe" {installer=Some(change.backup.clone());continue;}
        if name==MANIFEST {continue;}
        let bytes=io(fs::read(&change.backup))?;
        zip.start_file(&name,zip::write::SimpleFileOptions::default()).map_err(|e|e.to_string())?;
        io(zip.write_all(&bytes))?;
        files.push(serde_json::json!({"path":name,"length":bytes.len(),"sha256":change.old_hash}));
    }
    // A first fixture/manual 2.x installation may not have a generated installer
    // backup. Production updates require it before normal shutdown.
    let installer=installer.ok_or("Previous version has no independent installer backup")?;
    zip.start_file(MANIFEST,zip::write::SimpleFileOptions::default()).map_err(|e|e.to_string())?;
    io(zip.write_all(&serde_json::to_vec(&serde_json::json!({"schema":1,"files":files})).map_err(|e|e.to_string())?))?;
    Ok(PreviousPayload {payload:zip.finish().map_err(|e|e.to_string())?.into_inner(),installer,deletions,build})
}
pub fn discard_previous(target:&Path,runtime:&dyn Runtime)->Result<()> {
    let target=normalize(target)?;let home=transaction_home(&target)?;let path=home.join("previous.json");
    if !path.exists(){return Ok(());}
    let journal:Journal=serde_json::from_slice(&io(fs::read(&path))?).map_err(|e|e.to_string())?;
    validate_journal(&journal,&target,&home,runtime)?;
    clean_committed(&journal)?;
    io(fs::remove_file(path))?;
    let marker=target.join("FrameScopePreviousVersion.json");if marker.exists(){io(fs::remove_file(marker))?;}Ok(())
}
/// One-time offline program backup for migration from the old installation.
/// User data stays in place; no file is removed or replaced by this operation.
pub fn backup_legacy(target:&Path)->Result<Option<PathBuf>> {
    if !target.join("FrameScope.exe").exists()||target.join("FrameScopeBuildInfo.json").exists(){return Ok(None);}
    let target=normalize(target)?;
    fn collect(root:&Path,dir:&Path,files:&mut Vec<PathBuf>)->Result<()> {
        assert_path(dir)?;
        for item in io(fs::read_dir(dir))? {
            let path=io(item)?.path();assert_path(&path)?;
            let relative=path.strip_prefix(root).map_err(|e|e.to_string())?;
            let first=relative.components().next().ok_or("Missing legacy entry")?.as_os_str().to_string_lossy();
            if crate::installer::LOCAL_DATA_FILES.iter().chain(crate::installer::LOCAL_DATA_DIRS).any(|s|first.eq_ignore_ascii_case(s)){continue;}
            if path.is_dir(){collect(root,&path,files)?;}else{files.push(path);}
        }Ok(())
    }
    let mut files=Vec::new();collect(&target,&target,&mut files)?;
    let home=transaction_home(&target)?;assert_path(&home)?;io(fs::create_dir_all(&home))?;
    let backup=home.join(format!("offline-1x-{}.zip",std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_err(|e|e.to_string())?.as_nanos()));
    let mut zip=zip::ZipWriter::new(io(fs::OpenOptions::new().create_new(true).write(true).open(&backup))?);
    let mut inventory=Vec::new();
    for path in files {
        let name=path.strip_prefix(&target).map_err(|e|e.to_string())?.to_string_lossy().replace('\\',"/");
        let before=hash(&path)?;
        zip.start_file(&name,zip::write::SimpleFileOptions::default()).map_err(|e|e.to_string())?;
        io(std::io::copy(&mut io(fs::File::open(&path))?,&mut zip))?;
        if hash(&path)?!=before{return Err("Legacy program changed during offline backup; installation cancelled".into());}
        inventory.push(serde_json::json!({"path":name,"sha256":before}));
    }
    zip.start_file("FrameScopeOfflineBackup.json",zip::write::SimpleFileOptions::default()).map_err(|e|e.to_string())?;
    io(zip.write_all(&serde_json::to_vec(&inventory).map_err(|e|e.to_string())?))?;
    io(zip.finish().map_err(|e|e.to_string())?.sync_all())?;
    Ok(Some(backup))
}
fn recover_locked(target: &Path, home: &Path, runtime: &mut dyn Runtime) -> Result<()> {
    let path = home.join("journal.jsonl");
    assert_path(&path)?;
    assert_path(&path.with_extension("committed"))?;
    if !path.exists() {
        if path.with_extension("committed").exists() {
            io(fs::remove_file(path.with_extension("committed")))?;
        }
        return Ok(());
    }
    let (journal, committed) = read_journal(&path)?;
    validate_journal(&journal, target, home, runtime)?;
    if !committed {
        rollback(&journal, &path, runtime)
            .map_err(|e| format!("Recovery incomplete; journal and backups retained: {e}"))?;
    }
    if committed {finish_committed(&journal,home,runtime)?;} else {clean_committed(&journal)?;}
    io(fs::remove_file(&path))?;
    if path.with_extension("committed").exists() {
        io(fs::remove_file(path.with_extension("committed")))?;
    }
    Ok(())
}

fn commit_marker(log: &Path, work: &Path, runtime: &mut dyn Runtime) -> Result<()> {
    assert_path(log)?;
    assert_path(&log.with_extension("committed"))?;
    let bytes = io(fs::read(log))?;
    let first = bytes
        .iter()
        .position(|b| *b == b'\n')
        .ok_or("Incomplete commit header")?;
    let temporary = work.join("commit.tmp");
    let mut file = io(fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temporary))?;
    runtime.checkpoint("commit-marker-write", 0)?;
    io(file.write_all(format!("{:x}", Sha256::digest(&bytes[..first])).as_bytes()))?;
    runtime.checkpoint("commit-marker-sync", 0)?;
    io(file.sync_all())?;
    drop(file);
    runtime.checkpoint("commit-marker-rename", 0)?;
    io(fs::rename(temporary, log.with_extension("committed")))
}
pub fn recover(target: &Path, runtime: &mut dyn Runtime) -> Result<()> {
    let normalized = normalize(target)?;
    let target = normalized.as_path();
    let home = transaction_home(target)?;
    let lock = Lock::acquire(&home)?;
    let result = recover_locked(target, &home, runtime);
    drop(lock);
    result
}
pub fn interrupted_helper(target:&Path,runtime:&dyn Runtime)->Result<Option<PathBuf>> {
    let target=normalize(target)?;let home=transaction_home(&target)?;let path=home.join("journal.jsonl");
    if !path.exists(){return Ok(None);}
    let (journal,_)=read_journal(&path)?;validate_journal(&journal,&target,&home,runtime)?;
    let helper=journal.changes.iter().find(|c|c.destination==target.join("FrameScopeUninstaller.exe")).ok_or("Recovery journal lacks normal-stop helper")?;
    for (path,expected) in [(Some(&helper.destination),&helper.new_hash),(helper.source.as_ref(),&helper.new_hash),(Some(&helper.backup),&helper.old_hash)] {
        if let Some(path)=path {if expected.is_some()&&matches(path,expected)? {return Ok(Some(path.clone()));}}
    }
    Err("Verified shutdown helper unavailable; recovery material preserved".into())
}

pub fn stage_payload(
    target: &Path,
    stage: &Path,
    payload: &[u8],
    required: &[String],
    progress: &dyn Fn(u32, &str),
) -> Result<Vec<PathBuf>> {
    let paths = crate::installer::validate_payload_paths(target, payload)?;
    let mut zip = zip::ZipArchive::new(std::io::Cursor::new(payload)).map_err(|e| e.to_string())?;
    let mut manifest_bytes = Vec::new();
    zip.by_name(MANIFEST)
        .map_err(|e| format!("Missing payload manifest: {e}"))?
        .read_to_end(&mut manifest_bytes)
        .map_err(|e| e.to_string())?;
    if manifest_bytes.starts_with(&[0xef, 0xbb, 0xbf]) {
        manifest_bytes.drain(..3);
    }
    let manifest: Manifest = serde_json::from_slice(&manifest_bytes)
        .map_err(|e| format!("Invalid payload manifest: {e}"))?;
    if manifest.schema != 1 {
        return Err("Unsupported payload manifest schema".into());
    }
    let mut expected = BTreeSet::new();
    for file in &manifest.files {
        crate::installer::checked_relative(Path::new(&file.path))?;
        let first = file.path.split(['/', '\\']).next().unwrap_or("");
        if first.eq_ignore_ascii_case("FrameScope Installer.exe") {
            return Err("Payload cannot replace generated uninstaller identity".into());
        }
        if crate::installer::LOCAL_DATA_FILES
            .iter()
            .chain(crate::installer::LOCAL_DATA_DIRS)
            .any(|n| first.eq_ignore_ascii_case(n))
        {
            return Err("Payload attempts to replace user data".into());
        }
        if !expected.insert(file.path.replace('\\', "/").to_lowercase())
            || file.sha256.len() != 64
            || !file.sha256.bytes().all(|b| b.is_ascii_hexdigit())
        {
            return Err("Invalid/duplicate manifest identity".into());
        }
    }
    for name in required {
        if !expected.contains(&name.replace('\\', "/").to_lowercase()) {
            return Err(format!("Required payload file missing: {name}"));
        }
    }
    let mut actual = BTreeSet::new();
    for index in 0..zip.len() {
        let file = zip.by_index(index).map_err(|e| e.to_string())?;
        if !file.is_dir() && file.name() != MANIFEST {
            actual.insert(file.name().replace('\\', "/").to_lowercase());
        }
    }
    if actual != expected {
        return Err("Manifest file set differs from ZIP".into());
    }
    io(fs::create_dir_all(stage))?;
    let mut output = Vec::new();
    for (index, relative) in paths.iter().enumerate() {
        let mut file = zip.by_index(index).map_err(|e| e.to_string())?;
        if file.is_dir() {
            continue;
        }
        let destination = stage.join(relative);
        assert_path(&destination)?;
        io(fs::create_dir_all(
            destination.parent().ok_or("Missing stage parent")?,
        ))?;
        let mut dest = io(fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&destination))?;
        io(std::io::copy(&mut file, &mut dest))?;
        io(dest.sync_all())?;
        if relative != Path::new(MANIFEST) {
            let entry = manifest
                .files
                .iter()
                .find(|f| {
                    f.path
                        .replace('\\', "/")
                        .eq_ignore_ascii_case(&relative.to_string_lossy().replace('\\', "/"))
                })
                .ok_or("Missing manifest entry")?;
            if io(fs::metadata(&destination))?.len() != entry.length
                || !hash(&destination)?.eq_ignore_ascii_case(&entry.sha256)
            {
                return Err(format!(
                    "Payload length/SHA256 mismatch: {}",
                    relative.display()
                ));
            }
        }
        output.push(relative.clone());
        progress((index + 1) as u32, &relative.to_string_lossy());
    }
    let capabilities = stage.join("FrameScopeHostCapabilities.json");
    if capabilities.exists() {
        let mut bytes = io(fs::read(capabilities))?;
        if bytes.starts_with(&[0xef, 0xbb, 0xbf]) {
            bytes.drain(..3);
        }
        let marker: serde_json::Value =
            serde_json::from_slice(&bytes).map_err(|e| e.to_string())?;
        if marker["schema"] != 1
            || marker["requestExitProtocol"] != 1
            || !marker["hostSha256"].as_str().is_some_and(|h| {
                hash(&stage.join("FrameScope.exe"))
                    .is_ok_and(|actual| actual.eq_ignore_ascii_case(h))
            })
        {
            return Err("Host shutdown capability/hash mismatch".into());
        }
    }
    Ok(output)
}

fn occupancy(path: &Path) -> Result<()> {
    assert_path(path)?;
    if !path.exists() {
        return Ok(());
    }
    let mut options = fs::OpenOptions::new();
    options.read(true);
    #[cfg(windows)]
    {
        use std::os::windows::fs::OpenOptionsExt;
        // DELETE access with all sharing allowed still conflicts with a game
        // loader handle that omitted FILE_SHARE_DELETE, without changing the file.
        options.access_mode(0x00010000).share_mode(7);
    }
    let handle = options
        .open(path)
        .map_err(|e| format!("File occupied; target untouched: {}: {e}", path.display()))?;
    drop(handle);
    Ok(())
}

pub struct Request<'a> {
    pub target: &'a Path,
    pub payload: &'a [u8],
    pub self_exe: &'a Path,
    pub required: &'a [String],
    pub uninstall: bool,
    pub delete_data: bool,
    pub deletions: Vec<PathBuf>,
    pub data_roots: Vec<PathBuf>,
    pub progress: &'a dyn Fn(u32, &str),
}

fn data_files(root: &Path, files: &mut BTreeSet<PathBuf>) -> Result<()> {
    assert_path(root)?;
    if !root.exists() {
        return Ok(());
    }
    if root.is_file() {
        files.insert(root.to_path_buf());
        return Ok(());
    }
    for entry in io(fs::read_dir(root))? {
        data_files(&io(entry)?.path(), files)?;
    }
    Ok(())
}
fn collect_data(request: &Request<'_>, files: &mut BTreeSet<PathBuf>) -> Result<()> {
    if request.uninstall && request.delete_data {
        for name in crate::installer::LOCAL_DATA_FILES
            .iter()
            .chain(crate::installer::LOCAL_DATA_DIRS)
        {
            data_files(&request.target.join(name), files)?;
        }
        for root in &request.data_roots {
            data_files(root, files)?;
        }
    }
    Ok(())
}

pub fn execute(request: Request<'_>, runtime: &mut dyn Runtime) -> Result<()> {
    let normalized = normalize(request.target).map_err(|error| format!("install target normalization failed: {error}"))?;
    let target = normalized.as_path();
    let request = Request {
        target,
        payload: request.payload,
        self_exe: request.self_exe,
        required: request.required,
        uninstall: request.uninstall,
        delete_data: request.delete_data,
        progress: request.progress,
        data_roots: request
            .data_roots
            .into_iter()
            .map(|r| normalize(&r).map_err(|error| format!("data-root normalization failed: {error}")))
            .collect::<Result<Vec<_>>>()?,
        deletions: request
            .deletions
            .into_iter()
            .map(|r| normalize(&r).map_err(|error| format!("deletion-path normalization failed: {error}")))
            .collect::<Result<Vec<_>>>()?,
    };
    if normalize(request.self_exe).map_err(|error| format!("installer path normalization failed: {error}"))?.starts_with(target) {
        return Err("Installer must relaunch outside target before changing files".into());
    }
    let home = transaction_home(target).map_err(|error| format!("transaction-root resolution failed: {error}"))?;
    let lock = Lock::acquire(&home)?;
    recover_locked(target, &home, runtime)?;
    let id = format!(
        "{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map_err(|e| e.to_string())?
            .as_nanos()
    );
    let work = home.join(&id);
    io(fs::create_dir(&work))?;
    let stage = work.join("stage");
    let before_commit = (|| {
        let mut initial_data = BTreeSet::new();
        collect_data(&request, &mut initial_data)?;
        let mut zip = zip::ZipArchive::new(std::io::Cursor::new(request.payload))
            .map_err(|e| e.to_string())?;
        let mut estimate = io(fs::metadata(request.self_exe))?
            .len()
            .checked_add(1024 * 1024)
            .ok_or("Space overflow")?;
        for index in 0..zip.len() {
            let file = zip.by_index(index).map_err(|e| e.to_string())?;
            estimate = estimate.checked_add(file.size()).ok_or("Space overflow")?;
            let relative = PathBuf::from(file.name().replace('\\', "/"));
            crate::installer::checked_relative(&relative)?;
            let old = target.join(relative);
            assert_path(&old)?;
            if old.is_file() {
                estimate = estimate
                    .checked_add(io(fs::metadata(old))?.len())
                    .ok_or("Space overflow")?;
            }
        }
        if runtime.space(&existing_parent(target)?)? < estimate {
            return Err("Insufficient space for staging and rollback".into());
        }
        let paths = stage_payload(
            target,
            &stage,
            request.payload,
            request.required,
            request.progress,
        )?;
        runtime.checkpoint("staged", 0)?;
        // Normal shutdown can append telemetry or publish a final report. The
        // deletion set and old identities must be observed after it completes.
        runtime.stop(target, &stage.join("FrameScopeUninstaller.exe"))?;
        runtime.checkpoint("stopped", 0)?;
        let mut names = BTreeSet::new();
        collect_data(&request, &mut names)?;
        let mut changes = Vec::new();
        let mut roots = vec![target.to_path_buf()];
        roots.extend(request.data_roots);
        for root in &roots {
            assert_path(root)?;
        }
        if !request.uninstall {
            let uninstaller = stage.join("FrameScope Installer.exe");
            io(fs::copy(request.self_exe, &uninstaller))?;
            if hash(request.self_exe)? != hash(&uninstaller)? {
                return Err("Uninstaller copy identity mismatch".into());
            }
        }
        for relative in paths
            .iter()
            .chain(std::iter::once(&PathBuf::from("FrameScope Installer.exe")))
        {
            let destination = target.join(relative);
            names.insert(destination);
        }
        for path in request.deletions {
            names.insert(path);
        }
        let mut bytes = 1024u64 * 1024;
        for destination in names {
            assert_path(&destination)?;
            if !roots.iter().any(|r| within(&destination, r)) {
                return Err("Deletion escapes explicit scope".into());
            }
            if destination.is_dir() {
                return Err("File transaction cannot overwrite directory".into());
            }
            let old_hash = if destination.exists() {
                bytes = bytes
                    .checked_add(io(fs::metadata(&destination))?.len())
                    .ok_or("Space overflow")?;
                Some(hash(&destination)?)
            } else {
                None
            };
            let source = if !request.uninstall && destination.starts_with(target) {
                let candidate = stage.join(
                    destination
                        .strip_prefix(target)
                        .map_err(|e| e.to_string())?,
                );
                if candidate.is_file() {
                    bytes = bytes
                        .checked_add(io(fs::metadata(&candidate))?.len())
                        .ok_or("Space overflow")?;
                    Some(candidate)
                } else {
                    None
                }
            } else {
                None
            };
            let new_hash = source.as_ref().map(|p| hash(p)).transpose()?;
            let backup = if destination.starts_with(target) {
                work.join(format!("backup-{}", changes.len()))
            } else {
                destination
                    .parent()
                    .ok_or("Missing external data parent")?
                    .join(format!(".framescope-backup-{id}-{}", changes.len()))
            };
            changes.push(Change {
                destination,
                backup,
                source,
                old_hash,
                new_hash,
            });
        }
        if runtime.space(&existing_parent(target)?)? < bytes {
            return Err("Insufficient space for staging and rollback".into());
        }
        // External backups are same-volume renames and require no data copy.
        let snapshot = runtime.snapshot()?;
        let mut directories = BTreeSet::new();
        for change in &changes {
            if change.source.is_some() {
                for dir in change
                    .destination
                    .ancestors()
                    .skip(1)
                    .take_while(|p| within(p, target))
                {
                    if !dir.exists() {
                        directories.insert(dir.to_path_buf());
                    }
                }
            }
        }
        let mut created_dirs: Vec<_> = directories.into_iter().collect();
        created_dirs.sort_by_key(|p| p.components().count());
        let journal = Journal {
            previous_build: if runtime.retain_previous() && !request.uninstall {
                let path=target.join("FrameScopeBuildInfo.json");
                if path.exists(){
                    let build:serde_json::Value=serde_json::from_slice(&io(fs::read(path))?).map_err(|e|e.to_string())?;
                    if build["version"].as_str().and_then(|s|s.split('.').next()?.parse::<u64>().ok()).unwrap_or(0)<2 {None}else{Some(build)}
                }else{None}
            } else {None},
            schema: 1,
            target: target.to_path_buf(),
            work: work.clone(),
            roots,
            changes,
            created_dirs,
            snapshot,
            uninstall: request.uninstall,
        };
        validate_journal(&journal, target, &home, runtime)?;
        for change in &journal.changes {
            occupancy(&change.destination)?;
        }
        runtime.checkpoint("preflight", 0)?;
        durable_journal(&home.join("journal.jsonl"), &journal, runtime)?;
        Ok(journal)
    })();
    let journal = match before_commit {
        Ok(j) => j,
        Err(error) => {
            if !home.join("journal.jsonl").exists() {
                let _ = fs::remove_dir_all(&work);
            }
            return Err(error);
        }
    };
    let log = home.join("journal.jsonl");
    let commit = (|| {
        let mut sequence = 0;
        runtime.checkpoint("intent", 0)?;
        for (index, dir) in journal.created_dirs.iter().enumerate() {
            append(&log, "directory-intent", runtime, &mut sequence)?;
            runtime.checkpoint("directory-before-create", index)?;
            assert_path(dir)?;
            io(fs::create_dir(dir))?;
            runtime.checkpoint("directory-after-create", index)?;
        }
        for (index, change) in journal.changes.iter().enumerate() {
            runtime.checkpoint("before-backup", index)?;
            append(
                &log,
                &format!("backup-intent {index}"),
                runtime,
                &mut sequence,
            )?;
            assert_path(&change.destination)?;
            if !matches(&change.destination, &change.old_hash)? {
                return Err("Destination changed after planning".into());
            }
            if change.old_hash.is_some() {
                io(fs::rename(&change.destination, &change.backup))?;
            }
            runtime.checkpoint("after-backup", index)?;
            append(
                &log,
                &format!("replace-intent {index}"),
                runtime,
                &mut sequence,
            )?;
            runtime.checkpoint("before-replace", index)?;
            if let Some(source) = &change.source {
                io(fs::rename(source, &change.destination))?;
            }
            runtime.checkpoint("after-replace", index)?;
            append(
                &log,
                &format!("file-complete {index}"),
                runtime,
                &mut sequence,
            )?;
        }
        runtime.checkpoint("before-side-effects", 0)?;
        append(&log, "side-effects-intent", runtime, &mut sequence)?;
        runtime.apply()?;
        runtime.checkpoint("after-side-effects", 0)?;
        append(&log, "side-effects-complete", runtime, &mut sequence)?;
        if !journal.uninstall { runtime.confirm_startup()?; }
        runtime.checkpoint("before-committed", 0)?;
        commit_marker(&log, &work, runtime)?;
        Ok(())
    })();
    if let Err(error) = commit {
        if let Err(stop)=runtime.abort_startup(){return Err(format!("{error}; rollback incomplete; candidate could not drain; journal retained: {stop}"));}
        match rollback(&journal, &log, runtime) {
            Ok(()) => {
                clean_committed(&journal)?;
                io(fs::remove_file(&log))?;
                return Err(error);
            }
            Err(recovery) => {
                return Err(format!(
                    "{error}; rollback incomplete; journal/backup retained: {recovery}"
                ))
            }
        }
    }
    finish_committed(&journal,&home,runtime)
        .map_err(|e| format!("Commit complete; cleanup incomplete; journal retained: {e}"))?;
    io(fs::remove_file(&log))?;
    io(fs::remove_file(log.with_extension("committed")))?;
    drop(lock);
    Ok(())
}

#[cfg(test)]
#[path = "install_transaction_tests.rs"]
mod tests;
