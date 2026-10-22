//! Update inputs are repository release identities, never frontend supplied URLs.
use serde::{Deserialize, Serialize};
mod download;
#[cfg(windows)]
pub mod autostart;
use semver::Version;
use sha2::{Digest, Sha256};
use std::{fs, io::{Read, Write}, path::{Path, PathBuf}, sync::atomic::{AtomicBool, Ordering}, time::Duration};

pub type Result<T> = std::result::Result<T, String>;
pub const REPOSITORY: &str = "Misakamiro/FrameScopeMonitor";
pub const PROXY: &str = "https://gh-proxy.org/";
pub const MANIFEST: &str = "release-manifest.json";
pub const SIGNATURE: &str = "release-manifest.json.minisig";
pub const PUBLIC_KEY: &str = include_str!("../release.pub");
pub fn public_key() -> &'static str { PUBLIC_KEY.lines().nth(1).expect("Minisign public key missing") }
pub fn installation_allowed(phase:&str)->bool {matches!(phase,"idle"|"stopped"|"completed")}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct BuildInfo { pub version:String, pub git_sha:String, pub channel:String }
impl BuildInfo {
    pub fn new(version:&str,sha:&str)->Self {Self {version:version.into(),git_sha:sha.into(),channel:if version.contains('-'){"beta"}else{"stable"}.into()}}
}
#[derive(Clone, Serialize, Deserialize)]
pub struct StartupTicket { pub nonce:String, pub build:BuildInfo }
#[derive(Serialize, Deserialize)]
pub struct StartupAck { pub nonce:String, pub build:BuildInfo, pub executable:PathBuf, pub pid:u32 }
pub fn startup_ack_matches(ticket:&StartupTicket,ack:&StartupAck,expected:&Path,running_image:Option<&Path>)->bool {
    ack.pid!=0&&ack.nonce==ticket.nonce&&ack.build.version==ticket.build.version&&ack.build.git_sha==ticket.build.git_sha
        &&ack.build.channel==ticket.build.channel&&ack.executable==expected&&running_image==Some(expected)
}
pub fn acknowledge_startup(root:&Path,build:BuildInfo)->Result<()> {
    let ticket = root.join("FrameScopeUpdatePending.json");
    if !ticket.exists() {return Ok(());}
    let ticket:StartupTicket = serde_json::from_slice(&fs::read(ticket).map_err(|e|e.to_string())?).map_err(|e|e.to_string())?;
    if ticket.build.version != build.version || ticket.build.git_sha != build.git_sha {return Err("更新启动身份不匹配".into());}
    atomic_json(&root.join("FrameScopeUpdateReady.json"),&StartupAck {nonce:ticket.nonce,build,
        executable:std::env::current_exe().map_err(|e|e.to_string())?.canonicalize().map_err(|e|e.to_string())?,pid:std::process::id()})
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Package { pub name: String, pub size: u64, pub sha256: String }
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Manifest {
    pub schema: u32, pub version: String, pub channel: String, pub git_sha: String,
    pub platform: String, pub installer: Package,
}
fn hex(s: &str, n: usize) -> bool { s.len() == n && s.bytes().all(|b| b.is_ascii_hexdigit()) }
impl Manifest {
    pub fn validate(&self, tag: &str) -> Result<()> {
        let version = Version::parse(&self.version).map_err(|e| e.to_string())?;
        let channel = if version.pre.is_empty() { "stable" } else { "beta" };
        if self.schema != 1 || version.major < 2 || !version.build.is_empty()
            || tag != format!("v{}", self.version) || self.channel != channel
            || self.platform != "windows-x86_64" || !hex(&self.git_sha, 40)
            || self.installer.name != format!("FrameScope_{}_x64-setup.exe", self.version)
            || self.installer.size == 0 || self.installer.size > 2 * 1024 * 1024 * 1024
            || !hex(&self.installer.sha256, 64) {
            return Err("发行清单的版本、渠道、平台或包身份不一致".into());
        }
        Ok(())
    }
}
pub fn verify_manifest(bytes: &[u8], signature: &str, key: &str, tag: &str) -> Result<Manifest> {
    let key = minisign_verify::PublicKey::from_base64(key.trim()).map_err(|e| e.to_string())?;
    let signature = minisign_verify::Signature::decode(signature).map_err(|e| e.to_string())?;
    key.verify(bytes, &signature, false).map_err(|e| format!("更新清单签名校验失败: {e}"))?;
    let manifest: Manifest = serde_json::from_slice(bytes).map_err(|e| e.to_string())?;
    manifest.validate(tag)?;
    Ok(manifest)
}
pub fn eligible(candidate: &str, current: &str, beta: bool, return_stable: bool, skipped: Option<&str>) -> bool {
    let (Ok(next), Ok(current)) = (Version::parse(candidate), Version::parse(current)) else { return false };
    next.major >= 2 && next.build.is_empty() && (beta || next.pre.is_empty())
        && skipped != Some(candidate) && (next > current || (return_stable && !current.pre.is_empty() && next.pre.is_empty() && next != current))
}

#[derive(Clone, Debug, Deserialize)]
pub struct Release {
    pub id: u64, pub tag_name: String, pub draft: bool, pub prerelease: bool,
    #[serde(default)] pub body: Option<String>, pub assets: Vec<Asset>,
}
#[derive(Clone, Debug, Deserialize)]
pub struct Asset { pub name: String, pub size: u64 }
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Candidate { pub id: u64, pub tag: String, pub notes: String, pub manifest: Manifest }
#[derive(Clone, Serialize, Deserialize)]
pub struct Receipt { pub candidate: Candidate, pub manifest_text: String, pub signature: String }

/// Production constructor fixes the repository and proxy. Tests use loopback only.
pub struct Source { client: reqwest::blocking::Client, api: String, downloads: String, proxy: Option<String>, key: String, origin_first: AtomicBool }
fn system_proxy_enabled() -> bool {
    if ["HTTPS_PROXY", "https_proxy", "ALL_PROXY", "all_proxy", "HTTP_PROXY", "http_proxy"]
        .iter().any(|name| std::env::var(name).map(|v| !v.trim().is_empty()).unwrap_or(false)) { return true; }
    #[cfg(windows)]
    if let Ok(key) = winreg::RegKey::predef(winreg::enums::HKEY_CURRENT_USER)
        .open_subkey(r"Software\Microsoft\Windows\CurrentVersion\Internet Settings") {
        return key.get_value::<u32, _>("ProxyEnable").unwrap_or(0) != 0
            || !key.get_value::<String, _>("AutoConfigURL").unwrap_or_default().is_empty();
    }
    false
}
fn request_error(error: reqwest::Error) -> String {
    use std::error::Error;
    let mut message = error.to_string();
    let mut cause = error.source();
    while let Some(value) = cause { message.push_str(": "); message.push_str(&value.to_string()); cause = value.source(); }
    message
}
impl Source {
    pub fn github(key: &str, accelerated: bool) -> Result<Self> {
        let source = Self::new(format!("https://api.github.com/repos/{REPOSITORY}"),
            format!("https://github.com/{REPOSITORY}/releases/download"),
            accelerated.then(|| PROXY.into()), key.into())?;
        source.origin_first.store(system_proxy_enabled(), Ordering::Relaxed);
        Ok(source)
    }
    fn new(api: String, downloads: String, proxy: Option<String>, key: String) -> Result<Self> {
        let client = reqwest::blocking::Client::builder().user_agent("FrameScope/2 updater")
            .connect_timeout(Duration::from_secs(4))
            .redirect(reqwest::redirect::Policy::limited(5)).build().map_err(|e| e.to_string())?;
        Ok(Self {client, api, downloads, proxy, key, origin_first: AtomicBool::new(false)})
    }
    fn url(&self, original: &str, proxy: bool) -> String {
        if proxy { format!("{}{original}", self.proxy.as_deref().unwrap_or("")) } else { original.into() }
    }
    fn small(&self, original: &str, proxy: bool, cancel: &AtomicBool) -> Result<Vec<u8>> {
        cancelled(cancel)?;
        let response = self.client.get(self.url(original, proxy)).timeout(Duration::from_secs(8))
            .send().and_then(|r|r.error_for_status()).map_err(request_error)?;
        let mut bytes = Vec::new();
        response.take(2 * 1024 * 1024 + 1).read_to_end(&mut bytes).map_err(|e|e.to_string())?;
        cancelled(cancel)?;
        if bytes.len() > 2 * 1024 * 1024 { return Err("更新响应过大".into()); }
        Ok(bytes)
    }
    fn attempts<T>(&self, cancel: &AtomicBool, operation: impl FnMut(bool) -> Result<T>) -> Result<T> {
        let routes: &[bool] = if self.proxy.is_none() { &[false] }
            else if self.origin_first.load(Ordering::Relaxed) { &[false, true] } else { &[true, false] };
        self.attempts_on(cancel, routes, operation)
    }
    fn attempts_on<T>(&self, cancel: &AtomicBool, routes: &[bool], mut operation: impl FnMut(bool) -> Result<T>) -> Result<T> {
        let mut errors = Vec::new();
        for &proxy in routes {
            cancelled(cancel)?;
            match operation(proxy) {
                Ok(value) => { self.origin_first.store(!proxy, Ordering::Relaxed); return Ok(value); }
                Err(error) => errors.push(format!("{}：{error}", if proxy { "GitHub 加速站" } else { "GitHub 原站（遵循系统代理）" })),
            }
        }
        cancelled(cancel)?;
        Err(errors.join("\n"))
    }
    fn signed(&self, tag: &str, proxy: bool, cancel: &AtomicBool) -> Result<(Manifest,String,String)> {
        // tag is a validated SemVer; no path/query components are accepted.
        Version::parse(tag.strip_prefix('v').ok_or("Invalid release tag")?).map_err(|e|e.to_string())?;
        let base = format!("{}/{tag}", self.downloads);
        let text = String::from_utf8(self.small(&format!("{base}/{MANIFEST}"),proxy,cancel)?).map_err(|e|e.to_string())?;
        let sig = String::from_utf8(self.small(&format!("{base}/{SIGNATURE}"),proxy,cancel)?).map_err(|e|e.to_string())?;
        let manifest = verify_manifest(text.as_bytes(), &sig, &self.key, tag)?;
        Ok((manifest,text,sig))
    }
    pub fn check(&self, current: &str, beta: bool, return_stable: bool, skipped: Option<&str>, cancel: &AtomicBool) -> Result<Vec<Candidate>> {
            let mut releases = Vec::new();
            for page in 1..=10 {
                let list: Vec<Release> = self.attempts(cancel, |proxy| {
                    let bytes = self.small(&format!("{}/releases?per_page=100&page={page}", self.api),proxy,cancel)?;
                    serde_json::from_slice(&bytes).map_err(|e|e.to_string())
                })?;
                let done = list.len() < 100;
                releases.extend(list);
                if done { break; }
                if page == 10 { return Err("发行数量超过检查上限".into()); }
            }
            releases.retain(|r| !r.draft && eligible(r.tag_name.strip_prefix('v').unwrap_or(""),current,beta,return_stable,skipped));
            releases.sort_by_key(|r| std::cmp::Reverse(Version::parse(&r.tag_name[1..]).unwrap()));
            let mut result = Vec::new();
            let mut invalid = None;
            for release in releases {
                if ![MANIFEST,SIGNATURE].iter().all(|name|release.assets.iter().any(|a|a.name == *name)) { continue; }
                // A bad signature from the proxy must be retried at the origin.
                let signed = self.attempts(cancel, |proxy| self.signed(&release.tag_name,proxy,cancel));
                let (manifest,_,_) = match signed {
                    Ok(s) => s,
                    Err(e) => { cancelled(cancel)?; invalid = Some(e); continue; }
                };
                if release.prerelease != (manifest.channel == "beta") || !release.assets.iter().any(|a|a.name == manifest.installer.name && a.size == manifest.installer.size) { continue; }
                result.push(Candidate { id:release.id, tag:release.tag_name, notes:release.body.unwrap_or_default(), manifest });
                // Keep newest eligible + newest stable (explicit stable return).
                if result.len() >= 2 && result.iter().any(|r|r.manifest.channel == "stable") { break; }
            }
            if result.is_empty() { if let Some(error) = invalid { return Err(error); } }
            Ok(result)
    }
    pub fn download(&self, candidate: &Candidate, cache: &Path, cancel: &AtomicBool, progress: impl Fn(u64,u64)) -> Result<Receipt> {
        fs::create_dir_all(cache).map_err(|e|e.to_string())?;
        let path = cache.join(&candidate.manifest.installer.name);
        let partial = path.with_extension("part");
        let result = (|| {
            let (manifest,text,sig) = self.attempts(cancel, |proxy| self.signed(&candidate.tag,proxy,cancel))?;
            // Do not accept a replaced release under the same identity.
            if serde_json::to_value(&manifest).unwrap() != serde_json::to_value(&candidate.manifest).unwrap() { return Err("发行文件在检查后被修改".into()); }
            let original = format!("{}/{}/{}",self.downloads,candidate.tag,manifest.installer.name);
            let routes = self.download_routes(&original, manifest.installer.size, cancel)?;
            self.attempts_on(cancel, &routes, |proxy| {
                progress(0, manifest.installer.size);
                eprintln!("FrameScope updater download route: {}", if proxy { "accelerator" } else { "origin/system-proxy" });
                self.transfer_with_speed_check(&self.url(&original,proxy), &partial,manifest.installer.size,cancel,&progress,
                    routes.len() > 1 && proxy == routes[0])?;
                verify_file(&partial,&manifest.installer)?;
                cancelled(cancel)?;
                if path.exists() { fs::remove_file(&path).map_err(|e|e.to_string())?; }
                fs::rename(&partial,&path).map_err(|e|e.to_string())?;
                let receipt = Receipt {candidate:candidate.clone(),manifest_text:text.clone(),signature:sig.clone()};
                atomic_json(&cache.join("receipt.json"), &receipt)?;
                Ok(receipt)
            })
        })();
        let _ = fs::remove_file(partial);
        result
    }
    #[cfg(test)]
    fn transfer(&self, url:&str, path:&Path, expected:u64, cancel:&AtomicBool, progress:&impl Fn(u64,u64)) -> Result<()> {
        self.transfer_with_speed_check(url, path, expected, cancel, progress, true)
    }
    pub fn verified_cache(&self, cache: &Path, receipt: &Receipt) -> Result<PathBuf> {
        let manifest = verify_manifest(receipt.manifest_text.as_bytes(), &receipt.signature,&self.key,&receipt.candidate.tag)?;
        if serde_json::to_value(&manifest).unwrap() != serde_json::to_value(&receipt.candidate.manifest).unwrap() { return Err("缓存清单不一致".into()); }
        let path = cache.join(&manifest.installer.name);
        verify_file(&path,&manifest.installer)?;
        Ok(path)
    }
}
pub fn cancelled(cancel: &AtomicBool) -> Result<()> { if cancel.load(Ordering::SeqCst) {Err("下载已取消".into())} else {Ok(())} }
pub fn verify_file(path: &Path, expected: &Package) -> Result<()> {
    let mut file = fs::File::open(path).map_err(|e|e.to_string())?;
    if file.metadata().map_err(|e|e.to_string())?.len() != expected.size { return Err("安装包大小不符".into()); }
    let mut sha = Sha256::new();
    let mut bytes = [0u8;65536];
    loop { let n = file.read(&mut bytes).map_err(|e|e.to_string())?; if n==0 {break;} sha.update(&bytes[..n]); }
    if format!("{:x}",sha.finalize()) != expected.sha256.to_ascii_lowercase() {return Err("安装包 SHA256 校验失败".into());}
    Ok(())
}
pub fn atomic_json(path: &Path, value: &impl Serialize) -> Result<()> {
    let temp = path.with_extension("pending");
    let mut file = fs::File::create(&temp).map_err(|e|e.to_string())?;
    file.write_all(&serde_json::to_vec_pretty(value).map_err(|e|e.to_string())?).map_err(|e|e.to_string())?;
    file.sync_all().map_err(|e|e.to_string())?;
    drop(file);
    // Windows rename does not replace existing files. Use ReplaceFileW there.
    #[cfg(windows)]
    if path.exists() {
        use std::os::windows::ffi::OsStrExt;
        #[link(name="kernel32")] unsafe extern "system" { fn ReplaceFileW(old:*const u16,new:*const u16,backup:*const u16,flags:u32,a:*mut core::ffi::c_void,b:*mut core::ffi::c_void)->i32; }
        let wide = |p:&Path|p.as_os_str().encode_wide().chain(Some(0)).collect::<Vec<_>>();
        if unsafe {ReplaceFileW(wide(path).as_ptr(),wide(&temp).as_ptr(),std::ptr::null(),0,std::ptr::null_mut(),std::ptr::null_mut())} == 0 { return Err(std::io::Error::last_os_error().to_string()); }
        return Ok(());
    }
    fs::rename(temp,path).map_err(|e|e.to_string())
}

#[cfg(test)]
mod tests;
#[cfg(test)]
mod download_tests;
