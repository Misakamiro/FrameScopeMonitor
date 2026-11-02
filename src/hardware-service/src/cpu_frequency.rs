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
pub fn core_index(name:&str)->Option<usize> {
    let name=name.trim().to_ascii_lowercase();
    let rest=name.strip_prefix("core #").or_else(||name.strip_prefix("cpu core #"))?;
    rest.parse::<usize>().ok()?.checked_sub(1).filter(|n|*n<256)
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
        match (result.vendor.as_str(),family) {("AuthenticAMD",0x17|0x19)=>"amd-ratio-times-lhm-reference",("AuthenticAMD",0x1a)=>"amd-cof-already-mhz",("GenuineIntel",6)=>"intel-perf-ratio-times-tsc-reference",_=>"unclassified-mhz"}
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
        let hybrid=name.starts_with("P-Core #")||name.starts_with("E-Core #");
        // LHM 0.9.6 numbers hybrid names separately; the clock sensor ID is the physical-core index + 1.
        let index=if hybrid {row["identifier"].as_str().and_then(|id|id.rsplit_once("/clock/")).and_then(|(_,id)|id.parse::<usize>().ok()).and_then(|n|n.checked_sub(1)).filter(|n|*n<256)}else{core_index(name)};
        if let Some(index)=index{result.per_core_mhz.resize(result.per_core_mhz.len().max(index+1),None);result.per_core_mhz[index]=Some(value);}
    }
    result.raw_core_mhz=result.per_core_mhz.clone();
    result.reported_average_mhz=snapshot["dynamic"]["cpuFrequency"]["value"].as_f64().filter(|v|valid(*v));
    // Only the pinned Intel TSC-reference calculation is corrected. AMD Zen 5 COF is already MHz.
    let intel_branch=result.vendor=="GenuineIntel"&&result.family==6&&matches!(result.model,0x7d|0x7e|0x97|0x9a|0xb7|0xba|0xbf|0xc6|0xc5|0xb5)&&matches!(result.provider_version.as_deref(),Some("0.9.6.0"|"0.9.6"));
    let scale=measured_reference.filter(|v|intel_branch&&(50.0..=250.0).contains(v)).zip(result.reference_mhz).map(|(measured,old)|measured/old);
    if let Some(scale)=scale {
        for value in result.per_core_mhz.iter_mut().flatten(){*value*=scale;}
        average=average.map(|v|v*scale);
        result.reference_mhz=measured_reference;result.reference_source="intel-mchbar-bclk".into();
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
    result.current_mhz=if values.is_empty(){average.or_else(||snapshot["dynamic"]["cpuFrequency"]["value"].as_f64().filter(|v|valid(*v)).map(|v|v*scale.unwrap_or(1.0)))}else{Some(values.iter().sum::<f64>()/values.len() as f64)};
    if result.current_mhz.is_some(){result.source="lhm-core-clock-mhz".into();result.quality=if scale.is_some(){"reference-corrected-estimate"}else{"sensor-estimate"}.into();}
    result
}
#[cfg(test)]
mod tests {
    use super::*;use serde_json::json;
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
        let r=resolve(&json!({"sensors":[{"hardwareType":"Cpu","sensorType":"Clock","name":"P-Core #1","identifier":"/intelcpu/0/clock/1","value":5250.25},{"hardwareType":"Cpu","sensorType":"Clock","name":"E-Core #1","identifier":"/intelcpu/0/clock/2","value":4200.5}]}));
        assert_eq!(r.per_core_mhz,vec![Some(5250.25),Some(4200.5)]);assert_eq!(r.current_mhz,Some(4725.375));
    }
}
