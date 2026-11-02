use super::*;
use std::{collections::HashMap,time::{Instant,SystemTime,UNIX_EPOCH}};
use tokio::{sync::mpsc,task::JoinSet};

const SAMPLE_BYTES:u64=1024*1024;
const CHUNK_BYTES:u64=4*1024*1024;
const PARALLEL_MINIMUM:u64=2*CHUNK_BYTES;
const RANGE_UNAVAILABLE:&str="range-unavailable";

#[derive(Clone)]
pub(super) struct AssetProbe {pub proxy:bool,range:bool,rate:f64,estimate:f64}

pub(super) fn range_matches(value:&str,start:u64,end:u64,total:u64)->bool {
    let Some(value)=value.trim().strip_prefix("bytes ")else{return false;};
    let Some((range,size))=value.split_once('/')else{return false;};
    let Some((first,last))=range.split_once('-')else{return false;};
    first.parse::<u64>()==Ok(start)&&last.parse::<u64>()==Ok(end)&&size.parse::<u64>()==Ok(total)
}

async fn probe(client:&reqwest::Client,url:String,total:u64,proxy:bool,observer:&impl Fn(DownloadTelemetry))->Result<AssetProbe>{
    let limit=total.min(SAMPLE_BYTES);let started=Instant::now();
    let mut response=client.get(url).header(reqwest::header::RANGE,format!("bytes=0-{}",limit-1))
        .timeout(Duration::from_secs(6)).send().await.and_then(|r|r.error_for_status()).map_err(request_error)?;
    let first=Instant::now();let range=response.status()==reqwest::StatusCode::PARTIAL_CONTENT;
    if range&&!response.headers().get(reqwest::header::CONTENT_RANGE).and_then(|v|v.to_str().ok()).is_some_and(|v|range_matches(v,0,limit-1,total)){return Err(RANGE_UNAVAILABLE.into());}
    if response.content_length().is_some_and(|size|size!=if range{limit}else{total}){return Err("下载探测长度与清单不符".into());}
    let mut received=0;while received<limit{let Some(bytes)=response.chunk().await.map_err(request_error)?else{break;};received+=(bytes.len() as u64).min(limit-received);}
    if received!=limit{return Err("下载探测样本不完整".into());}
    let body_rate=received as f64/first.elapsed().as_secs_f64().max(0.000001);
    let ttfb=first.duration_since(started);let mut event=DownloadTelemetry::event("sample",proxy,body_rate as u64);
    event.ttfb_ms=Some(ttfb.as_millis() as u64);event.sample_bytes=Some(received);event.range_supported=Some(range);observer(event);
    Ok(AssetProbe{proxy,range,rate:body_rate,estimate:ttfb.as_secs_f64()+total as f64/body_rate.max(1.0)})
}

impl Source {
    pub(super) async fn asset_probes(&self,client:&reqwest::Client,original:&str,total:u64,cancel:&AtomicBool,observer:&impl Fn(DownloadTelemetry))->Result<Vec<AssetProbe>>{
        cancelled(cancel)?;
        if self.proxy.is_none(){return Ok(vec![AssetProbe{proxy:false,range:false,rate:0.0,estimate:0.0}]);}
        let preferred=!self.origin_first.load(Ordering::Relaxed);
        let probes=async{
            let (a,b)=tokio::join!(probe(client,self.url(original,preferred),total,preferred,observer),probe(client,self.url(original,!preferred),total,!preferred,observer));
            let mut values=vec![(preferred,a),(!preferred,b)].into_iter().map(|(proxy,result)|result.unwrap_or_else(|_|{let mut event=DownloadTelemetry::event("sample",proxy,0);event.reason=Some("probe-failed");observer(event);AssetProbe{proxy,range:false,rate:0.0,estimate:f64::INFINITY}})).collect::<Vec<_>>();
            values.sort_by(|a,b|a.estimate.total_cmp(&b.estimate));Ok(values)
        };
        tokio::select!{result=probes=>result,_=super::download::cancellation(cancel)=>Err("下载已取消".into())}
    }

    pub(super) fn download_asset(&self,original:&str,path:&Path,package:&Package,cancel:&AtomicBool,progress:&impl Fn(u64,u64),observer:&impl Fn(DownloadTelemetry))->Result<()> {
        let runtime=tokio::runtime::Builder::new_current_thread().enable_all().build().map_err(|e|e.to_string())?;
        let client=super::download::client()?;let mut probes=runtime.block_on(self.asset_probes(&client,original,package.size,cancel,observer))?;
        let mut current=probes[0].proxy;let mut switches=0;let mut serial_only=false;let mut allow_slow=false;let mut errors=Vec::new();
        loop {
            cancelled(cancel)?;progress(0,package.size);observer(DownloadTelemetry::event("route",current,0));
            let range=probes.iter().any(|p|p.proxy==current&&p.range);
            let rate=std::cell::Cell::new(0u64);let observed=|event:DownloadTelemetry|{if event.event=="progress"{rate.set(event.bytes_per_second);}observer(event);};
            let mut result=if package.size>=PARALLEL_MINIMUM&&self.proxy.is_some()&&range&&!serial_only {
                runtime.block_on(self.segmented(&client,original,path,package.size,cancel,progress,observer,&mut probes,&mut current,&mut switches))
            }else{
                let alternate=probes.iter().find(|p|p.proxy!=current);
                let threshold=alternate.map_or(super::download::MIN_BYTES_PER_SECOND,|p|(p.rate*0.5) as u64).max(super::download::MIN_BYTES_PER_SECOND);
                self.transfer_observed(&self.url(original,current),path,package.size,cancel,progress,alternate.is_some()&&switches<2&&!allow_slow,threshold,current,&observed)
            };
            if result.as_ref().is_err_and(|e|e==RANGE_UNAVAILABLE){serial_only=true;let mut event=DownloadTelemetry::event("fallback",current,0);event.reason=Some("range-unavailable-use-single-stream");observer(event);continue;}
            if result.is_ok(){result=verify_file(path,package);}
            if result.as_ref().is_err_and(|e|e.contains("持续低速"))&&self.proxy.is_some()&&switches<2 {
                let refreshed=runtime.block_on(async{tokio::select!{p=probe(&client,self.url(original,!current),package.size,!current,observer)=>p,_=super::download::cancellation(cancel)=>Err("下载已取消".into())}});
                cancelled(cancel)?;
                if !refreshed.as_ref().is_ok_and(|p|p.rate>rate.get() as f64*1.25){allow_slow=true;let mut event=DownloadTelemetry::event("fallback",current,rate.get());event.reason=Some("no-faster-route-continue");observer(event);continue;}
                if let Ok(value)=refreshed{if let Some(old)=probes.iter_mut().find(|p|p.proxy==value.proxy){*old=value;}}
            }
            match result {
                Ok(())=>{self.origin_first.store(!current,Ordering::Relaxed);return Ok(());}
                Err(error)=>{
                    cancelled(cancel)?;errors.push(error);
                    if self.proxy.is_none()||switches>=2{return Err(errors.join("\n"));}
                    current=!current;switches+=1;allow_slow=false;let mut event=DownloadTelemetry::event("switch",current,0);event.reason=Some("transport-or-integrity-failure");observer(event);
                }
            }
        }
    }

    async fn segmented(&self,client:&reqwest::Client,original:&str,path:&Path,total:u64,cancel:&AtomicBool,progress:&impl Fn(u64,u64),observer:&impl Fn(DownloadTelemetry),probes:&mut [AssetProbe],current:&mut bool,switches:&mut u8)->Result<()> {
        let count=total.div_ceil(CHUNK_BYTES) as usize;let store=Parts::new(path,count)?;let mut complete=vec![false;count];let mut committed=0u64;
        let mut jobs:JoinSet<std::result::Result<(usize,Vec<u8>),ChunkError>>=JoinSet::new();let (sender,mut receiver)=mpsc::unbounded_channel();let mut inflight=HashMap::<usize,u64>::new();let mut generation=0u32;
        let started=Instant::now();let mut tick=tokio::time::interval(Duration::from_millis(100));let mut window=Instant::now();let mut window_bytes=0;let mut sample_stamp=Instant::now();let mut sample_bytes=0;let mut reprobes=0;
        loop {
            if cancel.load(Ordering::SeqCst)||started.elapsed()>Duration::from_secs(1800){stop_jobs(&mut jobs).await;return Err(if cancel.load(Ordering::SeqCst){"下载已取消"}else{"安装包下载超时"}.into());}
            if complete.iter().all(|value|*value){break;}
            while jobs.len()<4 {
                let Some(index)=(0..count).find(|i|!complete[*i]&&!inflight.contains_key(i))else{break;};
                let start=index as u64*CHUNK_BYTES;let end=start.saturating_add(CHUNK_BYTES-1).min(total-1);inflight.insert(index,0);
                jobs.spawn(chunk(client.clone(),self.url(original,*current),index,start,end,total,sender.clone(),generation));
                let mut event=DownloadTelemetry::event("parallelism",*current,0);event.active_requests=Some(jobs.len());observer(event);
            }
            tokio::select!{
                Some((epoch,index,size))=receiver.recv()=>{if epoch==generation&&!complete[index]&&inflight.contains_key(&index){inflight.insert(index,size);}},
                joined=jobs.join_next(),if !jobs.is_empty()=>{
                    match joined {
                        Some(Ok(Ok((index,data))))=>{if let Err(e)=fs::write(&store.files[index],&data){stop_jobs(&mut jobs).await;return Err(e.to_string());}complete[index]=true;committed+=data.len() as u64;inflight.remove(&index);}
                        Some(Ok(Err(ChunkError::Range)))=>{stop_jobs(&mut jobs).await;return Err(RANGE_UNAVAILABLE.into());}
                        other=>{
                            stop_jobs(&mut jobs).await;inflight.clear();generation+=1;
                            if self.proxy.is_none()||*switches>=2{return Err(match other{Some(Ok(Err(ChunkError::Transport(e))))=>e,_=>"下载分段任务失败".into()});}
                            *current=!*current;*switches+=1;let mut event=DownloadTelemetry::event("switch",*current,0);event.reason=Some("segment-transport-failure");observer(event);
                            if !probes.iter().any(|p|p.proxy==*current&&p.range){return Err(RANGE_UNAVAILABLE.into());}
                            window=Instant::now();window_bytes=committed;sample_stamp=Instant::now();sample_bytes=committed;
                        }
                    }
                },
                _=tick.tick()=>{
                    let unique=committed+inflight.values().sum::<u64>();progress(unique,total);
                    if sample_stamp.elapsed()>=Duration::from_millis(250){let rate=(unique.saturating_sub(sample_bytes) as f64/sample_stamp.elapsed().as_secs_f64()) as u64;observer(DownloadTelemetry::event("progress",*current,rate));sample_stamp=Instant::now();sample_bytes=unique;}
                    if window.elapsed()>=super::download::SPEED_WINDOW {
                        let rate=unique.saturating_sub(window_bytes) as f64/window.elapsed().as_secs_f64();let alternate=probes.iter().find(|p|p.proxy!=*current).cloned();
                        let slow=rate<super::download::MIN_BYTES_PER_SECOND as f64||alternate.as_ref().is_some_and(|p|rate<p.rate*0.5);
                        if slow&&reprobes<2&&*switches<2 {
                            if let Some(alternate)=alternate {
                                stop_jobs(&mut jobs).await;inflight.clear();generation+=1;reprobes+=1;
                                let refreshed=tokio::select!{p=probe(client,self.url(original,alternate.proxy),total,alternate.proxy,observer)=>p,_=super::download::cancellation(cancel)=>return Err("下载已取消".into())};
                                if let Ok(value)=refreshed {
                                    if value.rate>rate*1.25 {
                                        *current=value.proxy;*switches+=1;let mut event=DownloadTelemetry::event("switch",*current,value.rate as u64);event.reason=Some("faster-route-after-slow-window");observer(event);
                                        if !value.range{return Err(RANGE_UNAVAILABLE.into());}
                                    }
                                    if let Some(old)=probes.iter_mut().find(|p|p.proxy==value.proxy){*old=value;}
                                }
                                sample_stamp=Instant::now();sample_bytes=committed;
                            }
                        }
                        window=Instant::now();window_bytes=committed+inflight.values().sum::<u64>();
                    }
                }
            }
        }
        stop_jobs(&mut jobs).await;let mut file=fs::File::create(path).map_err(|e|e.to_string())?;
        for part in &store.files{cancelled(cancel)?;std::io::copy(&mut fs::File::open(part).map_err(|e|e.to_string())?,&mut file).map_err(|e|e.to_string())?;}
        file.sync_all().map_err(|e|e.to_string())?;progress(total,total);Ok(())
    }
}

enum ChunkError {Range,Transport(String)}
async fn chunk(client:reqwest::Client,url:String,index:usize,start:u64,end:u64,total:u64,sender:mpsc::UnboundedSender<(u32,usize,u64)>,generation:u32)->std::result::Result<(usize,Vec<u8>),ChunkError>{
    let mut response=client.get(url).header(reqwest::header::RANGE,format!("bytes={start}-{end}")).send().await.map_err(|e|ChunkError::Transport(request_error(e)))?;
    if response.status()!=reqwest::StatusCode::PARTIAL_CONTENT{return if response.status().is_success()||response.status()==reqwest::StatusCode::RANGE_NOT_SATISFIABLE{Err(ChunkError::Range)}else{Err(ChunkError::Transport(format!("下载分段HTTP状态 {}",response.status()))) };}
    if !response.headers().get(reqwest::header::CONTENT_RANGE).and_then(|v|v.to_str().ok()).is_some_and(|v|range_matches(v,start,end,total))||response.content_length().is_some_and(|n|n!=end-start+1){return Err(ChunkError::Range);}
    let mut bytes=Vec::with_capacity((end-start+1) as usize);
    while let Some(data)=response.chunk().await.map_err(|e|ChunkError::Transport(request_error(e)))?{if bytes.len() as u64+data.len() as u64>end-start+1{return Err(ChunkError::Range);}bytes.extend_from_slice(&data);let _=sender.send((generation,index,bytes.len() as u64));}
    if bytes.len() as u64!=end-start+1{return Err(ChunkError::Range);}Ok((index,bytes))
}
async fn stop_jobs(jobs:&mut JoinSet<std::result::Result<(usize,Vec<u8>),ChunkError>>){jobs.abort_all();while jobs.join_next().await.is_some(){}}

struct Parts{directory:PathBuf,files:Vec<PathBuf>}
impl Parts {
    fn new(path:&Path,count:usize)->Result<Self>{
        let name=path.file_name().ok_or("缺少下载缓存文件名")?.to_string_lossy();let nonce=SystemTime::now().duration_since(UNIX_EPOCH).map_err(|e|e.to_string())?.as_nanos();
        let directory=path.with_file_name(format!("{name}.segments-{}-{nonce}",std::process::id()));fs::create_dir(&directory).map_err(|e|e.to_string())?;
        let files=(0..count).map(|i|directory.join(format!("{i:08}.part"))).collect();Ok(Self{directory,files})
    }
}
impl Drop for Parts{fn drop(&mut self){for file in &self.files{let _=fs::remove_file(file);}let _=fs::remove_dir(&self.directory);}}
