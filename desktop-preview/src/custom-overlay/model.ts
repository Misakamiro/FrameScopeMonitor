import {OVERLAY_METRICS,type SensorBinding,type OverlaySensor} from '@/lib/overlay-items';
export type Kind='value'|'text'|'progress'|'trend';
export interface ComponentStyle {font:string;size:number;weight:number;color:string;align:'left'|'center'|'right';background:string;border_color:string;border_width:number;radius:number;italic:boolean;decoration:'none'|'underline'|'line-through';opacity:number}
export interface Component {id:string;kind:Kind;name:string;text:string;metric:string;binding:SensorBinding|null;x:number;y:number;width:number;height:number;minimum:number;maximum:number;style:ComponentStyle}
export interface Presentation {show_name:boolean;show_value:boolean;show_unit:boolean;decimals:number;duration:number;line_width:number;fill:boolean;auto_range:boolean;direction:'right'|'left'|'up'|'down'}
export interface Component {presentation?:Presentation}
export interface Layout {monitor:string;design_width:number;design_height:number;components:Component[]}
export interface SavedLayout {id:string;name:string;revision:number;layout:Layout}
export interface LayoutStore {schema:2;revision:number;draft:Layout;applied:Layout|null;templates:Component[];layouts:SavedLayout[];editing_id:string|null;applied_id:string|null;applied_revision:number|null}
export interface Display {id:string;name:string;x:number;y:number;width:number;height:number;scale:number}
export const metrics:Record<string,{label:string;unit:string}>={fps:{label:'FPS',unit:'FPS'},fps_1low:{label:'1% Low',unit:'FPS'},fps_01low:{label:'0.1% Low',unit:'FPS'},...OVERLAY_METRICS,game_ping:{label:'游戏延迟',unit:'ms'},time:{label:'时间',unit:''},delta_password:{label:'三角洲密码',unit:''},cpu_core_clocks:{label:'CPU 分核频率',unit:'MHz'},cpu_core_voltages:{label:'CPU 分核电压',unit:'V'}};
export const clone=<T,>(value:T):T=>JSON.parse(JSON.stringify(value));
export function presentation(item:Component):Presentation{return {show_name:true,show_value:true,show_unit:true,decimals:metrics[item.metric]?.unit==='V'?3:['RPM','MHz','FPS'].includes(metrics[item.metric]?.unit)?0:1,duration:60,line_width:2,fill:false,auto_range:false,direction:'right',...item.presentation};}
export function createComponent(kind:Kind='value',metric='fps'):Component{return {id:crypto.randomUUID(),kind,name:kind==='text'?'自定义文字':metrics[metric]?.label||'组件',text:'自定义文字',metric,binding:null,x:0,y:0,width:260,height:kind==='trend'?120:72,minimum:0,maximum:metric.startsWith('fps')?240:100,style:{font:'Microsoft YaHei',size:24,weight:500,color:'#ffffff',align:'left',background:'#11182799',border_color:'#ffffff33',border_width:1,radius:8,italic:false,decoration:'none',opacity:1}};}
export function instance(template:Component):Component{return {...clone(template),id:crypto.randomUUID()};}
export function geometry(item:Component,layout:Layout,width:number,height:number){const scale=Math.min(width/layout.design_width,height/layout.design_height);return {x:item.x*layout.design_width*scale,y:item.y*layout.design_height*scale,width:item.width*scale,height:item.height*scale,scale};}
export function snap(item:Component,others:Component[],layout:Layout,x:number,y:number,threshold=8){
  const xs=[0,layout.design_width/2,layout.design_width],ys=[0,layout.design_height/2,layout.design_height];
  for(const other of others){if(other.id===item.id)continue;const px=other.x*layout.design_width,py=other.y*layout.design_height;xs.push(px,px+other.width/2,px+other.width);ys.push(py,py+other.height/2,py+other.height);}
  function nearest(value:number,size:number,guides:number[]){let best=threshold+1,target:number|undefined,delta=0;for(const offset of [0,size/2,size])for(const guide of guides){const diff=guide-value-offset;if(Math.abs(diff)<best){best=Math.abs(diff);delta=diff;target=guide;}}return {value:best<=threshold?value+delta:value,guide:best<=threshold?target:undefined};}
  const a=nearest(x,item.width,xs),b=nearest(y,item.height,ys);
  return {x:Math.max(0,Math.min(layout.design_width-item.width,a.value))/layout.design_width,y:Math.max(0,Math.min(layout.design_height-item.height,b.value))/layout.design_height,vertical:a.guide,horizontal:b.guide};
}
export const isFrame=(metric:string)=>['fps','fps_1low','fps_01low'].includes(metric);
export const isNumeric=(metric:string)=>!['time','delta_password','cpu_core_clocks','cpu_core_voltages'].includes(metric);
type Status='ready'|'waiting-target'|'waiting-frames'|'sensor-offline'|'stale';
export interface Reading {text:string;value:number|null;status:Status;at:number;key:string;source:string}
export function sensorValue(item:Component,data:Record<string,unknown>,now=Date.now()):Reading{
  const format=presentation(item),unit=format.show_unit?metrics[item.metric]?.unit||'':'';
  let at=Number(data.hardware_timestamp_ms??data.timestamp_ms??0),source='hardware',key=item.metric+':'+JSON.stringify(item.binding)+':'+(item.metric.startsWith('gpu_')&&!item.binding?data.active_gpu_index:''),raw:unknown;
  const result=(text:string,value:number|null,status:Status):Reading=>({text,value,status,at,key,source});
  const missing=(status:Exclude<Status,'ready'>)=>result({'waiting-target':'等待游戏目标','waiting-frames':'等待帧数据','sensor-offline':'传感器离线',stale:'数据已过期'}[status],null,status);
  if(item.kind==='text')return result(item.text,null,'ready');
  if(item.metric==='time')return result(new Date(now+Number(data.net_time_offset_ms||0)).toLocaleTimeString(),null,'ready');
  if(isFrame(item.metric)){
    source=String(data.fps_source||'');const frames=data.frame_snapshot as Record<string,unknown>|undefined;
    key=[item.metric,data.target_pid,data.target_start_ms,data.target_generation,source,frames?.target_generation].join(':');
    if(!data.target_pid)return missing('waiting-target');
    if(!frames||frames.valid===false||frames.target_pid!==data.target_pid||frames.target_start_ms!==data.target_start_ms)return missing('waiting-frames');
    if(source.startsWith('etw')&&frames.target_generation!==data.target_generation)return missing('waiting-frames');
    at=Number(frames.window_end_ms||0);raw=data[item.metric];
    const until=Math.min(at+2000,Number(frames.valid_until_ms??at+2000));
    if(!at||now>=until||at>now+1000)return missing('stale');
    if(typeof raw!=='number'||raw<=0)return missing('waiting-frames');
  }else{
    if(item.binding){const sensors=(data.sensor_catalog||[]) as OverlaySensor[];const sensor=sensors.find(row=>row.category===item.binding!.category&&row.device_id===item.binding!.device_id&&row.sensor_id===item.binding!.sensor_id&&row.available);raw=sensor?.value;}
    else raw=data[item.metric==='gpu_vram'?'gpu_vram_used':item.metric];
    if(raw===undefined||raw===null)return missing('sensor-offline');
    if(!at||now-at>=2000||at>now+1000)return missing('stale');
  }
  if(Array.isArray(raw))return result(raw.map((v,index)=>`${index+1}: ${typeof v==='number'?v.toFixed(format.decimals):'—'}`).join(' / '),null,'ready');
  if(typeof raw==='string')return result(raw,null,'ready');
  if(typeof raw!=='number'||!Number.isFinite(raw))return missing('sensor-offline');
  return result(raw.toFixed(format.decimals)+(unit?' '+unit:''),raw,'ready');
}
export interface History {key:string;points:{at:number;value:number|null}[]}
export function appendHistory(history:History|undefined,reading:Reading,seconds:number):History{
  const points=history?.key===reading.key?history.points:[];
  if(reading.status!=='ready')return {key:reading.key,points:[]};
  if(points.at(-1)?.at===reading.at)return {key:reading.key,points};
  return {key:reading.key,points:[...points.filter(p=>p.at>reading.at-seconds*1000&&p.at<reading.at),{at:reading.at,value:reading.value}].slice(-3600)};
}
export function placeComponent(item:Component,layout:Layout,gap=12):Component|null{
  if(item.width>layout.design_width||item.height>layout.design_height)return null;
  const start={x:Math.min(item.x*layout.design_width,layout.design_width-item.width),y:Math.min(item.y*layout.design_height,layout.design_height-item.height)};
  const boxes=layout.components.map(c=>({x:c.x*layout.design_width,y:c.y*layout.design_height,width:c.width,height:c.height}));
  const xs=[0,start.x,...boxes.flatMap(b=>[b.x+b.width+gap,b.x-item.width-gap])],ys=[0,start.y,...boxes.flatMap(b=>[b.y+b.height+gap,b.y-item.height-gap])];
  const candidates=xs.flatMap(x=>ys.map(y=>({x,y}))).filter(p=>p.x>=0&&p.y>=0&&p.x+item.width<=layout.design_width&&p.y+item.height<=layout.design_height);
  candidates.sort((a,b)=>(a.x-start.x)**2+(a.y-start.y)**2-((b.x-start.x)**2+(b.y-start.y)**2));
  const free=candidates.find(p=>boxes.every(b=>p.x+item.width+gap<=b.x||p.x>=b.x+b.width+gap||p.y+item.height+gap<=b.y||p.y>=b.y+b.height+gap));
  return free?{...item,x:free.x/layout.design_width,y:free.y/layout.design_height}:null;
}
export interface View {zoom:number;x:number;y:number}
export function zoomView(view:View,zoom:number,x:number,y:number):View{zoom=Math.max(.25,Math.min(4,zoom));const ratio=zoom/view.zoom;return {zoom,x:x-(x-view.x)*ratio,y:y-(y-view.y)*ratio};}
