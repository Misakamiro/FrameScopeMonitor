//! Merge signed component inventories without copying intact installed tools.
use crate::install_transaction::{assert_path, hash, Result, MANIFEST};
use framescope_update::components::{self, ManifestV2};
use std::{collections::BTreeSet, fs, io::{Read, Write}, path::{Path, PathBuf}};

pub fn stage(target: &Path, stage: &Path, changed: &mut Vec<PathBuf>, manifest: &ManifestV2, cache: &Path) -> Result<Vec<PathBuf>> {
    let plan=manifest.plan(target)?;
    let record_path=stage.join(components::INSTALLED_COMPONENTS);
    let record:serde_json::Value=serde_json::from_slice(&fs::read(&record_path).map_err(|e|e.to_string())?).map_err(|e|e.to_string())?;
    if record!=serde_json::to_value(manifest.installed_record()).map_err(|e|e.to_string())? {
        return Err("Embedded component inventory differs from signed release".into());
    }
    let manifest_path=stage.join(MANIFEST);
    let bytes=fs::read(&manifest_path).map_err(|e|e.to_string())?;
    let mut inventory:serde_json::Value=serde_json::from_slice(bytes.strip_prefix(&[0xef,0xbb,0xbf]).unwrap_or(&bytes)).map_err(|e|e.to_string())?;
    let files=inventory["files"].as_array_mut().ok_or("Missing staged program inventory")?;
    let mut names:BTreeSet<String>=files.iter().filter_map(|f|f["path"].as_str()).map(|p|p.replace('\\',"/").to_lowercase()).collect();
    if names.iter().any(|p|p.starts_with("toolbox/files/") || p.starts_with("dependencies/")) {
        return Err("Cumulative payload unexpectedly embeds reusable components".into());
    }
    let mut owned=changed.clone();
    for component in &manifest.components {
        if plan.download.contains(&component.id) {
            let archive_path=cache.join(&component.asset.package.name);
            assert_path(&archive_path)?;
            let mut options=fs::OpenOptions::new();options.read(true);
            #[cfg(windows)] { use std::os::windows::fs::OpenOptionsExt; options.share_mode(1); }
            let handle=options.open(&archive_path).map_err(|e|format!("组件 {} 未完整下载：{e}",component.id))?;
            framescope_update::verify_file(&archive_path,&component.asset.package)?;
            let mut zip=zip::ZipArchive::new(handle).map_err(|e|e.to_string())?;
            if zip.len()!=component.files.len() {return Err("Component ZIP file set differs from signed inventory".into());}
            let mut extracted=BTreeSet::new();
            for index in 0..zip.len() {
                let mut entry=zip.by_index(index).map_err(|e|e.to_string())?;
                let relative=components::relative_file(entry.name())?;
                let file=component.files.iter().find(|f|f.path==entry.name()).ok_or("Unsigned file in component ZIP")?;
                if entry.is_dir() || entry.size()!=file.length || !extracted.insert(file.path.clone()) ||
                    entry.unix_mode().is_some_and(|mode|mode & 0o170000==0o120000) {
                    return Err("Invalid component ZIP entry".into());
                }
                let destination=stage.join(&relative);assert_path(&destination)?;
                fs::create_dir_all(destination.parent().ok_or("Missing component parent")?).map_err(|e|e.to_string())?;
                let mut output=fs::OpenOptions::new().write(true).create_new(true).open(&destination).map_err(|e|e.to_string())?;
                let written=std::io::copy(&mut entry.by_ref().take(file.length+1),&mut output).map_err(|e|e.to_string())?;
                output.flush().and_then(|_|output.sync_all()).map_err(|e|e.to_string())?;
                if written!=file.length || !hash(&destination)?.eq_ignore_ascii_case(&file.sha256) {return Err("Component length/SHA256 mismatch".into());}
                changed.push(relative);
            }
        }
        for file in &component.files {
            if !names.insert(file.path.to_lowercase()) {return Err("Component overlaps application payload".into());}
            files.push(serde_json::to_value(file).map_err(|e|e.to_string())?);
            owned.push(PathBuf::from(&file.path));
        }
    }
    framescope_update::atomic_json(&manifest_path,&inventory)?;
    Ok(owned)
}
