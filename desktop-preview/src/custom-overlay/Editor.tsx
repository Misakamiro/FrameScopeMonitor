import {useEffect,useRef,useState,type PointerEvent} from 'react';
import {Box,Button,Checkbox,Grid,Heading,HStack,Input,Slider,SliderTrack,SliderFilledTrack,SliderThumb,Text,VStack} from '@chakra-ui/react';
import {useNavigate,useSearchParams} from 'react-router-dom';
import {CustomSelect} from '@/components/special/custom-select';
import {LiquidGlassCard} from '@/components/special/liquid-glass-card';
import {PreviewDialog,useFloatingFieldStyle} from '@/preview/pages/shared';
import {nativeOperation} from '@/preview/runtime/platform';
import {searchItems} from '@/lib/unified-search';
import {BUILTIN_CHINESE_FONTS} from '@/lib/fonts';
import type {OverlaySensor} from '@/lib/overlay-items';
import {OverlayWorkbench as FeatureWorkbench} from '@/components/ui/overlay-workbench';
import {Widget} from './Renderer';
import {clone,createComponent,geometry,instance,isFrame,isNumeric,metrics,placeComponent,presentation,sensorValue,snap,zoomView,type Component,type ComponentStyle,type Display,type Kind,type Layout,type LayoutStore,type Presentation,type View} from './model';

type Editable={draft:Layout;templates:Component[]};
const kinds=[{value:'value',label:'数值'},{value:'progress',label:'数值与进度条'},{value:'trend',label:'数值与折线'}];
export default function CustomOverlayEditor(){
  const navigate=useNavigate(),[params]=useSearchParams(),field=useFloatingFieldStyle();
  const [state,setState]=useState<LayoutStore|null>(null),[displays,setDisplays]=useState<Display[]>([]),[data,setData]=useState<Record<string,unknown>>({});
  const [selected,setSelected]=useState(''),[query,setQuery]=useState(''),[error,setError]=useState(''),[dataError,setDataError]=useState(''),[notice,setNotice]=useState(''),[busy,setBusy]=useState(false),[dirty,setDirty]=useState(false);
  const [name,setName]=useState('我的布局'),[saveAs,setSaveAs]=useState(false),[saveName,setSaveName]=useState('');
  const [history,setHistory]=useState<Editable[]>([]),[future,setFuture]=useState<Editable[]>([]),[creating,setCreating]=useState(false),[newKind,setNewKind]=useState<Kind>('value'),[newName,setNewName]=useState(''),[leave,setLeave]=useState(false);
  const [libraryOpen,setLibraryOpen]=useState(true),[propertiesOpen,setPropertiesOpen]=useState(true),[active,setActive]=useState(false);
  const [viewport,setViewport]=useState({width:500,height:420}),[view,setView]=useState<View>({zoom:1,x:12,y:12});
  const [guides,setGuides]=useState<{vertical?:number;horizontal?:number}>({}),stage=useRef<HTMLDivElement>(null),current=useRef(state);current.current=state;
  const drag=useRef<{before:Editable;id:string;clientX:number;clientY:number;resize:boolean;scale:number}|null>(null),pan=useRef<{x:number;y:number;view:View}|null>(null);
  const draft=state?.draft,item=draft?.components.find(row=>row.id===selected),display=displays.find(row=>row.id===draft?.monitor);
  const targetW=display?.width||draft?.design_width||1920,targetH=display?.height||draft?.design_height||1080;
  const fit=Math.max(.001,Math.min((viewport.width-24)/targetW,(viewport.height-24)/targetH)),scale=fit*view.zoom;
  const sensors=(data.sensor_catalog||[]) as OverlaySensor[],saved=state?.layouts.find(l=>l.id===state.editing_id),format=item?presentation(item):null;
  useEffect(()=>{let alive=true;
    void Promise.all([nativeOperation<LayoutStore>('custom_overlay_get'),nativeOperation<Display[]>('custom_overlay_displays')]).then(([value,monitors])=>{
      if(!alive)return;const id=params.get('id'),selected=value.layouts.find(l=>l.id===(id||value.editing_id));
      if(id==='new'){value.editing_id=null;value.draft={monitor:monitors[0]?.id||'',design_width:monitors[0]?.width||1920,design_height:monitors[0]?.height||1080,components:[]};}
      else if(selected){value.draft=clone(selected.layout);value.editing_id=selected.id;setName(selected.name);}
      if(!value.draft.monitor&&monitors.length){value.draft.monitor=monitors[0].id;value.draft.design_width=monitors[0].width;value.draft.design_height=monitors[0].height;}
      setState(value);setDisplays(monitors);
    }).catch(cause=>{if(alive)setError(String(cause));});
    let inFlight=false;const poll=async()=>{if(inFlight)return;inFlight=true;try{const value=await nativeOperation<Record<string,unknown>>('get_overlay_hardware_data');if(alive){setData(value);setDataError('');}}catch(cause){if(alive){setData({});setDataError('监测数据读取失败：'+String(cause));}}finally{inFlight=false;}};
    void poll();const timer=setInterval(poll,500);return()=>{alive=false;clearInterval(timer);};
  },[params]);
  useEffect(()=>{const element=stage.current;if(!element)return;const observer=new ResizeObserver(entries=>setViewport({width:entries[0].contentRect.width,height:entries[0].contentRect.height}));observer.observe(element);return()=>observer.disconnect();},[!!state]);
  const fitted=()=>({zoom:1,x:(viewport.width-targetW*fit)/2,y:(viewport.height-targetH*fit)/2});
  useEffect(()=>{setView(fitted());},[viewport.width,viewport.height,targetW,targetH]);
  useEffect(()=>{const element=stage.current;if(!element)return;const wheel=(event:WheelEvent)=>{
    if(!element.contains(document.activeElement))return;event.preventDefault();const rect=element.getBoundingClientRect();setView(old=>zoomView(old,old.zoom*Math.exp(-event.deltaY*.0015),event.clientX-rect.left,event.clientY-rect.top));
  };element.addEventListener('wheel',wheel,{passive:false});return()=>element.removeEventListener('wheel',wheel);},[!!state]);
  function snapshot(value:LayoutStore):Editable{return clone({draft:value.draft,templates:value.templates});}
  function change(next:Editable){if(!state)return;setHistory(old=>[...old.slice(-49),snapshot(state)]);setFuture([]);setState({...state,...next});setDirty(true);setNotice('');}
  function update(values:Partial<Component>){if(!state||!item)return;change({...snapshot(state),draft:{...state.draft,components:state.draft.components.map(row=>row.id===item.id?{...row,...values}:row)}});}
  function style(values:Partial<ComponentStyle>){if(item)update({style:{...item.style,...values}});}
  function present(values:Partial<Presentation>){if(format)update({presentation:{...format,...values}});}
  function add(template:Component,saveTemplate=false){if(!state)return;
    const placed=placeComponent(instance(template),state.draft);
    if(!placed){setError('画布没有足够空位，请缩小组件或移除不需要的组件。');return;}
    const created=placed,box=geometry(created,state.draft,targetW,targetH);change({draft:{...state.draft,components:[...state.draft.components,created]},templates:saveTemplate?[...state.templates,template]:state.templates});setSelected(created.id);setError('');
    setView(old=>({...old,x:viewport.width/2-(box.x+box.width/2)*scale,y:viewport.height/2-(box.y+box.height/2)*scale}));setCreating(false);setNewName('');
  }
  function undo(redo=false){if(!state)return;const source=redo?future:history,target=source[source.length-1];if(!target)return;if(redo){setFuture(source.slice(0,-1));setHistory(old=>[...old,snapshot(state)]);}else{setHistory(source.slice(0,-1));setFuture(old=>[...old,snapshot(state)]);}setState({...state,...clone(target)});setDirty(true);}
  function pointerDown(event:PointerEvent<HTMLDivElement>,row:Component,resize=false){if(!state||busy)return;event.preventDefault();event.stopPropagation();stage.current?.focus();event.currentTarget.setPointerCapture(event.pointerId);setSelected(row.id);drag.current={before:snapshot(state),id:row.id,clientX:event.clientX,clientY:event.clientY,resize,scale};}
  function pointerMove(event:PointerEvent<HTMLDivElement>){
    if(pan.current){const p=pan.current;setView({...p.view,x:p.view.x+event.clientX-p.x,y:p.view.y+event.clientY-p.y});return;}
    const moving=drag.current,latest=current.current;if(!moving||!latest)return;const layout=moving.before.draft,row=layout.components.find(c=>c.id===moving.id)!;
    const dx=(event.clientX-moving.clientX)/moving.scale,dy=(event.clientY-moving.clientY)/moving.scale,box=geometry(row,layout,targetW,targetH);let values:Partial<Component>;
    if(moving.resize){values={width:Math.max(16,Math.min(layout.design_width*(1-row.x),row.width+dx/box.scale)),height:Math.max(16,Math.min(layout.design_height*(1-row.y),row.height+dy/box.scale))};setGuides({});}
    else{const result=snap(row,layout.components,layout,row.x*layout.design_width+dx/box.scale,row.y*layout.design_height+dy/box.scale,6/(moving.scale*box.scale));values={x:result.x,y:result.y};setGuides({vertical:result.vertical===undefined?undefined:result.vertical*box.scale,horizontal:result.horizontal===undefined?undefined:result.horizontal*box.scale});}
    setState({...latest,draft:{...latest.draft,components:latest.draft.components.map(c=>c.id===row.id?{...c,...values}:c)}});
  }
  function pointerUp(){pan.current=null;if(!drag.current)return;const before=drag.current.before;setHistory(old=>[...old.slice(-49),before]);setFuture([]);drag.current=null;setGuides({});setDirty(true);}
  async function save(copy=false){if(!state)return;setBusy(true);setError('');try{
    const next=await nativeOperation<LayoutStore>('custom_overlay_save',{revision:state.revision,id:copy?null:state.editing_id,layout_revision:copy?null:saved?.revision,name:copy?saveName:name,...snapshot(state)});
    setState(next);setName(next.layouts.find(l=>l.id===next.editing_id)!.name);setDirty(false);setSaveAs(false);setNotice('布局已保存，可在外观设置中选择应用。当前显示保持原版本。');
  }catch(cause){setError(String(cause));}finally{setBusy(false);}}
  const input=(label:string,value:string|number,onChange:(value:string)=>void,type='text',min?:number,max?:number)=><Box flex={1} minW={0}><Text fontSize="xs" mb={1}>{label}</Text><Input size="sm" aria-label={label} type={type} min={min} max={max} value={value} onChange={event=>onChange(event.target.value)} sx={field.sx}/></Box>;
  const color=(label:string,value:string,onChange:(value:string)=>void)=><HStack justify="space-between"><Text fontSize="xs">{label}</Text><Input aria-label={label} type="color" value={value.slice(0,7)} onChange={e=>onChange(e.target.value+value.slice(7))} w="64px" h="30px" p={0} cursor="pointer"/></HStack>;
  const opacity=(label:string,value:number,onChange:(value:number)=>void)=><Box><Text fontSize="xs" mb={1}>{label} · {Math.round(value*100)}%</Text><Box px={2}><Slider aria-label={label} min={0} max={100} value={Math.round(value*100)} onChange={n=>onChange(n/100)}><SliderTrack><SliderFilledTrack/></SliderTrack><SliderThumb/></Slider></Box></Box>;
  if(!state)return <Text p={6}>{error||'正在读取自定义布局…'}</Text>;
  const library=[...Object.keys(metrics).map(metric=>({...createComponent('value',metric),id:'builtin:'+metric})),...state.templates],reading=item?sensorValue(item,data):null;
  return <FeatureWorkbench backLabel="悬浮框" id="search-custom-overlay" title="自定义悬浮窗" description="命名保存多套布局，在外观设置中选择应用" onBack={()=>dirty?setLeave(true):navigate('/overlay-panel')} actions={<><Button size="sm" isDisabled={!history.length||busy} onClick={()=>undo()}>撤销</Button><Button size="sm" isDisabled={!future.length||busy} onClick={()=>undo(true)}>重做</Button><Button size="sm" isDisabled={busy} onClick={()=>{setSaveName(name+' 副本');setSaveAs(true);}}>另存副本</Button><Button size="sm" isLoading={busy} onClick={()=>void save()}>保存布局</Button></>}>
    {error&&<Text color="red.400" role="alert" mb={3}>{error}</Text>}{dataError&&<Text color="orange.400" role="alert">{dataError}</Text>}{notice&&<Text role="status" mb={3}>{notice}</Text>}
    <HStack mb={3} wrap="wrap"><Input aria-label="布局名称" value={name} onChange={e=>{setName(e.target.value);setDirty(true);}} maxW="220px" sx={field.sx}/><CustomSelect ariaLabel="目标显示器" value={draft!.monitor} onChange={id=>change({...snapshot(state),draft:{...draft!,monitor:id}})} options={[...displays.map(row=>({value:row.id,label:row.name+' · '+row.width+' × '+row.height})),...(!display&&draft!.monitor?[{value:draft!.monitor,label:draft!.monitor+'（已断开）'}]:[])]}/></HStack>
    <HStack mb={3} wrap="wrap"><Button size="xs" onClick={()=>setLibraryOpen(v=>!v)}>{libraryOpen?'收起':'展开'}组件库</Button><Button size="xs" onClick={()=>setPropertiesOpen(v=>!v)}>{propertiesOpen?'收起':'展开'}属性</Button><Button size="xs" aria-label="缩小画布" onClick={()=>setView(old=>zoomView(old,old.zoom/1.25,viewport.width/2,viewport.height/2))}>−</Button><Text fontSize="xs">{Math.round(view.zoom*100)}%</Text><Button size="xs" aria-label="放大画布" onClick={()=>setView(old=>zoomView(old,old.zoom*1.25,viewport.width/2,viewport.height/2))}>＋</Button><Button size="xs" onClick={()=>setView(fitted())}>适应画布</Button></HStack>
    <Grid templateColumns={(libraryOpen?'minmax(140px,170px) ':'')+'minmax(220px,1fr)'+(propertiesOpen?' minmax(260px,300px)':'')} gap={3} alignItems="start" minW={0} overflowX="auto">
      {libraryOpen&&<LiquidGlassCard p={3}><Heading size="sm" mb={3}>组件库</Heading><Input size="sm" aria-label="搜索组件" placeholder="搜索组件" value={query} onChange={e=>setQuery(e.target.value)} sx={field.sx}/><Button size="sm" width="100%" my={3} onClick={()=>setCreating(true)}>新建组件</Button><VStack align="stretch" maxH="58vh" overflowY="auto">{searchItems(library,query,row=>[row.name,row.metric]).map(row=><HStack key={row.id}><Button size="sm" flex={1} justifyContent="flex-start" overflow="hidden" onClick={()=>add(row)}>{row.name}</Button>{!row.id.startsWith('builtin:')&&<Button size="xs" aria-label={'删除模板 '+row.name} onClick={()=>change({...snapshot(state),templates:state.templates.filter(t=>t.id!==row.id)})}>×</Button>}</HStack>)}</VStack></LiquidGlassCard>}
      <Box minW={0}><Box ref={stage} width="100%" height="58vh" minH="340px" maxH="620px" position="relative" overflow="hidden" border="1px solid" borderColor={active?'var(--framescope-primary)':'var(--framescope-control-border)'} borderRadius="md" background="var(--framescope-control-bg, #20242c)" tabIndex={0} aria-label="布局画布" onFocus={()=>setActive(true)} onBlur={e=>{if(!e.currentTarget.contains(e.relatedTarget))setActive(false);}} style={{touchAction:'none'}} onPointerDown={e=>{e.currentTarget.focus();e.currentTarget.setPointerCapture(e.pointerId);pan.current={x:e.clientX,y:e.clientY,view};}} onPointerMove={pointerMove} onPointerUp={pointerUp} onPointerCancel={pointerUp}>
        <Box position="absolute" left={0} top={0} width={targetW+'px'} height={targetH+'px'} transform={'translate('+view.x+'px,'+view.y+'px) scale('+scale+')'} transformOrigin="top left" background="var(--framescope-control-bg, #20242c)" outline="1px solid #8888" data-overlay-canvas>
          {draft!.components.map(row=>{const box=geometry(row,draft!,targetW,targetH);return <Box key={row.id} data-overlay-component={row.id} position="absolute" left={box.x+'px'} top={box.y+'px'} width={box.width+'px'} height={box.height+'px'} outline={selected===row.id?(2/scale)+'px solid var(--framescope-primary)':(1/scale)+'px dashed #8888'} cursor="move" onPointerDown={e=>pointerDown(e,row)} aria-label={'画布组件 '+row.name} tabIndex={0} onFocus={()=>setSelected(row.id)}><Box transform={'scale('+box.scale+')'} transformOrigin="top left" pointerEvents="none"><Widget item={row} data={data}/></Box>{selected===row.id&&<Box position="absolute" right={-4/scale} bottom={-4/scale} w={10/scale+'px'} h={10/scale+'px'} bg="var(--framescope-primary)" cursor="nwse-resize" onPointerDown={e=>pointerDown(e,row,true)} aria-label="调整组件大小"/>}</Box>;})}
          {guides.vertical!==undefined&&<Box position="absolute" pointerEvents="none" left={guides.vertical+'px'} top={0} bottom={0} borderLeft={(1/scale)+'px solid #38bdf8'}/>} {guides.horizontal!==undefined&&<Box position="absolute" pointerEvents="none" top={guides.horizontal+'px'} left={0} right={0} borderTop={(1/scale)+'px solid #38bdf8'}/>}
        </Box>
      </Box><Text fontSize="xs" opacity={.7} mt={2}>{targetW} × {targetH} · 点击画布后滚轮缩放，拖动空白处平移 · 坐标包含任务栏区域{!display?' · 目标屏幕断开时暂停显示':''}</Text><HStack mt={3}>{input('设计宽度',draft!.design_width,v=>change({...snapshot(state),draft:{...draft!,design_width:Number(v)}}),'number',320,16384)}{input('设计高度',draft!.design_height,v=>change({...snapshot(state),draft:{...draft!,design_height:Number(v)}}),'number',200,16384)}</HStack></Box>
      {propertiesOpen&&<LiquidGlassCard p={3} display="flex" flexDirection="column" maxH="67vh" minH={0}><Heading size="sm" mb={3} flexShrink={0}>组件属性</Heading>{item&&format?<><VStack align="stretch" spacing={3} flex={1} minH={0} overflowY="auto" px={2} pb={2} sx={{'& > *':{flexShrink:0}}}>
        <Text fontSize="xs" fontWeight="bold">数据</Text>{input('组件名称',item.name,v=>update({name:v}))}
        {item.kind==='text'?input('文字内容',item.text,v=>update({text:v})):<><CustomSelect ariaLabel="数据指标" value={item.metric} onChange={v=>update({metric:v,binding:null,kind:isNumeric(v)?item.kind:'value',presentation:undefined})} options={Object.entries(metrics).map(([value,m])=>({value,label:m.label}))}/>
          {isFrame(item.metric)?<Text fontSize="xs">当前帧来源：{reading?.source==='presentmon'?'主监控':reading?.source.startsWith('etw')?'ETW 备用':'等待帧数据'}</Text>:metrics[item.metric]&&isNumeric(item.metric)&&<CustomSelect ariaLabel="传感器接口" value={item.binding?JSON.stringify(item.binding):''} onChange={v=>update({binding:v?JSON.parse(v):null})} options={[{value:'',label:'系统当前默认传感器'},...sensors.filter(s=>s.category===item.metric).map(s=>({value:JSON.stringify({category:s.category,device_id:s.device_id,sensor_id:s.sensor_id}),label:s.device_name+' · '+s.name+(s.available?'':'（离线）')})),...(item.binding&&!sensors.some(s=>s.category===item.binding!.category&&s.device_id===item.binding!.device_id&&s.sensor_id===item.binding!.sensor_id)?[{value:JSON.stringify(item.binding),label:'已绑定传感器（离线）'}]:[])]}/>}
          <Text fontSize="xs" role="status">{reading?.text}</Text><Checkbox isChecked={format.show_name} onChange={e=>present({show_name:e.target.checked})}>显示名称</Checkbox>
          {isNumeric(item.metric)&&<><Checkbox isChecked={format.show_unit} onChange={e=>present({show_unit:e.target.checked})}>显示单位</Checkbox>{input('小数位',format.decimals,v=>present({decimals:Number(v)}),'number',0,4)}
            <Text fontSize="xs" fontWeight="bold" mt={2}>图表</Text><CustomSelect ariaLabel="显示方式" value={item.kind} onChange={v=>update({kind:v as Kind,height:v==='trend'?Math.max(item.height,120):item.height,presentation:{...format,show_value:v==='value'?true:format.show_value}})} options={kinds}/>
            {item.kind!=='value'&&<><Checkbox isChecked={format.show_value} onChange={e=>present({show_value:e.target.checked})}>显示数值</Checkbox>{item.kind==='trend'&&<><Checkbox isChecked={format.auto_range} onChange={e=>present({auto_range:e.target.checked})}>自动范围</Checkbox>{input('折线时长（秒）',format.duration,v=>present({duration:Number(v)}),'number',5,300)}{input('折线宽度',format.line_width,v=>present({line_width:Number(v)}),'number',.5,12)}<Checkbox isChecked={format.fill} onChange={e=>present({fill:e.target.checked})}>填充折线下方</Checkbox></>}
              {(item.kind==='progress'||!format.auto_range)&&<HStack>{input('最小值',item.minimum,v=>update({minimum:Number(v)}),'number')}{input('最大值',item.maximum,v=>update({maximum:Number(v)}),'number')}</HStack>}
              {item.kind==='progress'&&<CustomSelect ariaLabel="进度方向" value={format.direction} onChange={v=>present({direction:v as Presentation['direction']})} options={[{value:'right',label:'向右'},{value:'left',label:'向左'},{value:'up',label:'向上'},{value:'down',label:'向下'}]}/>}</>}
          </>}
        </>}
        <Text fontSize="xs" fontWeight="bold" mt={2}>外观</Text><CustomSelect ariaLabel="字体" value={item.style.font} onChange={v=>style({font:v})} options={[...new Set([...BUILTIN_CHINESE_FONTS,item.style.font])].map(v=>({value:v,label:v}))}/><HStack>{input('字号',item.style.size,v=>style({size:Number(v)}),'number',6,240)}{input('粗细',item.style.weight,v=>style({weight:Number(v)}),'number',100,900)}</HStack>
        <CustomSelect ariaLabel="文字对齐" value={item.style.align} onChange={v=>style({align:v as ComponentStyle['align']})} options={[{value:'left',label:'左对齐'},{value:'center',label:'居中'},{value:'right',label:'右对齐'}]}/>
        <HStack><Checkbox isChecked={item.style.italic} onChange={e=>style({italic:e.target.checked})}>斜体</Checkbox><Checkbox isChecked={item.style.decoration==='underline'} onChange={e=>style({decoration:e.target.checked?'underline':'none'})}>下划线</Checkbox></HStack>
        {color('文字与图表颜色',item.style.color,v=>style({color:v}))}{color('背景颜色',item.style.background,v=>style({background:v}))}{opacity('背景不透明度',item.style.background.length===9?parseInt(item.style.background.slice(7),16)/255:1,v=>style({background:item.style.background.slice(0,7)+Math.round(v*255).toString(16).padStart(2,'0')}))}{color('边框颜色',item.style.border_color,v=>style({border_color:v}))}
        <HStack>{input('边框宽度',item.style.border_width,v=>style({border_width:Number(v)}),'number',0,20)}{input('圆角',item.style.radius,v=>style({radius:Number(v)}),'number',0,500)}</HStack>{opacity('组件不透明度',item.style.opacity,v=>style({opacity:v}))}
        <HStack>{input('横坐标（设计像素）',Math.round(item.x*draft!.design_width),v=>update({x:Math.max(0,Math.min(1,Number(v)/draft!.design_width))}),'number',0,draft!.design_width)}{input('纵坐标（设计像素）',Math.round(item.y*draft!.design_height),v=>update({y:Math.max(0,Math.min(1,Number(v)/draft!.design_height))}),'number',0,draft!.design_height)}</HStack>
        <HStack>{input('宽度',item.width,v=>update({width:Number(v)}),'number',16,16384)}{input('高度',item.height,v=>update({height:Number(v)}),'number',16,16384)}</HStack>
        </VStack><VStack align="stretch" flexShrink={0} pt={3} mt={3} borderTopWidth="1px" borderColor="var(--framescope-control-border)" sx={{'& > button':{flexShrink:0,minHeight:'32px'}}}>
        <Button size="sm" onClick={()=>{change({...snapshot(state),templates:[...state.templates,instance(item)]});setNotice('已存为独立组件模板。');}}>另存为组件模板</Button><Button size="sm" onClick={()=>add(item)}>复制组件</Button><Button size="sm" variant="ghost" onClick={()=>{change({...snapshot(state),draft:{...draft!,components:draft!.components.filter(c=>c.id!==item.id)}});setSelected('');}}>移除组件</Button>
      </VStack></>:<Text fontSize="sm" opacity={.7}>选择画布组件以编辑属性。</Text>}</LiquidGlassCard>}
    </Grid>
    <PreviewDialog title="新建组件模板" isOpen={creating} onClose={()=>setCreating(false)}><VStack align="stretch" spacing={4}><Input aria-label="新组件名称" placeholder="组件名称" value={newName} onChange={e=>setNewName(e.target.value)} sx={field.sx}/><CustomSelect ariaLabel="新组件类型" value={newKind} onChange={v=>setNewKind(v as Kind)} options={kinds}/><Button isDisabled={!newName.trim()} onClick={()=>add({...createComponent(newKind),name:newName.trim()},true)}>创建并添加到画布</Button></VStack></PreviewDialog>
    <PreviewDialog title="另存布局副本" isOpen={saveAs} onClose={()=>setSaveAs(false)}><Input aria-label="副本名称" value={saveName} onChange={e=>setSaveName(e.target.value)} sx={field.sx}/><Button mt={4} isLoading={busy} isDisabled={!saveName.trim()} onClick={()=>void save(true)}>保存副本</Button></PreviewDialog>
    <PreviewDialog title="布局尚未保存" isOpen={leave} onClose={()=>setLeave(false)}><Text>离开后保留上次保存的布局。</Text><HStack mt={4}><Button onClick={()=>setLeave(false)}>继续编辑</Button><Button onClick={()=>navigate('/overlay-panel')}>放弃改动并离开</Button></HStack></PreviewDialog>
  </FeatureWorkbench>;
}
