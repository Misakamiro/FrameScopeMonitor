use crate::{overlay_panel::OverlaySettings,overlay_sensors::{category_unit,SensorEntry}};
use serde::{Deserialize,Serialize};
use serde_json::Value;

#[derive(Clone,Debug,Default,Serialize,Deserialize)]
pub struct ItemOptions {
    #[serde(default,skip_serializing_if="Option::is_none")]
    pub binding:Option<SensorBinding>,
    #[serde(default,skip_serializing_if="Option::is_none")]
    pub font_color:Option<String>,
    #[serde(default,skip_serializing_if="Option::is_none")]
    pub inherit_font_color:Option<bool>,
    #[serde(default,skip_serializing_if="Option::is_none")]
    pub font_size:Option<u32>,
    #[serde(default,skip_serializing_if="Option::is_none")]
    pub font_bold:Option<bool>,
    #[serde(default,skip_serializing_if="Option::is_none")]
    pub metric:Option<String>,
}
#[derive(Clone,Debug,Serialize,Deserialize)]
pub struct SensorBinding {pub category:String,pub device_id:String,pub sensor_id:String}
#[derive(Clone,Debug,Serialize,Deserialize)]
pub struct ResolvedItem {
    pub id:String,pub metric:String,pub label:String,pub value:String,
    pub color:Option<String>,pub font_size:u32,pub font_bold:bool,
    #[serde(default,skip_serializing_if="Vec::is_empty")]
    pub core_rows:Vec<CoreRow>,
}
#[derive(Clone,Debug,Serialize,Deserialize)]
pub struct CoreRow { pub label:String,pub cells:Vec<ResolvedItem> }
fn color_valid(value:&str)->bool {value.len()==7&&value.starts_with('#')&&value.as_bytes()[1..].iter().all(u8::is_ascii_hexdigit)}
pub fn validate(options:&ItemOptions,metric:&str,custom:bool)->Result<(),String>{
    if options.font_size.is_some_and(|n|!(8..=72).contains(&n)){return Err("字号应为 8–72".into());}
    if options.font_color.as_ref().is_some_and(|v|!color_valid(v)){return Err("字体颜色必须为 #RRGGBB".into());}
    if !custom&&options.metric.is_some(){return Err("内置项不能更改指标类别".into());}
    if custom&&!metric.is_empty()&&category_unit(metric).is_none(){return Err("自定义指标类别无效".into());}
    if let Some(binding)=&options.binding {
        if category_unit(metric).is_none()||binding.category!=metric||binding.device_id.is_empty()||binding.sensor_id.is_empty()
            ||binding.device_id.len()>512||binding.sensor_id.len()>512 {return Err("接口与显示项的设备类别或指标不匹配".into());}
    }
    Ok(())
}
fn formatted(value:Option<f64>,unit:&str)->String{
    value.filter(|v|v.is_finite()).map(|v|if unit=="V"{format!("{v:.3}{unit}")}
        else if unit=="MHz"||unit=="RPM"{format!("{v:.0}{unit}")}
        else{format!("{v:.1}{unit}")}).unwrap_or_else(||format!("--{unit}"))
}
pub fn resolve(settings:&OverlaySettings,data:&Value)->Vec<ResolvedItem>{
    let catalog:Vec<SensorEntry>=serde_json::from_value(data["sensor_catalog"].clone()).unwrap_or_default();
    let mut result=Vec::new();
    let mut add=|id:&str,metric:&str,label:&str,options:&ItemOptions,text:Option<&str>,legacy_color:Option<&str>|{
        let mut label=label.to_string();
        let unit=category_unit(metric).unwrap_or(if metric=="game_ping"{"ms"}else{""});
        let value=if let Some(binding)=&options.binding {
            // No name-based rebind and no fallback for a manually selected interface.
            let sensor=catalog.iter().find(|s|s.device_id==binding.device_id&&s.sensor_id==binding.sensor_id
                &&s.category==metric&&s.unit==unit&&s.available);
            if metric=="cpu_voltage"&&sensor.is_some_and(|s|s.name.to_ascii_lowercase().contains("vid")){label.push_str(" (VID)");}
            formatted(sensor.and_then(|s|s.value),unit)
        }else if let Some(text)=text {text.to_string()}
        else if metric=="time" {
            let offset=data["net_time_offset_ms"].as_i64().unwrap_or(0);
            (chrono::Utc::now()+chrono::Duration::milliseconds(offset)).with_timezone(&chrono::FixedOffset::east_opt(8*3600).unwrap()).format("%H:%M:%S").to_string()
        }else if metric=="delta_password" {data[metric].as_str().unwrap_or("--").into()}
        else if metric=="gpu_vram" {match(data["gpu_vram_used"].as_f64(),data["gpu_vram_total"].as_f64()){
            (Some(used),Some(total))=>format!("{used:.1}/{total:.1}GB"),_=>"--GB".into()}}
        else {
            if metric=="gpu_usage" {label.push_str(match data["gpu_usage_source"].as_str(){Some("3d")=>" (3D)",Some("core-fallback")=>" (Core 回退)",_=>""});}
            if metric=="cpu_voltage"&&data["cpu_voltage_kind"]=="vid-average"{label.push_str(" (平均 VID)");}
            formatted(data[metric].as_f64(),unit)
        };
        result.push(ResolvedItem{id:id.into(),metric:metric.into(),label,value,color:options.font_color.clone().or_else(||legacy_color.map(str::to_owned)),
            font_size:options.font_size.unwrap_or(settings.font_size),font_bold:options.font_bold.unwrap_or(settings.font_bold),core_rows:Vec::new()});
    };
    let clocks=settings.display_items.iter().find(|i|i.enabled&&i.id=="cpu_core_clocks");
    let volts=settings.display_items.iter().find(|i|i.enabled&&i.id=="cpu_core_voltages");
    let combine=clocks.is_some()&&volts.is_some();
    for item in settings.display_items.iter().filter(|i|i.enabled){
        if matches!(item.id.as_str(),"cpu_core_clocks"|"cpu_core_voltages") {
            if settings.style != "vertical_panel" { continue; }
            if combine&&item.id=="cpu_core_voltages"{continue;}
            // Resolve each part with its own style, then group without repeating the full title.
            let values=data[&item.id].as_array();
            let count=values.map_or(0,Vec::len).max(if combine{data["cpu_core_voltages"].as_array().map_or(0,Vec::len)}else{0});
            add(&item.id,&item.id,&item.label,&item.options,Some(if count==0{"--"}else{""}),None);
            for i in 0..count {
                let unit=if item.id=="cpu_core_clocks"{"MHz"}else{"V"};
                let suffix=if unit=="V"&&data["cpu_core_voltage_kind"]=="vid"{" VID"}else{""};
                let text=format!("{}{suffix}",formatted(values.and_then(|v|v.get(i)).and_then(Value::as_f64),unit));
                add(&format!("{}:{i}",item.id),&item.id,&format!("Core {}",i+1),&item.options,Some(&text),None);
                if combine {
                    let voltage=volts.unwrap();
                    let text=format!("{}{}",formatted(data["cpu_core_voltages"].get(i).and_then(Value::as_f64),"V"),
                        if data["cpu_core_voltage_kind"]=="vid"{" VID"}else{""});
                    add(&format!("cpu_core_voltages:{i}"),"cpu_core_voltages","",&voltage.options,Some(&text),None);
                }
            }
        }else{add(&item.id,&item.id,&item.label,&item.options,None,None);}
    }
    for item in settings.custom_items.iter().filter(|i|i.enabled){
        let metric=item.options.metric.as_deref().unwrap_or("");
        add(&item.id,metric,if metric.is_empty(){""}else{&item.text},&item.options,
            if metric.is_empty(){Some(&item.text)}else{None},if item.options.inherit_font_color==Some(true){None}else{Some(&item.color)});
    }
    let mut grouped:Vec<ResolvedItem>=Vec::new();
    for item in result {
        if matches!(item.metric.as_str(),"cpu_core_clocks"|"cpu_core_voltages")&&item.id.contains(':') {
            if let Some(group)=grouped.last_mut() {
                if item.label.is_empty() {
                    if let Some(row)=group.core_rows.last_mut(){row.cells.push(item);}
                }else{group.core_rows.push(CoreRow{label:item.label.clone(),cells:vec![item]});}
            }
        }else{
            let mut item=item;
            if combine&&item.metric=="cpu_core_clocks" {item.label=format!("{} / {}",item.label,volts.unwrap().label);}
            if item.metric=="cpu_core_voltages"&&data["cpu_core_voltage_kind"]=="vid" {item.label.push_str(" (VID)");}
            grouped.push(item);
        }
    }
    grouped
}
pub fn enrich(mut data:Value)->Value{
    let settings=crate::overlay_panel::get_or_init_settings();
    data["resolved_items"]=serde_json::to_value(resolve(&settings,&data)).unwrap_or(Value::Null);data
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    #[test] fn core_groups_keep_compact_names_missing_values_and_independent_styles(){
        let mut settings=OverlaySettings::default();settings.custom_items.clear();
        settings.style="vertical_panel".into();
        settings.display_items=serde_json::from_value(json!([
            {"id":"cpu_core_clocks","label":"频率","enabled":true,"font_size":14,"font_color":"#123456"},
            {"id":"cpu_core_voltages","label":"电压","enabled":true,"font_size":22,"font_bold":true}
        ])).unwrap();
        let result=resolve(&settings,&json!({"cpu_core_clocks":[5000,null,5100],"cpu_core_voltages":[1.1,1.2,null],"cpu_core_voltage_kind":"vid"}));
        assert_eq!(result.len(),1);assert_eq!(result[0].core_rows.len(),3);
        let row=&result[0].core_rows[1];assert_eq!(row.label,"Core 2");
        assert_eq!(row.cells[0].value,"--MHz");assert_eq!(row.cells[1].value,"1.200V VID");
        assert_eq!(row.cells[0].font_size,14);assert_eq!(row.cells[1].font_size,22);assert!(row.cells[1].font_bold);
        let saved=settings.display_items.clone();
        for style in ["default","dynamic_island"] {settings.style=style.into();assert!(resolve(&settings,&json!({"cpu_core_clocks":[5000],"cpu_core_voltages":[1.1]})).is_empty());}
        settings.style="vertical_panel".into();
        assert_eq!(serde_json::to_value(&settings.display_items).unwrap(),serde_json::to_value(saved).unwrap());
        settings.display_items[0].enabled=false;
        assert_eq!(resolve(&settings,&json!({"cpu_core_voltages":[1.1]}))[0].core_rows[0].cells.len(),1);
    }
    #[test] fn custom_color_can_restore_inheritance_without_rewriting_legacy_color(){
        let mut settings=OverlaySettings::default();settings.display_items.clear();
        settings.custom_items=serde_json::from_value(json!([{"id":"a","text":"text","color":"#123456","enabled":true}])).unwrap();
        assert_eq!(resolve(&settings,&json!({}))[0].color.as_deref(),Some("#123456"));
        settings.custom_items[0].options.inherit_font_color=Some(true);
        let settings:OverlaySettings=serde_json::from_value(serde_json::to_value(settings).unwrap()).unwrap();
        assert!(resolve(&settings,&json!({}))[0].color.is_none());
    }
    #[test] fn manual_binding_keeps_identity_and_style_when_sensor_disappears(){
        let mut settings=OverlaySettings::default();
        settings.display_items.clear();settings.custom_items.clear();
        settings.font_size=17;settings.font_bold=true;
        let item=json!({"id":"cpu_clock","label":"主核","enabled":true,"font_bold":false,
            "binding":{"category":"cpu_clock","device_id":"cpu0","sensor_id":"clock1"}});
        settings.display_items.push(serde_json::from_value(item).unwrap());
        let sensor=json!({"device_id":"cpu0","sensor_id":"clock1","device_name":"CPU","name":"P-Core #1","category":"cpu_clock","unit":"MHz","value":5300,"available":true});
        let data=json!({"cpu_clock":4000,"sensor_catalog":[sensor]});
        let result=resolve(&settings,&data);assert_eq!(result[0].value,"5300MHz");assert_eq!(result[0].font_size,17);assert!(!result[0].font_bold);
        assert_eq!(resolve(&settings,&json!({"cpu_clock":4000}))[0].value,"--MHz");
        let mut wrong=data.clone();wrong["sensor_catalog"][0]["category"]=json!("gpu_clock");
        assert_eq!(resolve(&settings,&wrong)[0].value,"--MHz");
        let saved=serde_json::to_value(&settings).unwrap();
        assert_eq!(resolve(&serde_json::from_value(saved).unwrap(),&data)[0].value,"5300MHz");
    }
    #[test] fn custom_items_select_different_cores_without_overwriting(){
        let mut settings=OverlaySettings::default();settings.display_items.clear();
        settings.custom_items=serde_json::from_value(json!([
            {"id":"a","text":"P 核","color":"#FFFFFF","enabled":true,"metric":"cpu_clock","binding":{"category":"cpu_clock","device_id":"cpu0","sensor_id":"clock1"}},
            {"id":"b","text":"E 核","color":"#FFFFFF","enabled":true,"metric":"cpu_clock","binding":{"category":"cpu_clock","device_id":"cpu0","sensor_id":"clock9"}}
        ])).unwrap();
        let sensors=vec![("clock1",5300),("clock9",4200)].into_iter().map(|(id,v)|json!({"device_id":"cpu0","sensor_id":id,"device_name":"CPU","name":id,"category":"cpu_clock","unit":"MHz","value":v,"available":true})).collect::<Vec<_>>();
        let rows=resolve(&settings,&json!({"sensor_catalog":sensors}));
        assert_eq!(rows.iter().map(|r|r.value.as_str()).collect::<Vec<_>>(),["5300MHz","4200MHz"]);
    }
    #[test] fn bindings_are_typed_and_frame_sources_locked(){
        let mut options=ItemOptions::default();options.binding=Some(SensorBinding{category:"cpu_clock".into(),device_id:"/cpu/0".into(),sensor_id:"/cpu/0/clock/1".into()});
        assert!(validate(&options,"cpu_clock",false).is_ok());assert!(validate(&options,"gpu_voltage",false).is_err());assert!(validate(&options,"fps",false).is_err());
        options.font_size=Some(100);assert!(validate(&options,"cpu_clock",false).is_err());
    }
}
