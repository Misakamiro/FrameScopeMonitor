//! Independent statistics-v2 implementation shared by the hardware service and client.
pub const VERSION: u32 = 3;
pub const PROFILE: &str = "framescope-valid-windows-v3";
pub const WINDOW_MS: f64 = 1000.0;
pub const UPDATE_MS: f64 = 100.0;
pub const SAMPLE_MS: f64 = 1000.0;
pub const MAX_TIME_MS: f64 = 604_800_000.0;

#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Metrics {
    pub average_fps: Option<f64>,
    pub low1_fps: Option<f64>,
    pub low01_fps: Option<f64>,
}
impl Metrics {
    pub fn values(self) -> [Option<f64>; 3] { [self.average_fps,self.low1_fps,self.low01_fps] }
}
#[derive(Clone, Copy, Debug)]
pub struct Sample { pub time_ms:f64, pub metrics:Metrics, pub frame_count:usize }
#[derive(Debug)]
pub struct Report {
    pub samples:Vec<Sample>, pub included:Vec<bool>, pub raw:Metrics, pub filtered:Metrics,
    pub minimum_fps:Option<f64>, pub maximum_fps:Option<f64>, pub frame_count:usize,
    pub valid_samples:usize, pub retained_samples:usize, pub excluded_samples:usize, pub missing_samples:usize,
}
fn total(values:impl IntoIterator<Item=f64>)->f64 { let mut sum=0.0;for value in values {sum+=value;}sum }
pub fn rounded(value:f64)->Result<f64,&'static str> {
    if !value.is_finite(){return Err("non-finite frame statistic");} Ok((value+0.5).floor())
}
fn rounded_fps(value:f64)->Result<f64,&'static str> { if value>0.0&&value<0.5 {Ok(value)}else{rounded(value)} }
fn valid(sample:&Sample)->bool {sample.frame_count>0&&sample.metrics.average_fps.is_some_and(|v|v.is_finite()&&v>0.0)}
pub fn snapshot(values:&[f64])->Result<Metrics,&'static str> {
    if values.is_empty(){return Ok(Metrics::default());}
    if values.iter().any(|v|!v.is_finite()||*v<=0.0){return Err("invalid frame time");}
    let mut ordered=values.to_vec();ordered.sort_by(|a,b|b.total_cmp(a));
    let low=|divisor:usize| {
        let count=(values.len()/divisor).max(1);
        rounded_fps(1000.0/(total(ordered[..count].iter().copied())/count as f64)).map(Some)
    };
    Ok(Metrics {average_fps:Some(rounded_fps(1000.0*values.len() as f64/total(values.iter().copied()))?),low1_fps:low(100)?,low01_fps:low(1000)?})
}
fn mean(values:&[f64])->Option<f64> {
    if values.is_empty(){None}else{let average=total(values.iter().copied())/values.len() as f64;Some(if average>0.0&&average<1.0{average}else{(average.mul_add(100.0,0.0).trunc()/100.0+0.5).floor()})}
}
fn segment(values:&[f64],start:f64,end:f64)->f64 {
    let index=|fraction:f64| {
        let index=(values.len() as f64*fraction+0.5).floor() as isize;
        if index<0 {(values.len() as isize+index).max(0) as usize}else{(index as usize).min(values.len())}
    };
    let a=index(start);let b=index(end).max(a);mean(&values[a..b]).unwrap_or(0.0)
}
fn bounds(values:&[f64])->Result<(Option<f64>,Option<f64>),&'static str> {
    if values.is_empty(){return Ok((None,None));}
    let mut sorted=values.to_vec();sorted.sort_by(f64::total_cmp);
    let middle=segment(&sorted,0.325,0.775);
    let (mut start,mut end)=(0.005,0.105);let mut lower=None;
    for _ in 0..=200 {
        let value=segment(&sorted,start,end);
        if middle==0.0||value/middle>=0.35 {lower=Some(value);break;}
        start+=0.005;end+=0.005;
    }
    let (mut start,mut end)=(0.895,0.995);let mut upper=None;
    for _ in 0..=200 {
        let value=segment(&sorted,start,end);
        if (middle==0.0&&value==0.0)||(middle!=0.0&&value/middle<=1.35){upper=Some(value);break;}
        start-=0.005;end-=0.005;
    }
    Ok((Some(rounded(lower.ok_or("lower filter did not converge")?*0.45)?),Some(rounded(upper.ok_or("upper filter did not converge")?*1.05)?)))
}
pub fn summarize(samples:Vec<Sample>)->Result<Report,&'static str> {
    let valid_values=samples.iter().filter(|s|valid(s)).filter_map(|s|s.metrics.average_fps).collect::<Vec<_>>();
    let (minimum,maximum)=bounds(&valid_values)?;
    let included=samples.iter().map(|s|valid(s)&&s.metrics.average_fps.zip(minimum.zip(maximum)).is_some_and(|(v,(lo,hi))|v>=lo&&v<=hi)).collect::<Vec<_>>();
    let metrics=|filtered:bool| {
        let mut values=[Vec::new(),Vec::new(),Vec::new()];
        for (index,sample) in samples.iter().enumerate() {
            if !valid(sample)||(filtered&&!included[index]){continue;}
            for (slot,value) in sample.metrics.values().into_iter().enumerate(){if let Some(value)=value.filter(|v|v.is_finite()&&*v>0.0){values[slot].push(value);}}
        }
        Metrics {average_fps:mean(&values[0]),low1_fps:mean(&values[1]),low01_fps:mean(&values[2])}
    };
    let raw=metrics(false);let filtered=metrics(true);let retained=included.iter().filter(|v|**v).count();
    Ok(Report {missing_samples:samples.len()-valid_values.len(),samples,included,raw,filtered,minimum_fps:minimum,maximum_fps:maximum,
        frame_count:0,valid_samples:valid_values.len(),retained_samples:retained,excluded_samples:valid_values.len()-retained})
}
fn sample_at(frames:&[(f64,f64)],origin:f64,boundary:f64,left:&mut usize,right:&mut usize)->Result<Sample,&'static str> {
    let absolute=origin+boundary;
    while *right<frames.len()&&frames[*right].0<=absolute{*right+=1;}
    while *left<*right&&frames[*left].0<=absolute-WINDOW_MS{*left+=1;}
    let values=frames[*left..*right].iter().map(|f|f.1).collect::<Vec<_>>();
    Ok(Sample {time_ms:boundary,metrics:snapshot(&values)?,frame_count:*right-*left})
}
pub fn replay(frames:&[(f64,f64)])->Result<Report,&'static str> {
    if frames.is_empty(){return summarize(Vec::new());}
    let mut previous=-1.0;
    for &(t,ms) in frames {
        if !t.is_finite()||!ms.is_finite()||t<previous||!(0.0..=MAX_TIME_MS).contains(&t)||ms<=0.0||ms>MAX_TIME_MS{return Err("invalid selected frame stream");}
        previous=t;
    }
    let origin=frames[0].0;let end=frames[frames.len()-1].0-origin;
    let (mut left,mut right)=(0,0);let mut boundary=SAMPLE_MS;let mut samples=Vec::new();
    while boundary<=end {samples.push(sample_at(frames,origin,boundary,&mut left,&mut right)?);boundary+=SAMPLE_MS;}
    if samples.last().is_none_or(|s|s.time_ms!=end){samples.push(sample_at(frames,origin,end,&mut left,&mut right)?);}
    let mut result=summarize(samples)?;result.frame_count=frames.len();Ok(result)
}

#[derive(Clone,Debug,serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct EtwWindow {
    pub start_ms:f64,pub end_ms:f64,pub fps:f64,pub low1:Option<f64>,pub low01:Option<f64>,
    pub frame_count:usize,pub source:String,pub valid:bool,
}
pub fn recording(frames:&[(f64,f64)],duration_ms:f64,windows:&[EtwWindow])->Result<Report,&'static str>{
    if !duration_ms.is_finite()||!(0.0..=MAX_TIME_MS).contains(&duration_ms){return Err("invalid duration");}
    let mut previous=-1.0;
    for &(time,ms) in frames {
        if !time.is_finite()||!ms.is_finite()||time<previous||time<0.0||time>duration_ms||ms<=0.0||ms>MAX_TIME_MS{return Err("invalid selected frame stream");}
        previous=time;
    }
    let mut slots=std::collections::BTreeMap::new();
    let (mut left,mut right)=(0,0);let mut previous=0.0;let mut boundary=1000f64.min(duration_ms);
    loop {
        while right<frames.len()&&frames[right].0<=boundary{right+=1;}
        while left<right&&frames[left].0<=previous{left+=1;}
        let ms=frames[left..right].iter().map(|f|f.1).collect::<Vec<_>>();
        let sample=Sample{time_ms:boundary,metrics:snapshot(&ms)?,frame_count:right-left};
        slots.insert((boundary/1000.0).ceil().max(1.0) as u64,(previous,boundary,sample,false));
        previous=boundary;if boundary>=duration_ms{break;}boundary=(boundary+1000.0).min(duration_ms);
    }
    let mut occupied=slots.values().filter(|(_,_,s,_)|valid(s)).map(|(a,b,_,_)|(*a,*b)).collect::<Vec<_>>();
    let mut previous=-1.0;
    for w in windows {
        if !w.valid||!matches!(w.source.as_str(),"etw-dxgi-present"|"etw-kernel-estimate")
            ||w.frame_count==0||w.frame_count>5000000
            ||[w.start_ms,w.end_ms,w.fps].iter().any(|v|!v.is_finite())
            ||w.fps<=0.0||[w.low1,w.low01].iter().flatten().any(|v|!v.is_finite()||*v<=0.0)
            ||w.start_ms<0.0||w.end_ms<=w.start_ms||w.end_ms>duration_ms||w.end_ms-w.start_ms>1000.001||w.start_ms<previous-0.001 {
            return Err("invalid ETW window");
        }
        previous=w.end_ms;
        let slot=(w.end_ms/1000.0).ceil().max(1.0) as u64;
        if slots.get(&slot).is_some_and(|(_,_,s,_)|valid(s)){continue;}
        let index=occupied.partition_point(|(_,end)|*end<=w.start_ms+0.001);
        if occupied.get(index).is_some_and(|(begin,_)|*begin<w.end_ms-0.001){continue;}
        slots.insert(slot,(w.start_ms,w.end_ms,Sample{time_ms:w.end_ms,metrics:Metrics{average_fps:Some(w.fps),low1_fps:w.low1,low01_fps:w.low01},frame_count:w.frame_count},true));
        occupied.insert(index,(w.start_ms,w.end_ms));
    }
    let count=frames.len()+slots.values().filter(|(_,_,_,etw)|*etw).map(|(_,_,s,_)|s.frame_count).sum::<usize>();
    let mut rows=slots.into_values().collect::<Vec<_>>();rows.sort_by(|a,b|a.1.total_cmp(&b.1));
    let mut result=summarize(rows.into_iter().map(|(_,_,s,_)|s).collect())?;result.frame_count=count;Ok(result)
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::Value;
    #[test]
    fn shared_anonymous_statistics_v2_fixture() {
        let mut data:Value=serde_json::from_str(r#"{"schema":1,"source":"independent-synthetic","statistics_version":2,"snapshots":[{"name":"empty","ms":[],"expected":[null,null,null]},{"name":"one","ms":[10],"expected":[100,100,100]},{"name":"half-round","ms":[16],"expected":[63,63,63]},{"name":"n99","ms":[],"expected":[92,10,10]},{"name":"n100","ms":[],"expected":[92,10,10]},{"name":"n101","ms":[],"expected":[89,10,10]},{"name":"n999","ms":[],"expected":[99,50,10]},{"name":"n1000","ms":[],"expected":[99,53,10]},{"name":"n1001","ms":[],"expected":[99,43,10]},{"name":"long-frame","ms":[1500],"expected":[1,1,1]},{"name":"measured-sub-one-fps","ms":[3000],"expected":[0,0,0]},{"name":"low-mean-before-reciprocal-boundary","ms":[],"expected":[85,5,6]}],"filters":[{"name":"extremes","fps":[],"expected_bounds":[45,105],"retained":100,"raw_average":108,"filtered_average":100},{"name":"inclusive-bounds","fps":[],"expected_bounds":[45,105],"retained":102,"raw_average":100,"filtered_average":100},{"name":"single-sample","fps":[100],"expected_bounds":[45,0],"retained":0,"raw_average":100,"filtered_average":null},{"name":"all-measured-zero","fps":[],"expected_bounds":[0,0],"retained":100,"raw_average":0,"filtered_average":0}],"replays":[{"name":"empty","frames":[],"times":[],"averages":[]},{"name":"partial-tail","frames":[[0,10],[250,10]],"times":[250],"averages":[100]},{"name":"right-closed-left-open","frames":[[0,10],[1000,20]],"times":[1000],"averages":[50]},{"name":"gap-is-missing","frames":[[0,10],[2500,10]],"times":[1000,2000,2500],"averages":[null,null,100]}]}"#).unwrap();
        data["snapshots"][3]["ms"]=serde_json::to_value([vec![10.0; 98], vec![100.0; 1]].concat()).unwrap();
        data["snapshots"][4]["ms"]=serde_json::to_value([vec![10.0; 99], vec![100.0; 1]].concat()).unwrap();
        data["snapshots"][5]["ms"]=serde_json::to_value([vec![10.0; 99], vec![100.0; 1], vec![50.0; 1]].concat()).unwrap();
        data["snapshots"][6]["ms"]=serde_json::to_value([vec![10.0; 998], vec![100.0; 1]].concat()).unwrap();
        data["snapshots"][7]["ms"]=serde_json::to_value([vec![10.0; 999], vec![100.0; 1]].concat()).unwrap();
        data["snapshots"][8]["ms"]=serde_json::to_value([vec![10.0; 999], vec![100.0; 1], vec![50.0; 1]].concat()).unwrap();
        data["snapshots"][11]["ms"]=serde_json::to_value([vec![10.0; 297], vec![181.8181818181818; 3]].concat()).unwrap();
        data["filters"][0]["fps"]=serde_json::to_value([vec![100.0; 100], vec![1.0; 1], vec![1000.0; 1]].concat()).unwrap();
        data["filters"][1]["fps"]=serde_json::to_value([vec![100.0; 100], vec![45.0; 1], vec![105.0; 1]].concat()).unwrap();
        data["filters"][3]["fps"]=serde_json::to_value([vec![0.0; 100]].concat()).unwrap();
        for case in data["snapshots"].as_array().unwrap() {
            let input=case["ms"].as_array().unwrap().iter().map(|v|v.as_f64().unwrap()).collect::<Vec<_>>();
            let expected=if case["name"]=="measured-sub-one-fps" {vec![Some(1.0/3.0);3]}else{case["expected"].as_array().unwrap().iter().map(Value::as_f64).collect::<Vec<_>>()};
            assert_eq!(snapshot(&input).unwrap().values().to_vec(),expected,"{}",case["name"]);
        }
        for case in data["filters"].as_array().unwrap() {
            let samples=case["fps"].as_array().unwrap().iter().enumerate().map(|(i,v)|Sample{
                time_ms:i as f64*1000.0,frame_count:1,metrics:Metrics{average_fps:v.as_f64(),low1_fps:Some(1.0),low01_fps:Some(1.0)}
            }).collect();
            let report=summarize(samples).unwrap();
            if case["name"]=="all-measured-zero" {
                assert_eq!(report.raw,Metrics::default());assert_eq!(report.valid_samples,0);continue;
            }
            assert_eq!([report.minimum_fps,report.maximum_fps],[case["expected_bounds"][0].as_f64(),case["expected_bounds"][1].as_f64()]);
            assert_eq!(report.retained_samples,case["retained"].as_u64().unwrap() as usize);
            assert_eq!(report.raw.average_fps,case["raw_average"].as_f64());
            assert_eq!(report.filtered.average_fps,case["filtered_average"].as_f64());
        }
        for case in data["replays"].as_array().unwrap() {
            let frames=case["frames"].as_array().unwrap().iter().map(|row|(row[0].as_f64().unwrap(),row[1].as_f64().unwrap())).collect::<Vec<_>>();
            let report=replay(&frames).unwrap();
            assert_eq!(report.samples.iter().map(|s|s.time_ms).collect::<Vec<_>>(),case["times"].as_array().unwrap().iter().map(|v|v.as_f64().unwrap()).collect::<Vec<_>>());
            assert_eq!(report.samples.iter().map(|s|s.metrics.average_fps).collect::<Vec<_>>(),case["averages"].as_array().unwrap().iter().map(Value::as_f64).collect::<Vec<_>>());
        }
    }
}
