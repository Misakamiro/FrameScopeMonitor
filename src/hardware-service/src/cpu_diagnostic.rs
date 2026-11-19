//! CPU-only evidence collection. Runtime results and diagnostic hypotheses stay distinct.
use crate::cpu_frequency::CpuFrequencyInfo;
use serde_json::{json, Value};
use std::time::{Duration, Instant};

pub fn parse_seconds(args: &[String]) -> Result<Option<u64>, &'static str> {
    let positions=args.iter().enumerate().filter(|(_,v)|v.as_str()=="--cpu-clock-seconds").map(|(i,_)|i).collect::<Vec<_>>();
    if positions.is_empty(){return Ok(None);}
    if positions.len()!=1 || !args.iter().any(|v|v=="--hardware-diagnostic") {
        return Err("--cpu-clock-seconds requires --hardware-diagnostic and may appear once");
    }
    let seconds=args.get(positions[0]+1).and_then(|v|v.parse::<u64>().ok()).filter(|v|(1..=30).contains(v));
    seconds.map(Some).ok_or("--cpu-clock-seconds must be an integer from 1 to 30")
}

// Corrected samples show the actual resolver result. Other eligible raw samples
// may show a labelled hypothesis; diagnostic arithmetic never updates runtime clocks.
fn comparison(frequency: &CpuFrequencyInfo, brand: &str) -> Value {
    if frequency.source=="lhm-core-clock-reference-corrected" && frequency.quality=="reference-corrected-estimate" {
        return json!({"status":"limited-candidate-correction-applied","appliedToRuntime":true,
            "referenceSource":frequency.reference_source,"referenceMhz":frequency.reference_mhz,
            "formulaBasisMhz":100.0,"formula":"raw_core_mhz * lhm_bus_mhz / 100",
            "correctedPerCoreMhz":frequency.per_core_mhz,"correctedAverageMhz":frequency.current_mhz});
    }
    let rejected=||json!({"status":"unavailable-or-ineligible","appliedToRuntime":false});
    if !brand.split_whitespace().any(|part|part=="9950X3D")
        || frequency.vendor!="AuthenticAMD" || frequency.family!=0x1a || frequency.model!=0x44
        || !matches!(frequency.provider_version.as_deref(),Some("0.9.6.0"|"0.9.6"))
        || frequency.reference_source!="lhm-tsc-derived"
        || frequency.calculation!="amd-fid-times-5mhz" || frequency.quality!="sensor-estimate"
        || frequency.source!="lhm-core-clock-mhz" {return rejected();}
    let Some(reference)=frequency.reference_mhz.filter(|v|v.is_finite()&&(50.0..=250.0).contains(v)) else{return rejected();};
    let cores=frequency.raw_core_mhz.iter().map(|v|v.filter(|v|v.is_finite()&&*v>0.0&&*v<=20000.0)
        .map(|v|v*reference/100.0).filter(|v|*v<=20000.0)).collect::<Vec<_>>();
    let values=cores.iter().flatten().copied().collect::<Vec<_>>();
    if values.is_empty(){return rejected();}
    json!({"status":"hypothesis-only-needs-same-machine-comparison","appliedToRuntime":false,
        "formula":"raw_core_mhz * lhm_bus_mhz / 100","formulaBasisMhz":100.0,
        "referenceMhz":reference,"referenceSource":"lhm-tsc-derived-not-independent-eclk-verified",
        "hypotheticalPerCoreMhz":cores,"hypotheticalAverageMhz":values.iter().sum::<f64>()/values.len() as f64})
}

fn sensor_id(value: &Value) -> Option<&str> {
    value.as_str().filter(|s|s.len()<=160 && ["/amdcpu/","/intelcpu/","/cpu/"].iter().any(|prefix|s.starts_with(prefix))
        && s.bytes().all(|b|b.is_ascii_alphanumeric()||b"/_-".contains(&b)))
}
fn sensor_name(name: &str) -> bool {
    name=="Bus Speed" || name=="Cores (Average)" || name=="Cores (Average Effective)"
        || crate::cpu_frequency::core_index(name.trim_end_matches(" (Effective)")).is_some()
        || ["P-Core #","E-Core #"].iter().any(|prefix|name.strip_prefix(prefix)
            .and_then(|v|v.trim_end_matches(" (Effective)").parse::<u32>().ok()).is_some_and(|v|v>0&&v<=256))
}
fn sample(snapshot: &Value, elapsed_ms: u64, last_sequence: &mut Option<u64>, brand: &str, frequency: CpuFrequencyInfo) -> Value {
    let sequence=snapshot["sequence"].as_u64();
    let timestamp=snapshot["capturedAt"].as_str().filter(|s|s.len()>=20&&s.len()<=40
        && s.bytes().all(|b|b.is_ascii_digit()||b"TZ:+-.".contains(&b)));
    let advancing=sequence.is_some_and(|seq|last_sequence.is_none_or(|last|seq>last));
    if advancing {*last_sequence=sequence;}
    let availability=match snapshot["availability"].as_str(){Some("available")=>"available",Some("partial")=>"partial",_=>"unavailable"};
    let fresh=advancing&&timestamp.is_some()&&availability!="unavailable";
    let sensors=snapshot["sensors"].as_array().into_iter().flatten().filter_map(|row|{
        if !row["hardwareType"].as_str().is_some_and(|s|s.eq_ignore_ascii_case("Cpu")){return None;}
        let name=row["name"].as_str()?;let kind=row["sensorType"].as_str()?;
        if !sensor_name(name)||!matches!(kind,"Clock"|"Factor"){return None;}
        Some(json!({"name":name,"sensorType":kind,"value":row["value"].as_f64().filter(|v|v.is_finite()),
            "unit":if kind=="Clock"{"MHz"}else{"ratio"},"identifier":sensor_id(&row["identifier"]),
            "sensorIdentifier":sensor_id(&row["sensorIdentifier"])}))
    }).collect::<Vec<_>>();
    json!({"elapsedMs":elapsed_ms,"capturedAt":timestamp,"sequence":sequence,"availability":availability,
        "freshBatch":fresh,"sensorFreshness":"provider-update-only","rawSensors":sensors,
        "providerErrorCount":snapshot["errors"].as_array().map_or(0,Vec::len),
        "frequency":if fresh{serde_json::to_value(&frequency).unwrap_or(Value::Null)}else{Value::Null},
        "comparison":if fresh{comparison(&frequency,brand)}else{Value::Null}})
}

fn cpu_brand() -> String {
    #[cfg(target_arch="x86_64")]
    unsafe {
        use std::arch::x86_64::__cpuid;
        if __cpuid(0x80000000).eax>=0x80000004 {
            let mut bytes=Vec::with_capacity(48);
            for leaf in 0x80000002..=0x80000004 {let value=__cpuid(leaf);for part in [value.eax,value.ebx,value.ecx,value.edx]{bytes.extend(part.to_le_bytes());}}
            return String::from_utf8_lossy(&bytes).trim_matches('\0').trim().to_string();
        }
    }
    String::new()
}

/// A separate owned helper is used; no installed service, FPS session, or user configuration is touched.
pub fn capture(helper: String, seconds: u64) -> Value {
    if !(1..=30).contains(&seconds){return json!({"status":"invalid-duration","readOnly":true});}
    let brand=cpu_brand();let (vendor,family,model)=crate::cpu_frequency::identity();
    let mut provider=crate::provider::ResidentProvider::new();
    if provider.set_helper(Some(helper)).is_err(){return json!({"status":"helper-unavailable","readOnly":true});}
    let started=Instant::now();let deadline=started+Duration::from_secs(seconds);
    let mut samples=Vec::new();let mut last_sequence=None;let mut transport_failed=false;
    while Instant::now()<deadline {
        match provider.send_raw_until("{\"cmd\":\"read\"}",deadline) {
            Ok(snapshot)=>{
                let frequency=crate::cpu_frequency::resolve(&snapshot);
                samples.push(sample(&snapshot,started.elapsed().as_millis() as u64,&mut last_sequence,&brand,frequency));
            }
            Err(_)=>{transport_failed=true;break;}
        }
        let remaining=deadline.saturating_duration_since(Instant::now());
        if !remaining.is_zero(){std::thread::sleep(remaining.min(Duration::from_secs(1)));}
    }
    let sampling_elapsed=started.elapsed().as_millis() as u64;
    let cleaned=provider.kill().is_ok();
    let usable=samples.iter().filter(|s|s["freshBatch"]==true&&s["frequency"]["current_mhz"].is_number()).count();
    let corrected=samples.iter().any(|s|s["comparison"]["appliedToRuntime"]==true);
    json!({"schema":2,"mode":"cpu-clock-diagnostic","readOnly":true,"cpuName":brand,
        "cpuIdentity":{"vendor":vendor,"family":family,"model":model},"requestedSeconds":seconds,
        "samplingElapsedMs":sampling_elapsed,"totalElapsedMs":started.elapsed().as_millis() as u64,
        "status":if !cleaned{"cleanup-failed"}else if transport_failed{"provider-read-failed"}else if usable==0{"no-usable-cpu-readings"}else{"completed"},
        "cleanupSucceeded":cleaned,"usableSamples":usable,"samples":samples,
        "correctionEnabled":corrected,"correctionAvailable":true,"validationStatus":"limited-candidate-awaiting-post-fix-machine-validation",
        "notes":["Reference correction is limited to 9950X3D Family 1Ah Model 44h with LHM 0.9.6.","The bus is TSC-derived; the 100 MHz divisor is a formula basis, not a measured reference.",
            "Fresh provider batches do not prove that every MSR was refreshed inside LibreHardwareMonitor.",
            "Collection is bounded to 30 seconds; owned-helper cleanup can take up to 6 additional seconds."]})
}

#[cfg(test)]
mod tests {
    use super::*;
    const BRAND: &str = "AMD Ryzen 9 9950X3D 16-Core Processor";
    fn frequency(reference: Option<f64>) -> CpuFrequencyInfo {
        CpuFrequencyInfo { vendor: "AuthenticAMD".into(), family: 0x1a, model: 0x44,
            provider_version: Some("0.9.6.0".into()), raw_core_mhz: vec![Some(5200.0), Some(5235.0)],
            per_core_mhz: vec![Some(5200.0), Some(5235.0)], current_mhz: Some(5217.5),
            raw_ratios: vec![Some(48.94), Some(49.27)], reference_mhz: reference,
            reference_source: "lhm-tsc-derived".into(), source: "lhm-core-clock-mhz".into(),
            quality: "sensor-estimate".into(), calculation: "amd-fid-times-5mhz".into(), ..Default::default() }
    }
    fn snapshot() -> Value {
        json!({"capturedAt":"2026-11-02T00:00:00Z","sequence":1,"availability":"available",
            "account":"SECRET_ACCOUNT","errors":["C:\\SECRET_PATH"],
            "sensors":[
                {"hardwareType":"Cpu","name":"Bus Speed","sensorType":"Clock","value":106.25,"identifier":"/amdcpu/0","sensorIdentifier":"/amdcpu/0/clock/0","serial":"SECRET_SERIAL"},
                {"hardwareType":"Cpu","name":"Core #1","sensorType":"Clock","value":5200.0,"identifier":"/amdcpu/0","sensorIdentifier":"/amdcpu/0/clock/1"},
                {"hardwareType":"Cpu","name":"Core #1","sensorType":"Factor","value":48.94},
                {"hardwareType":"Cpu","name":"Core #1 (Effective)","sensorType":"Clock","value":444.0},
                {"hardwareType":"Cpu","name":"VID","sensorType":"Voltage","value":1.1},
                {"hardwareType":"Gpu","name":"Core","sensorType":"Clock","value":2000.0}
            ]})
    }
    #[test] fn bounded_duration_is_explicit_and_legacy_mode_is_unchanged() {
        let args=|s:&str|s.split_whitespace().map(str::to_owned).collect::<Vec<_>>();
        assert_eq!(parse_seconds(&args("service --hardware-diagnostic")),Ok(None));
        assert_eq!(parse_seconds(&args("service --hardware-diagnostic --cpu-clock-seconds 30")),Ok(Some(30)));
        for tail in ["0","31","-1","NaN",""] {
            assert!(parse_seconds(&args(&format!("service --hardware-diagnostic --cpu-clock-seconds {tail}"))).is_err());
        }
        assert!(parse_seconds(&args("service --cpu-clock-seconds 1")).is_err());
        assert!(parse_seconds(&args("service --hardware-diagnostic --cpu-clock-seconds 1 --cpu-clock-seconds 2")).is_err());
    }
    #[test] fn hypothesis_scales_raw_mhz_once_without_changing_runtime_or_using_factor() {
        let info=frequency(Some(106.25));let before=info.clone();
        let result=comparison(&info,BRAND);
        assert_eq!(result["hypotheticalPerCoreMhz"],json!([5525.0,5562.1875]));
        assert_eq!(result["hypotheticalAverageMhz"],5543.59375);
        assert_eq!(result["appliedToRuntime"],false);
        assert_eq!(info,before);assert_eq!(result,comparison(&info,BRAND));
        assert_eq!(comparison(&frequency(Some(100.0)),BRAND)["hypotheticalPerCoreMhz"],json!([5200.0,5235.0]));
    }
    #[test] fn admitted_runtime_result_is_reported_without_a_second_correction() {
        let mut info=frequency(Some(106.25));
        info.source="lhm-core-clock-reference-corrected".into();info.quality="reference-corrected-estimate".into();
        info.calculation="amd-fid-times-5mhz-times-tsc-reference-over-100".into();
        info.per_core_mhz=vec![Some(5525.0),Some(5562.1875)];info.current_mhz=Some(5543.59375);
        let result=comparison(&info,BRAND);
        assert_eq!(result["appliedToRuntime"],true);
        assert_eq!(result["correctedAverageMhz"],5543.59375);
        assert!(result["hypotheticalAverageMhz"].is_null());
        assert_eq!(result,comparison(&info,BRAND));
    }
    #[test] fn unknown_domains_versions_models_and_corrected_inputs_never_get_scaled() {
        let mut variants=vec![frequency(None),frequency(Some(f64::NAN)),frequency(Some(0.0)),frequency(Some(251.0))];
        let mut v=frequency(Some(106.25));v.vendor="GenuineIntel".into();variants.push(v);
        let mut v=frequency(Some(106.25));v.family=0x19;variants.push(v);
        let mut v=frequency(Some(106.25));v.model=0x61;variants.push(v);
        let mut v=frequency(Some(106.25));v.provider_version=Some("future".into());variants.push(v);
        let mut v=frequency(Some(106.25));v.quality="reference-corrected-estimate".into();variants.push(v);
        let mut v=frequency(Some(106.25));v.reference_source="intel-mchbar-bclk".into();variants.push(v);
        let mut v=frequency(Some(106.25));v.raw_core_mhz=vec![None,Some(f64::INFINITY)];variants.push(v);
        for v in variants {assert!(comparison(&v,BRAND)["hypotheticalPerCoreMhz"].is_null());}
        assert!(comparison(&frequency(Some(106.25)),"AMD Ryzen 9 9950X")["hypotheticalPerCoreMhz"].is_null());
    }
    #[test] fn raw_cpu_sensors_keep_precision_identity_and_distinct_effective_clock_without_private_fields() {
        let result=sample(&snapshot(),500,&mut None,BRAND,frequency(Some(106.25)));
        assert_eq!(result["rawSensors"].as_array().unwrap().len(),4);
        assert_eq!(result["rawSensors"][0]["value"],106.25);
        assert_eq!(result["rawSensors"][1]["sensorIdentifier"],"/amdcpu/0/clock/1");
        assert_eq!(result["rawSensors"][2]["sensorType"],"Factor");
        assert_eq!(result["frequency"]["current_mhz"],5217.5);
        assert_eq!(result["providerErrorCount"],1);
        assert!(!result.to_string().contains("SECRET"));
        assert_eq!(result["sensorFreshness"],"provider-update-only");
    }
    #[test] fn repeated_and_invalid_batches_cannot_be_reported_as_fresh_or_used_for_a_hypothesis() {
        let mut last=None;let raw=snapshot();
        assert_eq!(sample(&raw,0,&mut last,BRAND,frequency(Some(106.25)))["freshBatch"],true);
        let repeated=sample(&raw,1000,&mut last,BRAND,frequency(Some(106.25)));
        assert_eq!(repeated["freshBatch"],false);assert!(repeated["frequency"].is_null());assert!(repeated["comparison"].is_null());
        let mut invalid=raw;invalid["sequence"]=json!(2);invalid["availability"]=json!("unavailable");
        assert_eq!(sample(&invalid,2000,&mut last,BRAND,frequency(Some(106.25)))["freshBatch"],false);
    }
}
