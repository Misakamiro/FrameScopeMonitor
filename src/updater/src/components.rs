//! Signed cumulative updates keep reusable tool/dependency bytes separate.
//! An installed version number is never evidence that a component is intact.
use crate::{hex, verify_file, Package, Result};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{collections::BTreeSet, fs, path::{Path, PathBuf}};

pub const MANIFEST_V2: &str = "update-manifest-v2.json";
pub const INSTALLED_COMPONENTS: &str = "FrameScopeComponents.json";

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct ComponentFile { pub path: String, pub length: u64, pub sha256: String }

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct ComponentAsset { pub tag: String, pub package: Package }

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Component {
    pub id: String,
    pub revision: String,
    pub asset: ComponentAsset,
    pub files: Vec<ComponentFile>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct ManifestV2 {
    pub schema: u32,
    pub version: String,
    pub channel: String,
    pub git_sha: String,
    pub platform: String,
    pub edition: String,
    pub cumulative: Package,
    pub full: Option<Package>,
    pub components: Vec<Component>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct ComponentRecord {
    pub id: String,
    pub revision: String,
    pub files: Vec<ComponentFile>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct InstalledComponents { pub schema: u32, pub components: Vec<ComponentRecord> }

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct ComponentPlan {
    pub reuse: Vec<String>,
    pub download: Vec<String>,
    pub download_size: u64,
}

fn identifier(value: &str) -> bool {
    !value.is_empty() && value.len() <= 80 && value.bytes().all(|b| b.is_ascii_lowercase() || b.is_ascii_digit() || b == b'-')
}
pub fn relative_file(value: &str) -> Result<PathBuf> {
    if value.is_empty() || value.encode_utf16().count() > 240 || value.contains('\\') ||
        value.split('/').any(|part| part.is_empty() || part == "." || part == ".." ||
            part.ends_with(['.', ' ']) || part.chars().any(|c| c.is_control() || "<>:\"|?*".contains(c)) ||
            matches!(part.split('.').next().unwrap_or("").to_ascii_uppercase().as_str(),
                "CON"|"PRN"|"AUX"|"NUL"|"COM1"|"COM2"|"COM3"|"COM4"|"COM5"|"COM6"|"COM7"|"COM8"|"COM9"|
                "LPT1"|"LPT2"|"LPT3"|"LPT4"|"LPT5"|"LPT6"|"LPT7"|"LPT8"|"LPT9")) {
        return Err("组件文件路径无效".into());
    }
    Ok(PathBuf::from(value))
}
pub fn inventory_revision(files: &[ComponentFile]) -> Result<String> {
    let mut sorted = files.to_vec();
    sorted.sort_by(|a,b| a.path.cmp(&b.path));
    Ok(format!("{:x}", Sha256::digest(serde_json::to_vec(&sorted).map_err(|e| e.to_string())?)))
}
fn package(package: &Package) -> Result<()> {
    if !hex(&package.sha256, 64) || package.size == 0 || package.size > 2 * 1024 * 1024 * 1024 ||
        relative_file(&package.name)?.components().count() != 1 {
        return Err("组件下载附件身份无效".into());
    }
    Ok(())
}
impl Component {
    pub fn validate(&self) -> Result<()> {
        let scoped = |path: &str| {
            if let Some(id) = self.id.strip_prefix("tool-").filter(|id| identifier(id)) {
                path.starts_with(&format!("toolbox/files/{id}/"))
            } else {
                match self.id.as_str() {
                    "dependency-webview2" => path == "dependencies/Offline/WebView2.exe",
                    "dependency-dotnet48" => path == "dependencies/Offline/DotNet48.exe",
                    "dependency-pawnio" => path.starts_with("dependencies/PawnIO/"),
                    _ => false,
                }
            }
        };
        let mut paths = BTreeSet::new();
        if !identifier(&self.id) || !hex(&self.revision, 64) || self.files.is_empty() || self.files.len() > 50000 {
            return Err("组件身份或文件清单无效".into());
        }
        for file in &self.files {
            relative_file(&file.path)?;
            if !scoped(&file.path) || !hex(&file.sha256,64) || file.length > 2 * 1024 * 1024 * 1024 ||
                !paths.insert(file.path.to_ascii_lowercase()) {
                return Err("组件文件越界或身份重复".into());
            }
        }
        if inventory_revision(&self.files)? != self.revision { return Err("组件文件清单身份不符".into()); }
        let version = semver::Version::parse(self.asset.tag.strip_prefix('v').ok_or("组件来源标签无效")?).map_err(|e| e.to_string())?;
        if version.major < 2 || !version.build.is_empty() || self.asset.tag != format!("v{version}") {
            return Err("组件来源标签无效".into());
        }
        package(&self.asset.package)?;
        if self.asset.package.name != format!("FrameScope-component-{}-{}.zip",self.id,self.revision) {
            return Err("组件附件名称与身份不符".into());
        }
        Ok(())
    }
    pub fn record(&self) -> ComponentRecord {
        ComponentRecord { id:self.id.clone(), revision:self.revision.clone(), files:self.files.clone() }
    }
}
impl ManifestV2 {
    pub fn validate(&self, tag: &str) -> Result<()> {
        let version = semver::Version::parse(&self.version).map_err(|e| e.to_string())?;
        if self.schema != 2 || version.major < 2 || !version.build.is_empty() || tag != format!("v{version}") ||
            self.channel != if version.pre.is_empty() {"stable"} else {"beta"} ||
            self.platform != "windows-x86_64" || !matches!(self.edition.as_str(), "full"|"offline") || !hex(&self.git_sha,40) {
            return Err("累计更新清单的版本、渠道、平台或版型无效".into());
        }
        package(&self.cumulative)?;
        let edition = if self.edition == "offline" {"-offline"} else {""};
        if self.cumulative.name != format!("FrameScope_{}_x64{edition}-update.exe",self.version) {
            return Err("累计包名称无效".into());
        }
        if self.channel == "stable" && self.full.is_none() {return Err("正式版缺少全量安装包".into());}
        if let Some(full) = &self.full {
            package(full)?;
            if full.name != format!("FrameScope_{}_x64{edition}-setup.exe",self.version) {return Err("全量包名称无效".into());}
        }
        let mut ids = BTreeSet::new();
        let mut paths = BTreeSet::new();
        if self.components.len() > 256 { return Err("组件数量超过限制".into()); }
        for component in &self.components {
            component.validate()?;
            if !ids.insert(component.id.clone()) || component.files.iter().any(|file| !paths.insert(file.path.to_ascii_lowercase())) {
                return Err("累计更新包含重复组件或文件".into());
            }
        }
        Ok(())
    }
    pub fn installed_record(&self) -> InstalledComponents {
        InstalledComponents {schema:1,components:self.components.iter().map(Component::record).collect()}
    }
    pub fn plan(&self, target: &Path) -> Result<ComponentPlan> {
        self.validate(&format!("v{}",self.version))?;
        let record_path=target.join(INSTALLED_COMPONENTS);
        let installed = plain_file(target,&record_path).ok().and_then(|_|fs::read(&record_path).ok())
            .and_then(|bytes| serde_json::from_slice::<InstalledComponents>(&bytes).ok())
            .filter(|record| record.schema == 1);
        let mut plan=ComponentPlan {reuse:vec![],download:vec![],download_size:self.cumulative.size};
        for component in &self.components {
            let recorded=installed.as_ref().is_some_and(|record| {
                let matching=record.components.iter().filter(|row| row.id==component.id).collect::<Vec<_>>();
                matching.len()==1 && matching[0].revision==component.revision && matching[0].files==component.files
            });
            if recorded && verify_component(target,component).is_ok() {plan.reuse.push(component.id.clone());}
            else {
                plan.download.push(component.id.clone());
                plan.download_size=plan.download_size.checked_add(component.asset.package.size).ok_or("组件下载大小溢出")?;
            }
        }
        Ok(plan)
    }
}
pub fn verify_manifest_v2(bytes: &[u8], signature: &str, key: &str, tag: &str) -> Result<ManifestV2> {
    let key=minisign_verify::PublicKey::from_base64(key.trim()).map_err(|e|e.to_string())?;
    let signature=minisign_verify::Signature::decode(signature).map_err(|e|e.to_string())?;
    key.verify(bytes,&signature,false).map_err(|e|format!("累计更新清单签名校验失败: {e}"))?;
    let manifest:ManifestV2=serde_json::from_slice(bytes).map_err(|e|e.to_string())?;
    manifest.validate(tag)?;
    Ok(manifest)
}
fn plain_file(root: &Path, path: &Path) -> Result<()> {
    if !path.starts_with(root) {return Err("组件文件超出安装目录".into());}
    for ancestor in path.ancestors() {
        let metadata=fs::symlink_metadata(ancestor).map_err(|e|e.to_string())?;
        if metadata.file_type().is_symlink() {return Err("组件路径含链接".into());}
        #[cfg(windows)] {
            use std::os::windows::fs::MetadataExt;
            if metadata.file_attributes() & 0x400 != 0 {return Err("组件路径含重解析点".into());}
        }
    }
    if !fs::metadata(path).map_err(|e|e.to_string())?.is_file() {return Err("组件文件类型无效".into());}
    Ok(())
}
pub fn verify_component(target: &Path, component: &Component) -> Result<()> {
    component.validate()?;
    for file in &component.files {
        let path=target.join(relative_file(&file.path)?);
        plain_file(target,&path)?;
        verify_file(&path,&Package {name:file.path.clone(),size:file.length,sha256:file.sha256.clone()})?;
    }
    Ok(())
}

#[cfg(test)] mod tests;
