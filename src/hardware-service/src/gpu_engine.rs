//! Windows 3D engine sampling. No shell, WMI, GPU injection or driver writes.
use serde_json::{json,Value};
use std::collections::{BTreeMap,BTreeSet};
use std::sync::{Mutex,OnceLock};
use std::time::{Instant,Duration,SystemTime,UNIX_EPOCH};

#[derive(Clone,Debug,PartialEq,Eq,PartialOrd,Ord)]
struct Engine { luid:String, physical:u32, engine:u32 }
fn instance(name:&str)->Option<Engine> {
    let (_,rest)=name.split_once("_luid_")?;
    let (high,rest)=rest.split_once('_')?;
    let (low,rest)=rest.split_once("_phys_")?;
    let (physical,rest)=rest.split_once("_eng_")?;
    let (engine,kind)=rest.split_once("_engtype_")?;
    if kind!="3D" {return None;}
    let number=|s:&str|u32::from_str_radix(s.strip_prefix("0x")?,16).ok();
    Some(Engine{luid:format!("{:08x}:{:08x}",number(high)?,number(low)?),
        physical:physical.parse().ok()?,engine:engine.parse().ok()?})
}
fn aggregate(rows:impl IntoIterator<Item=(String,f64)>)->BTreeMap<String,f64> {
    let mut engines=BTreeMap::<Engine,f64>::new();
    let mut seen=BTreeSet::new();
    for (name,value) in rows {
        if !value.is_finite() || value<0.0 || !seen.insert(name.clone()){continue;}
        if let Some(key)=instance(&name){*engines.entry(key).or_default()+=value;}
    }
    let mut adapters=BTreeMap::<String,f64>::new();
    for (key,value) in engines {
        let entry=adapters.entry(key.luid).or_default();
        *entry=entry.max(value.min(100.0));
    }
    adapters
}

#[cfg(windows)]
mod native {
    use super::*;
    use windows_sys::Win32::System::Performance::*;
    use windows_sys::Win32::Devices::{DeviceAndDriverInstallation::*,Display::GUID_DISPLAY_DEVICE_ARRIVAL};
    use windows_sys::Wdk::Graphics::Direct3D::*;
    const PDH_FMT_NOCAP100:u32=0x8000;
    pub struct Query { query:isize, counter:isize, previous:Option<Instant> }
    impl Drop for Query {fn drop(&mut self){unsafe{PdhCloseQuery(self.query);}}}
    impl Query {
        pub fn new()->Result<Self,String>{unsafe{
            let mut query=0;
            let status=PdhOpenQueryW(std::ptr::null(),0,&mut query);
            if status!=0{return Err(format!("PDH open: {status:#x}"));}
            let mut counter=0;
            let path:Vec<u16>=r"\GPU Engine(*)\Utilization Percentage".encode_utf16().chain(Some(0)).collect();
            let status=PdhAddEnglishCounterW(query,path.as_ptr(),0,&mut counter);
            if status!=0{PdhCloseQuery(query);return Err(format!("PDH GPU Engine: {status:#x}"));}
            Ok(Self{query,counter,previous:None})
        }}
        pub fn sample(&mut self)->Result<BTreeMap<String,f64>,String>{unsafe{
            let now=Instant::now();
            let status=PdhCollectQueryData(self.query);
            if status!=0{self.previous=None;return Err(format!("PDH collect: {status:#x}"));}
            let elapsed=self.previous.replace(now).map(|t|now.duration_since(t));
            if elapsed.is_none()||elapsed.is_some_and(|t|t>Duration::from_secs(10)){
                return Err("warming-up".into());
            }
            let (mut bytes,mut count)=(0,0);
            let status=PdhGetFormattedCounterArrayW(self.counter,PDH_FMT_DOUBLE|PDH_FMT_NOCAP100,&mut bytes,&mut count,std::ptr::null_mut());
            if status!=PDH_MORE_DATA||bytes==0{return Err(format!("PDH array size: {status:#x}"));}
            for _ in 0..3 {
                if bytes>32*1024*1024{return Err("PDH array too large".into());}
                // usize storage supplies the alignment required by PDH's pointer-containing records.
                let mut buffer=vec![0usize;(bytes as usize+std::mem::size_of::<usize>()-1)/std::mem::size_of::<usize>()];
                let status=PdhGetFormattedCounterArrayW(self.counter,PDH_FMT_DOUBLE|PDH_FMT_NOCAP100,&mut bytes,&mut count,buffer.as_mut_ptr().cast());
                if status==PDH_MORE_DATA{continue;}
                if status!=0{return Err(format!("PDH array: {status:#x}"));}
                if count as usize*std::mem::size_of::<PDH_FMT_COUNTERVALUE_ITEM_W>()>buffer.len()*std::mem::size_of::<usize>(){return Err("PDH invalid count".into());}
                let items=std::slice::from_raw_parts(buffer.as_ptr().cast::<PDH_FMT_COUNTERVALUE_ITEM_W>(),count as usize);
                let mut rows=Vec::new();
                for item in items {
                    if item.FmtValue.CStatus>1||item.szName.is_null(){continue;}
                    let base=buffer.as_ptr() as usize;let end=base+buffer.len()*std::mem::size_of::<usize>();
                    let start=item.szName as usize;
                    if start<base||start>=end||start%2!=0{continue;}
                    let text=std::slice::from_raw_parts(item.szName,(end-start)/2);
                    let Some(len)=text.iter().position(|v|*v==0)else{continue};
                    rows.push((String::from_utf16_lossy(&text[..len]),item.FmtValue.Anonymous.doubleValue));
                }
                return Ok(aggregate(rows));
            }
            Err("PDH instances changed during sample".into())
        }}
    }
    // Resolve the exact device instance published by LHM to a kernel adapter LUID.
    // A name/ordinal fallback would silently mix identical adapters, so none is used.
    pub fn adapter(device_id:&str)->Result<String,String>{unsafe{
        if device_id.is_empty()||device_id.contains('\0'){return Err("missing-device-identity".into());}
        let device:Vec<u16>=device_id.encode_utf16().chain(Some(0)).collect();
        let mut len=0;
        let status=CM_Get_Device_Interface_List_SizeW(&mut len,&GUID_DISPLAY_DEVICE_ARRIVAL,device.as_ptr(),CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
        if status!=0||len<2||len>65536{return Err(format!("adapter interface size: {status}"));}
        let mut interfaces=vec![0u16;len as usize];
        let status=CM_Get_Device_Interface_ListW(&GUID_DISPLAY_DEVICE_ARRIVAL,device.as_ptr(),interfaces.as_mut_ptr(),len,CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
        if status!=0{return Err(format!("adapter interfaces: {status}"));}
        let mut luids=BTreeSet::new();
        let mut offset=0;
        while offset<interfaces.len()&&interfaces[offset]!=0 {
            let Some(length)=interfaces[offset..].iter().position(|v|*v==0)else{break};
            let mut request:D3DKMT_OPENADAPTERFROMDEVICENAME=std::mem::zeroed();
            request.pDeviceName=interfaces.as_ptr().add(offset);
            if D3DKMTOpenAdapterFromDeviceName(&mut request)>=0 {
                luids.insert(format!("{:08x}:{:08x}",request.AdapterLuid.HighPart as u32,request.AdapterLuid.LowPart));
                D3DKMTCloseAdapter(&D3DKMT_CLOSEADAPTER{hAdapter:request.hAdapter});
            }
            offset+=length+1;
        }
        if luids.len()!=1{return Err(format!("ambiguous-or-missing-adapter: {}",luids.len()));}
        Ok(luids.into_iter().next().unwrap())
    }}
}
#[cfg(not(windows))]
mod native {
    use super::*;
    pub struct Query;
    impl Query {pub fn new()->Result<Self,String>{Err("Windows only".into())}
        pub fn sample(&mut self)->Result<BTreeMap<String,f64>,String>{Err("Windows only".into())}}
    pub fn adapter(_: &str)->Result<String,String>{Err("Windows only".into())}
}
#[derive(Default)]
struct Sampler { query:Option<native::Query>, retry:Option<Instant> }
impl Sampler {
    fn sample(&mut self)->Result<BTreeMap<String,f64>,String> {
        if self.query.is_none() {
            if self.retry.is_some_and(|t|t.elapsed()<Duration::from_secs(5)){return Err("retry-pending".into());}
            self.retry=Some(Instant::now());self.query=Some(native::Query::new()?);
        }
        let result=self.query.as_mut().unwrap().sample();
        if result.as_ref().is_err_and(|e|e!="warming-up"){self.query=None;self.retry=Some(Instant::now());}
        result
    }
}
static SAMPLER:OnceLock<Mutex<Sampler>>=OnceLock::new();
pub fn shutdown() {
    if let Some(sampler)=SAMPLER.get(){*sampler.lock().unwrap_or_else(|p|p.into_inner())=Sampler::default();}
}

pub fn enrich(payload:&mut Value) {
    let captured_at=payload["capturedAt"].clone();
    let mut devices=BTreeMap::new();
    for row in payload["sensors"].as_array().into_iter().flatten(){
        if row["hardwareType"].as_str().is_some_and(|v|v.to_ascii_lowercase().contains("gpu")){
            if let Some(id)=row["identifier"].as_str().filter(|id|!id.is_empty()){
                devices.entry(id.to_string()).or_insert((row["hardware"].clone(),row["hardwareType"].clone(),row["deviceId"].as_str().unwrap_or("").to_string()));
            }
        }
    }
    let sample=SAMPLER.get_or_init(Default::default).lock().unwrap().sample();
    let captured=SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_millis() as u64;
    let mut status=Vec::new();
    let mut additions=Vec::new();
    let mapped=devices.iter().map(|(id,(_,_,device))|(id.clone(),native::adapter(device))).collect::<BTreeMap<_,_>>();
    for (id,(name,kind,device)) in devices {
        let adapter=&mapped[&id];
        let result=adapter.as_ref().map_err(Clone::clone).and_then(|luid|{
            if mapped.values().filter(|other|other.as_ref().ok()==Some(luid)).count()!=1{return Err("duplicate-device-mapping".into());}
            sample.as_ref().map_err(Clone::clone)?.get(luid).copied().ok_or_else(||"no-valid-3d-engine".into())
        });
        let value=result.as_ref().ok().copied();
        additions.push(json!({"hardware":name,"hardwareType":kind,"identifier":id,"deviceId":device,
            "sensorIdentifier":format!("{id}/windows/3d"),"name":"Windows 3D","sensorType":"Load","unit":"%","value":value}));
        status.push(json!({"device_id":id,"adapter_luid":adapter.as_ref().ok(),"source":"windows-pdh-3d",
            "captured_at_ms":captured,"available":value.is_some(),"reason":result.err()}));
        if let Some(gpus)=payload["dynamic"]["gpu"].as_array_mut(){
            for gpu in gpus.iter_mut().filter(|g|g["identifier"]==id){
                if let Some(v)=value {gpu["usage"]=json!({"value":v,"unit":"%","capturedAt":captured_at,"source":"windows-pdh-3d"});gpu["usage_source"]=json!("3d");}
                else {gpu["usage_source"]=json!("core-fallback");}
            }
        }
    }
    if let Some(rows)=payload["sensors"].as_array_mut(){rows.extend(additions);}
    payload["gpuEngine"]=json!(status);
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn groups_processes_per_engine_and_never_sums_engines_or_adapters(){
        let rows=[("pid_1_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D",20.0),
          ("pid_2_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D",30.0),
          ("pid_1_luid_0x00000000_0x00000001_phys_0_eng_1_engtype_3D",40.0),
          ("pid_1_luid_0x00000000_0x00000002_phys_0_eng_0_engtype_3D",0.0),
          ("pid_1_luid_0x00000000_0x00000001_phys_0_eng_2_engtype_Copy",90.0)];
        let result=aggregate(rows.into_iter().map(|(k,v)|(k.into(),v)));
        assert_eq!(result["00000000:00000001"],50.0);assert_eq!(result["00000000:00000002"],0.0);assert_eq!(result.len(),2);
    }
    #[test] fn rejects_invalid_values_and_deduplicates_instances(){
        let name="pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D";
        let result=aggregate([(name.into(),30.0),(name.into(),30.0),("invalid".into(),99.0)]);
        assert_eq!(result["00000000:00000001"],30.0);
        assert!(aggregate([(name.into(),f64::NAN)]).is_empty());
    }
}
