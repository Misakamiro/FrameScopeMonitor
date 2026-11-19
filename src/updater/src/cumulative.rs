use super::*;
use components::{ManifestV2,verify_manifest_v2};

fn projection(manifest:&ManifestV2)->Manifest {
    // This is the existing UI/package view, never a schema-1 signed document.
    Manifest {schema:2,version:manifest.version.clone(),channel:manifest.channel.clone(),git_sha:manifest.git_sha.clone(),
        platform:manifest.platform.clone(),edition:manifest.edition.clone(),installer:manifest.cumulative.clone()}
}
fn same(a:&impl Serialize,b:&impl Serialize)->Result<bool>{
    Ok(serde_json::to_value(a).map_err(|e|e.to_string())?==serde_json::to_value(b).map_err(|e|e.to_string())?)
}
pub fn verify_cumulative_receipt(receipt:&Receipt,key:&str)->Result<ManifestV2>{
    let manifest=verify_manifest_v2(receipt.manifest_text.as_bytes(),&receipt.signature,key,&receipt.candidate.tag)?;
    let expected_name=if manifest.edition=="offline"{"offline-update-manifest-v2.json"}else{components::MANIFEST_V2};
    if receipt.candidate.manifest_name!=expected_name ||
        !same(&receipt.candidate.v2,&Some(&manifest))? || !same(&receipt.candidate.manifest,&projection(&manifest))? {
        return Err("累计更新缓存清单身份不一致".into());
    }
    let mut ids=std::collections::BTreeSet::new();
    if receipt.downloaded_components.iter().any(|id| !ids.insert(id) || !manifest.components.iter().any(|c|c.id==*id)) {
        return Err("累计更新组件收据无效".into());
    }
    Ok(manifest)
}
impl Source {
    fn signed_cumulative(&self,tag:&str,name:&str,proxy:bool,cancel:&AtomicBool)->Result<(ManifestV2,String,String)>{
        let expected=if self.edition=="offline"{"offline-update-manifest-v2.json"}else{components::MANIFEST_V2};
        if name!=expected {return Err("累计更新清单名称无效".into());}
        let version=Version::parse(tag.strip_prefix('v').ok_or("发行标签无效")?).map_err(|e|e.to_string())?;
        if tag!=format!("v{version}")||!version.build.is_empty(){return Err("发行标签无效".into());}
        let base=format!("{}/{tag}",self.downloads);
        let text=String::from_utf8(self.small(&format!("{base}/{name}"),proxy,cancel)?).map_err(|e|e.to_string())?;
        let signature=String::from_utf8(self.small(&format!("{base}/{name}.minisig"),proxy,cancel)?).map_err(|e|e.to_string())?;
        let manifest=verify_manifest_v2(text.as_bytes(),&signature,&self.key,tag)?;
        if manifest.edition!=self.edition{return Err("累计更新版型与客户端不符".into());}
        Ok((manifest,text,signature))
    }
    pub(super) fn cumulative_candidate(&self,release:&Release,name:&str,cancel:&AtomicBool)->Result<Candidate>{
        if !release.assets.iter().any(|a|a.name==format!("{name}.minisig")){return Err("累计更新缺少签名附件".into());}
        let (manifest,_,_)=self.attempts(cancel,|proxy|self.signed_cumulative(&release.tag_name,name,proxy,cancel))?;
        let attached=|package:&Package|release.assets.iter().any(|a|a.name==package.name&&a.size==package.size);
        if release.prerelease!=(manifest.channel=="beta") || !attached(&manifest.cumulative) ||
            manifest.full.as_ref().is_some_and(|package|!attached(package)) {
            return Err("累计更新发行附件与签名清单不符".into());
        }
        let plan=manifest.plan(self.installation.as_deref().ok_or("累计更新缺少安装目录")?)?;
        Ok(Candidate {id:release.id,tag:release.tag_name.clone(),notes:release.body.clone().unwrap_or_default(),
            manifest:projection(&manifest),manifest_name:name.into(),v2:Some(manifest),component_plan:Some(plan)})
    }
    pub(super) fn download_cumulative(&self,candidate:&Candidate,cache:&Path,cancel:&AtomicBool,
        progress:impl Fn(u64,u64),observer:impl Fn(DownloadTelemetry))->Result<Receipt>{
        let (manifest,text,signature)=self.attempts(cancel,|proxy|
            self.signed_cumulative(&candidate.tag,&candidate.manifest_name,proxy,cancel))?;
        if !same(&candidate.v2,&Some(&manifest))? || !same(&candidate.manifest,&projection(&manifest))? {
            return Err("累计更新发行在检查后发生变化".into());
        }
        let plan=manifest.plan(self.installation.as_deref().ok_or("累计更新缺少安装目录")?)?;
        fs::create_dir_all(cache).map_err(|e|e.to_string())?;
        let mut assets=vec![(&candidate.tag,&manifest.cumulative)];
        for component in &manifest.components {
            if plan.download.contains(&component.id){assets.push((&component.asset.tag,&component.asset.package));}
        }
        let mut completed=0;
        for (tag,package) in assets {
            cancelled(cancel)?;
            let destination=cache.join(&package.name);
            if verify_file(&destination,package).is_err(){
                let partial=destination.with_extension("part");
                let result=(||{
                    let url=format!("{}/{tag}/{}",self.downloads,package.name);
                    self.download_asset(&url,&partial,package,cancel,&|done,_|progress(completed+done,plan.download_size),&observer)?;
                    cancelled(cancel)?;
                    if destination.exists(){fs::remove_file(&destination).map_err(|e|e.to_string())?;}
                    fs::rename(&partial,&destination).map_err(|e|e.to_string())
                })();
                let _=fs::remove_file(partial);
                result?;
            }
            completed+=package.size;progress(completed,plan.download_size);
        }
        cancelled(cancel)?;
        let mut selected=candidate.clone();selected.component_plan=Some(plan.clone());
        let receipt=Receipt {candidate:selected,manifest_text:text,signature,downloaded_components:plan.download};
        self.verified_cumulative_cache(cache,&receipt)?;
        atomic_json(&cache.join("receipt.json"),&receipt)?;
        Ok(receipt)
    }
    pub(super) fn verified_cumulative_cache(&self,cache:&Path,receipt:&Receipt)->Result<PathBuf>{
        let manifest=verify_cumulative_receipt(receipt,&self.key)?;
        if manifest.edition!=self.edition{return Err("累计更新缓存版型不符".into());}
        let plan=manifest.plan(self.installation.as_deref().ok_or("累计更新缺少安装目录")?)?;
        if plan.download.iter().any(|id|!receipt.downloaded_components.contains(id)){
            return Err("本机组件在下载后发生变化，请重新下载所需组件".into());
        }
        for component in &manifest.components {
            if receipt.downloaded_components.contains(&component.id){
                verify_file(&cache.join(&component.asset.package.name),&component.asset.package)?;
            }
        }
        let path=cache.join(&manifest.cumulative.name);verify_file(&path,&manifest.cumulative)?;
        Ok(path)
    }
}
