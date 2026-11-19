//! Core MHz and ratios have distinct units; a MHz value is never scaled twice.
use serde::Serialize;
use serde_json::Value;

#[derive(Debug,Clone,Default,PartialEq,Serialize)]
pub struct CpuFrequencyInfo {
    pub current_mhz:Option<f64>,
    pub per_core_mhz:Vec<Option<f64>>,
    pub raw_core_mhz:Vec<Option<f64>>,
    pub raw_ratios:Vec<Option<f64>>,
    pub reported_average_mhz:Option<f64>,
    pub reference_mhz:Option<f64>,
    pub reference_source:String,
    pub source:String,
    pub quality:String,
    pub calculation:String,
    pub provider_version:Option<String>,
    pub vendor:String,
    pub family:u32,
    pub model:u32,
}
pub fn identity()->(String,u32,u32) {
    #[cfg(target_arch="x86_64")]
    unsafe {
        use std::arch::x86_64::__cpuid;
        let v=__cpuid(0);let id=__cpuid(1).eax;
        let mut name=Vec::new();name.extend(v.ebx.to_le_bytes());name.extend(v.edx.to_le_bytes());name.extend(v.ecx.to_le_bytes());
        let base=(id>>8)&15;let family=base+if base==15{(id>>20)&255}else{0};
        let model=((id>>4)&15)+if base==6||base==15{((id>>16)&15)<<4}else{0};
        return(String::from_utf8_lossy(&name).into_owned(),family,model);
    }
    #[cfg(not(target_arch="x86_64"))]
    (String::new(),0,0)
}
fn valid(v:f64)->bool {v.is_finite()&&v>0.0&&v<=20000.0}
fn zen5_reference_candidate(snapshot:&Value,vendor:&str,family:u32,model:u32,version:Option<&str>)->bool {
    vendor=="AuthenticAMD" && family==0x1a && model==0x44
        && matches!(version,Some("0.9.6.0"|"0.9.6"))
        && snapshot["static"]["cpuName"].as_str().is_some_and(|name|name.split_whitespace().any(|part|part=="9950X3D"))
        && snapshot.get("cpuClockInputs").is_none()
}
fn zen5_reference(snapshot:&Value)->Option<f64> {
    if !matches!(snapshot["availability"].as_str(),Some("available"|"partial")){return None;}
    let rows=snapshot["sensors"].as_array()?;
    let mut reference=None;
    for row in rows {
        if !row["hardwareType"].as_str().is_some_and(|v|v.eq_ignore_ascii_case("Cpu"))
            || !row["sensorType"].as_str().is_some_and(|v|v.eq_ignore_ascii_case("Clock")){continue;}
        let name=row["name"].as_str().unwrap_or("");
        if name.eq_ignore_ascii_case("Bus Speed") || name.eq_ignore_ascii_case("Cores (Average)") || core_index(name).is_some() {
            if row["identifier"].as_str()!=Some("/amdcpu/0"){return None;}
        }
        if name.eq_ignore_ascii_case("Bus Speed") {
            if reference.is_some(){return None;}
            reference=Some(row["value"].as_f64().filter(|v|v.is_finite()&&(50.0..=250.0).contains(v))?);
        }
    }
    reference
}
pub fn core_index(name:&str)->Option<usize> {
    let name=name.trim().to_ascii_lowercase();
    let rest=name.strip_prefix("core #").or_else(||name.strip_prefix("cpu core #"))?;
    rest.parse::<usize>().ok()?.checked_sub(1).filter(|n|*n<256)
}
/// The provider's hardware identifier and sensor identifier are distinct.
/// Hybrid core names number P/E cores separately; only the sensor ID is global.
pub fn sensor_core_index(name:&str,hardware_id:&str,sensor_id:&str,kind:&str)->Option<usize> {
    let name=name.trim().to_ascii_lowercase();
    let name=if kind=="voltage"{name.strip_suffix(" vid").unwrap_or(&name)}else{&name};
    let hybrid=name.strip_prefix("p-core #").or_else(||name.strip_prefix("e-core #"));
    if let Some(number)=hybrid {number.parse::<usize>().ok()?.checked_sub(1)?;}
    else {
        let index=core_index(name)?;
        if !sensor_id.is_empty() {
            let (owner,number)=sensor_id.rsplit_once(&format!("/{kind}/"))?;
            if hardware_id.is_empty()||owner!=hardware_id||number.parse::<usize>().is_err(){return None;}
        }
        // AMD sensor indices can be sparse (clock, effective clock, bus, etc.).
        // A regular Core #N names the core; its sensor slot is not the core index.
        return Some(index);
    }
    if !sensor_id.is_empty() {
        let (owner,number)=sensor_id.rsplit_once(&format!("/{kind}/"))?;
        if hardware_id.is_empty()||owner!=hardware_id{return None;}
        return number.parse::<usize>().ok()?.checked_sub(1).filter(|n|*n<256);
    }
    if hybrid.is_some(){None}else{core_index(name)}
}
pub fn resolve(snapshot:&Value)->CpuFrequencyInfo {
    resolve_with_reference(snapshot,None)
}
pub fn resolve_with_reference(snapshot:&Value,measured_reference:Option<f64>)->CpuFrequencyInfo {
    let (vendor,family,model)=identity();
    resolve_for(snapshot,measured_reference,vendor,family,model)
}
fn resolve_for(snapshot:&Value,measured_reference:Option<f64>,vendor:String,family:u32,model:u32)->CpuFrequencyInfo {
    let mut result=CpuFrequencyInfo{vendor,family,model,source:"unavailable".into(),quality:"unavailable".into(),..Default::default()};
    result.provider_version=snapshot["provider"]["version"].as_str().map(str::to_string);
    result.calculation=if matches!(result.provider_version.as_deref(),Some("0.9.6.0"|"0.9.6")) {
        match (result.vendor.as_str(),family) {("AuthenticAMD",0x17|0x19)=>"amd-ratio-times-lhm-reference",("AuthenticAMD",0x1a)=>"amd-fid-times-5mhz",("GenuineIntel",6)=>"intel-perf-ratio-times-tsc-reference",_=>"unclassified-mhz"}
    }else{"unclassified-mhz"}.into();
    let mut average=None;
    for row in snapshot["sensors"].as_array().into_iter().flatten() {
        if !row["hardwareType"].as_str().unwrap_or("").to_ascii_lowercase().contains("cpu"){continue;}
        let name=row["name"].as_str().unwrap_or("");
        let Some(value)=row["value"].as_f64().filter(|v|valid(*v))else{continue;};
        let kind=row["sensorType"].as_str().unwrap_or("");
        if kind.eq_ignore_ascii_case("factor") {
            if let Some(index)=core_index(name){result.raw_ratios.resize(result.raw_ratios.len().max(index+1),None);result.raw_ratios[index]=Some(value);}
        }
        if !kind.eq_ignore_ascii_case("clock"){continue;}
        if name.eq_ignore_ascii_case("Bus Speed")&&(50.0..=250.0).contains(&value){result.reference_mhz=Some(value);result.reference_source="lhm-tsc-derived".into();}
        if name.eq_ignore_ascii_case("Cores (Average)"){average=Some(value);}
        let index=sensor_core_index(name,row["identifier"].as_str().unwrap_or(""),row["sensorIdentifier"].as_str().unwrap_or(""),"clock");
        if let Some(index)=index{result.per_core_mhz.resize(result.per_core_mhz.len().max(index+1),None);result.per_core_mhz[index]=Some(value);}
    }
    result.raw_core_mhz=result.per_core_mhz.clone();
    result.reported_average_mhz=snapshot["dynamic"]["cpuFrequency"]["value"].as_f64().filter(|v|valid(*v));
    // Intel uses its existing controller reference. The admitted Zen 5 path uses
    // the same CPU's TSC-derived bus; it never borrows a memory-controller clock.
    let intel_branch=result.vendor=="GenuineIntel"&&result.family==6&&matches!(result.model,0x7d|0x7e|0x97|0x9a|0xb7|0xba|0xbf|0xc6|0xc5|0xb5)&&matches!(result.provider_version.as_deref(),Some("0.9.6.0"|"0.9.6"));
    let scale=measured_reference.filter(|v|intel_branch&&(50.0..=250.0).contains(v)).zip(result.reference_mhz).map(|(measured,old)|measured/old);
    if let Some(scale)=scale {
        for value in result.per_core_mhz.iter_mut().flatten(){*value*=scale;}
        average=average.map(|v|v*scale);
        result.reference_mhz=measured_reference;result.reference_source="intel-mchbar-bclk".into();
    }
    let zen5=zen5_reference_candidate(snapshot,&result.vendor,family,model,result.provider_version.as_deref());
    let zen5_reference=if zen5{zen5_reference(snapshot)}else{None};
    if zen5 {
        for value in &mut result.per_core_mhz {
            *value=value.zip(zen5_reference).map(|(mhz,reference)|mhz*reference/100.0).filter(|mhz|valid(*mhz));
        }
        average=average.zip(zen5_reference).map(|(mhz,reference)|mhz*reference/100.0).filter(|mhz|valid(*mhz));
        result.reference_mhz=zen5_reference;
        result.reference_source=if zen5_reference.is_some(){"lhm-tsc-derived"}else{"unavailable"}.into();
        result.calculation=if zen5_reference.is_some(){"amd-fid-times-5mhz-times-tsc-reference-over-100"}else{"amd-eclk-reference-unavailable"}.into();
    }
    // Ratios are accepted only when the producer supplies a verified reference.
    if let Some(inputs)=snapshot["cpuClockInputs"].as_array() {
        for row in inputs {
            let Some(index)=row["core"].as_u64().filter(|i|*i<256).map(|i|i as usize)else{continue;};
            let value=row["value"].as_f64();
            let mhz=match row["unit"].as_str(){
                Some("MHz")=>value,
                Some("ratio") if row["reference_verified"]==true=>value.zip(row["reference_mhz"].as_f64().filter(|b|(50.0..=250.0).contains(b))).map(|(r,b)|r*b),
                _=>None,
            }.filter(|v|valid(*v));
            if let Some(value)=mhz{result.per_core_mhz.resize(result.per_core_mhz.len().max(index+1),None);result.per_core_mhz[index]=Some(value);}
        }
    }
    let values:Vec<f64>=result.per_core_mhz.iter().flatten().copied().collect();
    result.current_mhz=if values.is_empty(){average.or_else(||if zen5{None}else{snapshot["dynamic"]["cpuFrequency"]["value"].as_f64().filter(|v|valid(*v)).map(|v|v*scale.unwrap_or(1.0))})}else{Some(values.iter().sum::<f64>()/values.len() as f64)};
    if result.current_mhz.is_some(){
        result.source=if zen5{"lhm-core-clock-reference-corrected"}else{"lhm-core-clock-mhz"}.into();
        result.quality=if scale.is_some()||zen5{"reference-corrected-estimate"}else{"sensor-estimate"}.into();
    }
    result
}
#[cfg(test)]
mod tests {
    use super::*;use serde_json::json;
    fn admitted_snapshot(bus:Value)->Value {
        json!({"static":{"cpuName":"AMD Ryzen 9 9950X3D 16-Core Processor"},"availability":"available",
            "provider":{"version":"0.9.6.0"},"sensors":[
            {"hardwareType":"Cpu","identifier":"/amdcpu/0","sensorType":"Clock","name":"Bus Speed","value":bus},
            {"hardwareType":"Cpu","identifier":"/amdcpu/0","sensorType":"Clock","name":"Core #1","value":5200.0},
            {"hardwareType":"Cpu","identifier":"/amdcpu/0","sensorType":"Clock","name":"Core #2","value":5235.0},
            {"hardwareType":"Cpu","identifier":"/amdcpu/0","sensorType":"Factor","name":"Core #1","value":48.94},
            {"hardwareType":"Cpu","identifier":"/amdcpu/0","sensorType":"Clock","name":"Cores (Average)","value":5218.0},
            {"hardwareType":"Cpu","identifier":"/amdcpu/0","sensorType":"Clock","name":"Cores (Average Effective)","value":444.0}]})
    }
    #[test] fn admitted_9950x3d_scales_raw_core_mhz_once_and_keeps_precision(){
        let raw=admitted_snapshot(json!(106.25));let original=raw.clone();
        let result=resolve_for(&raw,Some(100.0),"AuthenticAMD".into(),0x1a,0x44);
        assert_eq!(result.per_core_mhz,vec![Some(5525.0),Some(5562.1875)]);
        assert_eq!(result.current_mhz,Some(5543.59375));
        assert_eq!(result.raw_core_mhz,vec![Some(5200.0),Some(5235.0)]);
        assert_eq!(result.reference_source,"lhm-tsc-derived");
        assert_eq!(result.quality,"reference-corrected-estimate");
        assert_eq!(raw,original);
        assert_eq!(resolve_for(&raw,None,"AuthenticAMD".into(),0x1a,0x44),result);
        let normal=resolve_for(&admitted_snapshot(json!(100.0)),None,"AuthenticAMD".into(),0x1a,0x44);
        assert_eq!(normal.per_core_mhz,vec![Some(5200.0),Some(5235.0)]);
    }
    #[test] fn correction_is_limited_to_known_cpu_provider_and_raw_same_domain_inputs(){
        let raw=admitted_snapshot(json!(106.25));let mut cases=Vec::new();
        let mut v=raw.clone();v["static"]["cpuName"]=json!("AMD Ryzen 9 9950X");cases.push(v);
        let mut v=raw.clone();v["provider"]["version"]=json!("future");cases.push(v);
        let mut v=raw.clone();v["cpuClockInputs"]=json!([{ "core":0,"value":5525.0,"unit":"MHz"}]);
        assert_eq!(resolve_for(&v,None,"AuthenticAMD".into(),0x1a,0x44).per_core_mhz[0],Some(5525.0));
        for bus in [Value::Null,json!(0),json!(251),json!("106.25")] {let mut v=raw.clone();v["sensors"][0]["value"]=bus;cases.push(v);}
        let mut v=raw.clone();v["availability"]=json!("unavailable");cases.push(v);
        let mut v=raw.clone();v["sensors"][0]["identifier"]=json!("/amdcpu/1");cases.push(v);
        for v in cases {assert_ne!(resolve_for(&v,None,"AuthenticAMD".into(),0x1a,0x44).quality,"reference-corrected-estimate");}
        for (vendor,family,model) in [("AuthenticAMD",0x17,0x71),("AuthenticAMD",0x19,0x61),("AuthenticAMD",0x1a,0x45),("GenuineIntel",6,0xb7)] {
            assert_eq!(resolve_for(&raw,None,vendor.into(),family,model).current_mhz,Some(5217.5));
        }
    }
    #[test]
    #[ignore = "requires private feedback JSON; never bundle user evidence in public source"]
    fn real_feedback_batches_replay_through_the_production_resolver(){
        let path=std::env::var("FRAMESCOPE_ECLK_FEEDBACK").expect("feedback JSON path");
        let data:Value=serde_json::from_slice(&std::fs::read(path).unwrap()).unwrap();
        let mut outputs=Vec::new();
        for sample in data["samples"].as_array().unwrap(){
            let sensors=sample["rawSensors"].as_array().unwrap().iter().map(|row|{
                let mut row=row.clone();row["hardwareType"]=json!("Cpu");row
            }).collect::<Vec<_>>();
            let raw=json!({"static":{"cpuName":data["cpuName"]},"provider":{"version":sample["frequency"]["provider_version"]},
                "availability":sample["availability"],"capturedAt":sample["capturedAt"],"sequence":sample["sequence"],"sensors":sensors});
            let result=resolve_for(&raw,None,data["cpuIdentity"]["vendor"].as_str().unwrap().into(),
                data["cpuIdentity"]["family"].as_u64().unwrap() as u32,data["cpuIdentity"]["model"].as_u64().unwrap() as u32);
            let expected=sample["comparison"]["hypotheticalAverageMhz"].as_f64().unwrap();
            assert!((result.current_mhz.unwrap()-expected).abs()<1e-8);
            assert_eq!(result.quality,"reference-corrected-estimate");
            assert_eq!(result.per_core_mhz.len(),16);
            outputs.push(json!({"Time":sample["capturedAt"],"cpu_clock":result.current_mhz,"cpu_core_clocks":result.per_core_mhz,"cpu_frequency_info":result}));
        }
        assert_eq!(outputs.len(),26);
        if let Ok(path)=std::env::var("FRAMESCOPE_ECLK_REPLAY_OUTPUT"){std::fs::write(path,serde_json::to_vec_pretty(&outputs).unwrap()).unwrap();}
    }
    #[test] fn unidentified_zen5_is_not_admitted_by_family_alone(){
        let raw=json!({"provider":{"version":"0.9.6.0"},"sensors":[{"hardwareType":"Cpu","sensorType":"Clock","name":"Bus Speed","value":106.25},{"hardwareType":"Cpu","sensorType":"Clock","name":"Core #1","value":5200.0}]});
        let result=resolve_for(&raw,None,"AuthenticAMD".into(),0x1a,0x44);
        assert_eq!(result.current_mhz,Some(5200.0),"CPU brand and clock-domain identity are required");
        assert_eq!(result.calculation,"amd-fid-times-5mhz");
    }
    #[test] fn verified_fractional_reference_is_applied_once_only_to_ratios(){
        let r=resolve(&json!({"cpuClockInputs":[{"core":0,"value":50.0,"unit":"ratio","reference_mhz":105.5,"reference_verified":true},{"core":1,"value":5275.0,"unit":"MHz","reference_mhz":105.5,"reference_verified":true}]}));
        assert_eq!(r.per_core_mhz,vec![Some(5275.0),Some(5275.0)]);
    }
    #[test] fn guessed_reference_and_effective_clock_are_not_current_core_clocks(){
        let r=resolve(&json!({"cpuClockInputs":[{"core":0,"value":50,"unit":"ratio","reference_mhz":100,"reference_verified":false}],"sensors":[{"hardwareType":"Cpu","sensorType":"Clock","name":"Cores (Average Effective)","value":500}]}));assert_eq!(r.current_mhz,None);
    }
    #[test] fn independent_measured_reference_corrects_a_stale_reference_but_not_twice(){
        let raw=serde_json::json!({"provider":{"version":"0.9.6.0"},"sensors":[{"hardwareType":"Cpu","sensorType":"Clock","name":"Bus Speed","value":100.0},{"hardwareType":"Cpu","sensorType":"Clock","name":"Core #1","value":5000.0}]});
        assert_eq!(resolve_for(&raw,Some(105.5),"GenuineIntel".into(),6,0xb7).current_mhz,Some(5275.0));
        let mut already=raw;already["sensors"][0]["value"]=serde_json::json!(105.5);already["sensors"][1]["value"]=serde_json::json!(5275.0);
        assert_eq!(resolve_for(&already,Some(105.5),"GenuineIntel".into(),6,0xb7).current_mhz,Some(5275.0));
        assert_eq!(resolve_for(&already,Some(110.0),"AuthenticAMD".into(),0x1a,0x44).current_mhz,Some(5275.0));
        already["provider"]["version"]=serde_json::json!("future");
        assert_eq!(resolve_for(&already,Some(110.0),"GenuineIntel".into(),6,0xb7).current_mhz,Some(5275.0));
    }
    #[test] fn hybrid_core_sensor_ids_do_not_collapse_p_and_e_names(){
        let r=resolve(&json!({"sensors":[{"hardwareType":"Cpu","sensorType":"Clock","name":"P-Core #1","identifier":"/intelcpu/0","sensorIdentifier":"/intelcpu/0/clock/1","value":5250.25},{"hardwareType":"Cpu","sensorType":"Clock","name":"E-Core #1","identifier":"/intelcpu/0","sensorIdentifier":"/intelcpu/0/clock/2","value":4200.5}]}));
        assert_eq!(r.per_core_mhz,vec![Some(5250.25),Some(4200.5)]);assert_eq!(r.current_mhz,Some(4725.375));
    }
    #[test] fn regular_amd_cores_do_not_use_sparse_sensor_slots_as_core_numbers(){
        let rows=(1..=8).map(|core|json!({"hardwareType":"Cpu","sensorType":"Clock","name":format!("Core #{core}"),
            "identifier":"/amdcpu/0","sensorIdentifier":format!("/amdcpu/0/clock/{}",core*2+1),"value":4950.0})).collect::<Vec<_>>();
        assert_eq!(resolve(&json!({"sensors":rows})).per_core_mhz,vec![Some(4950.0);8]);
        assert_eq!(sensor_core_index("Core #2 VID","/amdcpu/0","/amdcpu/0/voltage/9","voltage"),Some(1));
        assert_eq!(sensor_core_index("Core #2","/amdcpu/0","/amdcpu/1/clock/4","clock"),None);
    }
}
