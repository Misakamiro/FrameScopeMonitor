//! Current controller timings, not SPD profile timings. Public transport stays a string.
mod intel;
mod pawnio;
use serde::{Serialize,Deserialize};
use serde_json::{Value,json};
use std::collections::BTreeMap;
#[cfg(not(test))] use std::sync::{Mutex,OnceLock};
#[cfg(not(test))] use std::time::{Instant,Duration};

#[derive(Debug,Clone,Default,PartialEq,Serialize,Deserialize)]
pub struct Timings {pub cl:u32,pub trcd:u32,pub trp:u32,pub tras:u32,pub trc:u32,pub cr:Option<u32>,pub trfc:Option<u32>}
fn sane(t:&Timings)->bool {(4..=255).contains(&t.cl)&&(1..=255).contains(&t.trcd)&&(1..=255).contains(&t.trp)&&(1..=1023).contains(&t.tras)}
#[derive(Debug,Clone,PartialEq,Serialize)]
pub struct Channel {pub channel:u32,pub cl:u32,pub rcd:Option<u32>,pub rcdrd:Option<u32>,pub rcdwr:Option<u32>,pub rp:u32,pub ras:u32,pub cr:Option<u32>,pub gdm:Option<bool>,pub gear:Option<u32>}
#[derive(Debug,Clone,Serialize)]
pub struct RuntimeMemory {pub status:String,pub source:String,pub captured_at_ms:u64,pub memory_type:Option<String>,pub data_rate_mts:Option<f64>,pub reference_mhz:Option<f64>,pub channels:Vec<Channel>}
fn stamp()->u64 {std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_millis() as u64}
impl RuntimeMemory {fn missing(status:&str)->Self {Self{status:status.into(),source:"memory-controller".into(),captured_at_ms:stamp(),memory_type:None,data_rate_mts:None,reference_mhz:None,channels:vec![]}}}

// Bit definitions ported from ZenStates-Core 869b7e8 Dictionaries/DDR4/5Dictionary.cs (GPL-3.0).
fn amd_channel(channel:u32,ddr5:bool,cfg:u32,t1:u32,t2:u32)->Option<Channel> {
    if [cfg,t1,t2].contains(&u32::MAX)||t1==0{return None;}
    let c=Channel{channel,cl:t1&63,rcd:None,rcdrd:Some((t1>>16)&63),rcdwr:Some((t1>>24)&63),rp:(t2>>16)&63,ras:(t1>>8)&127,
        cr:Some(1+((cfg>>if ddr5{17}else{10})&1)),gdm:Some((cfg>>if ddr5{18}else{11})&1==1),gear:None};
    (c.cl>=4&&c.rp>0&&c.ras>0&&c.rcdrd!=Some(0)&&c.rcdwr!=Some(0)).then_some(c)
}
#[cfg(not(test))]
fn probe()->RuntimeMemory {
    let (vendor,family,model)=crate::cpu_frequency::identity();
    let amd_ddr5=vendor=="AuthenticAMD"&&((family==0x19&&matches!(model,0x61))||(family==0x1a&&matches!(model,0x44)));
    let amd_ddr4=vendor=="AuthenticAMD"&&((family==0x17&&matches!(model,0x71))||(family==0x19&&matches!(model,0x21)));
    if vendor=="AuthenticAMD"&&(amd_ddr4||amd_ddr5) {
        let Some(module)=pawnio::Module::open("AMDFamily17")else{return RuntimeMemory::missing("access-unavailable");};
        let mut r=RuntimeMemory::missing("unavailable");r.source="amd-umc".into();r.memory_type=Some(if amd_ddr5{"DDR5"}else{"DDR4"}.into());
        for ch in 0..2 {
            let rd=|offset:u32|module.read("ioctl_read_smn",0x50000+ch*0x100000+offset);
            if let (Some(cfg),Some(t1),Some(t2))=(rd(0x200),rd(0x204),rd(0x208)) {
                if let Some(c)=amd_channel(ch,amd_ddr5,cfg,t1,t2){r.channels.push(c);}
            }
        }
        if !r.channels.is_empty(){r.status="available".into();}return r;
    }
    if vendor=="GenuineIntel" {
        let Some(layout)=intel::layout(family,model)else{return RuntimeMemory::missing("unsupported");};
        let Some(module)=pawnio::Module::open("IntelMCHBAR")else{return RuntimeMemory::missing("access-unavailable");};
        let rd=|offset|module.read("ioctl_read_dword",offset);
        let Some(imc)=intel::decode(layout,100.0,rd)else{return RuntimeMemory::missing("unavailable");};
        if !matches!(imc.ddr,Some("DDR4"|"DDR5")){return RuntimeMemory::missing("unsupported");}
        let reference=imc.measured_reference_mhz;
        let expected=imc.channels;
        let channels=imc.channel_timings.into_iter().map(|(base,t)|Channel{channel:intel::channel_index(layout,base),cl:t.cl,rcd:Some(t.trcd),rcdrd:None,rcdwr:None,rp:t.trp,ras:t.tras,cr:t.cr,gdm:None,gear:imc.channel_gears.iter().find(|(b,_)|*b==base).map(|(_,g)|*g)}).collect::<Vec<_>>();
        return RuntimeMemory{status:if channels.is_empty(){"unavailable"}else if channels.len()<expected{"partial"}else{"available"}.into(),source:"intel-mchbar".into(),captured_at_ms:stamp(),memory_type:imc.ddr.map(str::to_string),data_rate_mts:reference.and(imc.dram_mhz.map(|v|v*2.0)),reference_mhz:reference,channels};
    }
    RuntimeMemory::missing("unsupported")
}

#[cfg(not(test))]
#[derive(Default)]
struct Cache {reading:bool,value:Option<(RuntimeMemory,Instant)>}
#[cfg(not(test))]
pub fn current()->RuntimeMemory {
    static CACHE:OnceLock<Mutex<Cache>>=OnceLock::new();let cache=CACHE.get_or_init(||Mutex::new(Cache::default()));
    let mut state=cache.lock().unwrap_or_else(|p|p.into_inner());
    let value=state.value.as_ref().filter(|(_,at)|at.elapsed()<Duration::from_secs(2)).map(|(value,_)|value.clone());
    let refresh=state.value.as_ref().map(|(_,at)|at.elapsed()>=Duration::from_secs(1)).unwrap_or(true);
    if refresh&&!state.reading {
        state.reading=true;
        std::thread::spawn(move||{let value=probe();let mut state=cache.lock().unwrap_or_else(|p|p.into_inner());state.value=Some((value,Instant::now()));state.reading=false;});
    }
    value.unwrap_or_else(||RuntimeMemory::missing("pending"))
}
#[cfg(test)]
pub fn current()->RuntimeMemory {RuntimeMemory::missing("test-no-hardware")}

pub fn enrich(hardware:&mut Value,runtime:&RuntimeMemory) {
    hardware["memory_details"]=serde_json::to_value(runtime).unwrap_or(Value::Null);
    match summary(hardware,runtime) {
        Ok(text)=>{hardware["memory_summary"]=json!(text);hardware["memory_summary_error"]=Value::Null;},
        Err(error)=>{hardware["memory_summary"]=Value::Null;hardware["memory_summary_error"]=json!(error);},
    }
}
pub fn summary(hardware:&Value,runtime:&RuntimeMemory)->Result<String,String> {
    let modules=hardware["memory"].as_array().ok_or("内存信息未读取")?;
    let mut total=0.0;let mut brands=BTreeMap::<String,usize>::new();let mut configured=0.0_f64;let mut kind="";
    for m in modules {
        total+=m["capacity_gb"].as_f64().filter(|v|v.is_finite()&&*v>0.0).ok_or("内存容量未完整读取")?;
        let raw=m["manufacturer"].as_str().unwrap_or("").trim();
        let brand=if raw.is_empty()||matches!(raw.to_ascii_lowercase().as_str(),"unknown"|"default string"|"to be filled by o.e.m."){"厂商未读取"}else{raw};
        if brand.chars().any(|c|c.is_control()||['\\','/',':'].contains(&c)){return Err("内存厂商信息无效".into());}
        *brands.entry(brand.to_string()).or_default()+=1;
        configured=configured.max(m["configured_clock_speed"].as_f64().filter(|v|v.is_finite()&&*v>0.0).unwrap_or(0.0));
        kind=m["memory_type"].as_str().filter(|s|matches!(*s,"DDR4"|"DDR5")).unwrap_or(kind);
    }
    if total<=0.0{return Err("内存容量未读取".into());}
    let speed=runtime.data_rate_mts.map(|v|format!("{v:.0}MT/s")).unwrap_or_else(||if configured>0.0{format!("配置{configured:.0}MT/s")}else{"速率未读取".into()});
    let mut parts=vec![format!("{total}GB {kind} {speed}"),brands.into_iter().map(|(b,n)|format!("{b}×{n}")).collect::<Vec<_>>().join("+")];
    if runtime.channels.is_empty(){parts.push("时序未读取".into());}
    else {
        let mut groups=BTreeMap::<String,Vec<u32>>::new();
        for c in &runtime.channels {
            let rcd=if let Some(v)=c.rcd{format!("RCD{v}")}else{format!("RCDRD{}/RCDWR{}",opt(c.rcdrd),opt(c.rcdwr))};
            let text=format!("CL{}/{rcd}/RP{}/RAS{}/CR{}T{}{}",c.cl,c.rp,c.ras,opt(c.cr),if c.gdm==Some(true){"(GDM)"}else{""},c.gear.map(|g|format!("/Gear{g}")).unwrap_or_default());groups.entry(text).or_default().push(c.channel);
        }
        for (text,channels) in groups {parts.push(format!("C{}:{text}",channels.iter().map(u32::to_string).collect::<Vec<_>>().join(",")));}
        if runtime.status=="partial"{parts.push("部分通道未读取".into());}
    }
    let text=parts.join(" | ");if text.chars().count()>256{return Err("内存完整摘要超过256字符，未截断或提交；请保留本地详情".into());}Ok(text)
}
fn opt(v:Option<u32>)->String {v.map(|v|v.to_string()).unwrap_or_else(||"?".into())}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn amd_current_main_timings_separate_read_write_and_command_rate(){
        let c=amd_channel(0,true,3000|(1<<18),30|(76<<8)|(36<<16)|(32<<24),36<<16).unwrap();assert_eq!((c.cl,c.rcdrd,c.rcdwr,c.rp,c.ras,c.cr,c.gdm),(30,Some(36),Some(32),36,76,Some(1),Some(true)));
        let c=amd_channel(1,false,48|(1<<10),16|(36<<8)|(18<<16)|(12<<24),18<<16).unwrap();assert_eq!(c.cr,Some(2));assert!(amd_channel(0,true,0,u32::MAX,0).is_none());
    }
    #[test] fn mixed_brands_channels_and_privacy_survive_summary_without_spd_substitution(){
        let h=json!({"memory":[{"manufacturer":"Kingston","capacity_gb":16,"memory_type":"DDR5","configured_clock_speed":6000,"serial_number":"SECRET"},{"manufacturer":"Corsair","capacity_gb":16,"memory_type":"DDR5"}]});
        let mut r=RuntimeMemory::missing("available");r.channels.push(amd_channel(0,true,3000,30|(76<<8)|(36<<16)|(32<<24),36<<16).unwrap());let s=summary(&h,&r).unwrap();assert!(s.contains("Kingston")&&s.contains("Corsair")&&s.contains("RCDWR32"));assert!(!s.contains("SECRET"));
        r.channels.clear();assert!(summary(&h,&r).unwrap().contains("时序未读取"));
    }
    #[test] fn summary_groups_equal_channels_but_never_truncates_brand_or_timings(){
        let mut r=RuntimeMemory::missing("partial");
        let c=amd_channel(0,true,0,30|(76<<8)|(36<<16)|(32<<24),36<<16).unwrap();r.channels.push(c.clone());let mut other=c;other.channel=1;r.channels.push(other);
        let mut h=json!({"memory":[{"manufacturer":"Kingston","capacity_gb":16,"memory_type":"DDR5"}]});
        let s=summary(&h,&r).unwrap();assert!(s.contains("C0,1:CL30")&&s.contains("部分通道未读取"));
        r.channels[1].rcdwr=Some(40);let s=summary(&h,&r).unwrap();assert!(s.contains("C1:CL30")&&s.contains("RCDWR40"));
        h["memory"][0]["manufacturer"]=json!("A".repeat(256));assert!(summary(&h,&r).unwrap_err().contains("超过256"));
        h["memory"][0]["manufacturer"]=json!("bad\u{0001}brand");assert!(summary(&h,&r).is_err());
        h["memory"][0]["capacity_gb"]=Value::Null;assert!(summary(&h,&r).is_err());
    }
    #[test] fn nominal_module_speed_never_fills_missing_configured_or_measured_rate(){
        let h=json!({"memory":[{"manufacturer":"Kingston","capacity_gb":16,"memory_type":"DDR5","speed_mhz":6000}]});
        let r=RuntimeMemory::missing("access-unavailable");
        let text=summary(&h,&r).unwrap();assert!(text.contains("速率未读取")&&!text.contains("6000"));
    }
}
