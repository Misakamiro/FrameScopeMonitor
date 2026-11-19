use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Clone,Debug,Serialize,Deserialize,PartialEq)]
pub struct SensorEntry {
    pub device_id:String, pub sensor_id:String, pub device_name:String,
    pub name:String, pub category:String, pub unit:String,
    pub value:Option<f64>, pub available:bool,
}
pub fn category_unit(category:&str)->Option<&'static str> {
    Some(match category {
        "cpu_clock"|"gpu_clock"|"gpu_memory_clock"=>"MHz",
        "cpu_voltage"|"gpu_voltage"=>"V", "cpu_power"|"gpu_power"=>"W",
        "cpu_temp"|"gpu_temp"|"ssd_temp"=>"°C", "cpu_usage"|"gpu_usage"|"memory_usage"=>"%",
        "cpu_fan_speed"|"gpu_fan_speed"=>"RPM", "gpu_vram"=>"GB", _=>return None,
    })
}
pub fn catalogue(payload:&Value)->Vec<SensorEntry> {
    let mut entries=Vec::new();
    for row in payload["sensors"].as_array().into_iter().flatten() {
        let text=|key:&str|row[key].as_str().unwrap_or("");
        let hardware=text("hardwareType").to_ascii_lowercase();
        let name=text("name");let kind=text("sensorType");
        let device=if hardware.contains("cpu"){"cpu"}else if hardware.contains("gpu"){"gpu"}
            else if hardware.contains("memory"){"memory"}else if hardware.contains("storage"){"ssd"}else{continue};
        let metric=match kind {"Clock"=>"clock","Voltage"=>"voltage","Power"=>"power","Temperature"=>"temp","Load"=>"usage","Fan"=>"fan_speed","SmallData" if device=="gpu"&&name=="GPU Memory Used"=>"vram",_=>continue};
        let category=if device=="gpu"&&kind=="Clock"&&name.to_ascii_lowercase().contains("memory"){"gpu_memory_clock".into()}else{format!("{device}_{metric}")};
        let Some(unit)=category_unit(&category)else{continue};
        let device_id=text("identifier");let sensor_id=text("sensorIdentifier");
        if device_id.is_empty()||sensor_id.is_empty(){continue;}
        let value=row["value"].as_f64().filter(|v|v.is_finite()).map(|v|if metric=="vram"{v/1024.0}else{v});
        entries.push(SensorEntry{device_id:device_id.into(),sensor_id:sensor_id.into(),device_name:text("hardware").into(),
            name:name.into(),category,unit:unit.into(),value,available:value.is_some()});
    }
    entries.sort_by(|a,b|(&a.device_id,&a.sensor_id).cmp(&(&b.device_id,&b.sensor_id)));
    entries.dedup_by(|a,b|a.device_id==b.device_id&&a.sensor_id==b.sensor_id);
    entries
}
