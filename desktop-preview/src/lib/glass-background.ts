import {composeLuminance,foregroundForSurface,foregroundTokens,glassMaterial,linearChannel,type ForegroundBranch,type MaterialSettings} from './glass-material';
import {uiActivity} from './ui-activity';
import {backgroundReferences} from './background-reference';

const surfaces='[data-glass-surface],.jelly-bounce-card,.real-liquid-glass,.chakra-modal__content,.chakra-drawer__content,.chakra-menu__menu-list,.chakra-popover__content,.chakra-input,.chakra-textarea,.chakra-select,.chakra-tabs__tablist,.chakra-tooltip';

/** One bounded background sample shared by all visible surfaces, including portals. */
export function watchGlassBackground(settings:MaterialSettings,backgroundMode:string) {
  const canvas=document.createElement('canvas');canvas.width=64;canvas.height=40;
  const context=canvas.getContext('2d',{willReadFrequently:true});
  const branches=new WeakMap<Element,ForegroundBranch>();
  let pixels:Uint8ClampedArray|null=null;
  let source:HTMLImageElement|HTMLVideoElement|null=null;
  let sampledSource='';let scheduled=0;let lastUpdate=0;
  const fallback=()=>settings.scheme==='dark'?.003:.97;
  let videoReference=fallback();
  const material=glassMaterial(settings);
  const root=document.documentElement;
  const set=(element:HTMLElement,name:string,value:string)=>{
    if(element.style.getPropertyValue(name)!==value)element.style.setProperty(name,value);
  };
  function sample() {
    if(backgroundMode==='mr')return;
    source=document.querySelector<HTMLVideoElement>('[data-framescope-background="video"]')
      ??document.querySelector<HTMLImageElement>('[data-framescope-background="image"]');
    const key=source?.src||'';
    if(key!==sampledSource){pixels=null;sampledSource=key;}
    if(backgroundMode==='dynamic'){
      const video=source instanceof HTMLVideoElement?source:null;
      videoReference=backgroundReferences.video(key,fallback(),video&&video.readyState>=2?()=>{
        if(!context)throw new Error('Background sample context unavailable');
        context.drawImage(video,0,0,64,40);
        const data=context.getImageData(0,0,64,40).data;let sum=0;
        for(let i=0;i<data.length;i+=4)sum+=.2126*linearChannel(data[i]/255)+.7152*linearChannel(data[i+1]/255)+.0722*linearChannel(data[i+2]/255);
        return sum/(data.length/4);
      }:undefined);
      return;
    }
    if(!source||!context||!(source instanceof HTMLImageElement)||!source.complete||pixels)return;
    try{
      context.clearRect(0,0,64,40);context.drawImage(source,0,0,64,40);
      pixels=context.getImageData(0,0,64,40).data;
    }catch{pixels=null;}
  }
  function backgroundAt(rect:DOMRect):number {
    if(backgroundMode==='mr')return .03;
    if(backgroundMode==='dynamic')return videoReference;
    if(!source||!pixels)return fallback();
    const bounds=source.getBoundingClientRect();
    const iw=source instanceof HTMLVideoElement?source.videoWidth:source.naturalWidth;
    const ih=source instanceof HTMLVideoElement?source.videoHeight:source.naturalHeight;
    if(!iw||!ih||!bounds.width||!bounds.height)return settings.scheme==='dark'?.003:.97;
    const scale=Math.max(bounds.width/iw,bounds.height/ih);
    const offsetX=(bounds.width-iw*scale)/2,offsetY=(bounds.height-ih*scale)/2;
    let sum=0;
    for(let y=0;y<3;y++)for(let x=0;x<5;x++){
      const px=rect.left+rect.width*(x+.5)/5,py=rect.top+rect.height*(y+.5)/3;
      const sx=Math.max(0,Math.min(63,Math.floor((px-bounds.left-offsetX)/(iw*scale)*64)));
      const sy=Math.max(0,Math.min(39,Math.floor((py-bounds.top-offsetY)/(ih*scale)*40)));
      const i=(sy*64+sx)*4;
      sum+=.2126*linearChannel(pixels[i]/255)+.7152*linearChannel(pixels[i+1]/255)+.0722*linearChannel(pixels[i+2]/255);
    }
    return sum/15;
  }
  function apply(element:HTMLElement,luminance:number) {
    const animated=backgroundMode==='mr'||backgroundMode==='dynamic';
    const branch=foregroundForSurface(luminance,animated?undefined:branches.get(element));branches.set(element,branch);
    const tokens=foregroundTokens(branch);
    set(element,'--framescope-surface-fg',tokens.foreground);
    set(element,'--framescope-surface-muted',tokens.muted);
    set(element,'--framescope-control-fg',tokens.foreground);
    set(element,'--framescope-control-border',tokens.border);
    set(element,'--framescope-control-hover',tokens.hover);
  }
  function update() {
    if(scheduled)clearTimeout(scheduled);
    scheduled=0;if(document.hidden||!uiActivity.getSnapshot())return;lastUpdate=performance.now();
    sample();
    const rootLuma=backgroundAt(new DOMRect(0,0,innerWidth,innerHeight));
    apply(root,rootLuma);
    const fills=new Map<Element,{channel:number;alpha:number}>();
    for(const surface of document.querySelectorAll<HTMLElement>(surfaces)){
      const match=getComputedStyle(surface).backgroundColor.match(/rgba?\(\s*([\d.]+)[, ]+([\d.]+)[, ]+([\d.]+)(?:[, /]+([\d.]+))?\s*\)/);
      if(match)fills.set(surface,{channel:.2126*Number(match[1])+.7152*Number(match[2])+.0722*Number(match[3]),alpha:match[4]===undefined?1:Number(match[4])});
    }
    for(const element of document.querySelectorAll<HTMLElement>(surfaces+',.chakra-text,.chakra-heading,.chakra-form__label')){
      const rect=element.getBoundingClientRect();
      if(!rect.width||!rect.height||rect.bottom<0||rect.top>innerHeight)continue;
      if(element.closest('button,[role="tab"],[role="option"][aria-selected="true"]')&&!element.matches(surfaces))continue;
      let luma=backgroundAt(rect);
      if(element.closest('.chakra-modal__content,.chakra-drawer__content'))luma=composeLuminance(luma,0,.18);
      let parent:HTMLElement|null=element;
      const layers:{channel:number;alpha:number}[]=[];
      while(parent){if(parent.matches(surfaces))layers.push(fills.get(parent)??material);parent=parent.parentElement;}
      for(const layer of layers.reverse())luma=composeLuminance(luma,layer.channel,layer.alpha);
      apply(element,luma);
    }
  }
  function schedule(){if(!scheduled)scheduled=window.setTimeout(update,Math.max(0,100-(performance.now()-lastUpdate)));}
  const observer=new MutationObserver(schedule);
  observer.observe(document.body,{childList:true,subtree:true,attributes:true,attributeFilter:['src','class','data-glass-surface']});
  const resize=new ResizeObserver(schedule);resize.observe(document.body);
  document.addEventListener('scroll',schedule,true);document.addEventListener('load',schedule,true);
  // Run synchronously before React exposes the ready video layer to avoid a late recolor.
  document.addEventListener('loadeddata',update,true);
  document.addEventListener('transitionend',schedule,true);document.addEventListener('animationend',schedule,true);
  document.addEventListener('visibilitychange',schedule);window.addEventListener('resize',schedule);
  const stopActivity=uiActivity.subscribe(schedule);
  update();
  return ()=>{
    observer.disconnect();resize.disconnect();clearTimeout(scheduled);
    stopActivity();
    document.removeEventListener('loadeddata',update,true);
    document.removeEventListener('scroll',schedule,true);document.removeEventListener('load',schedule,true);
    document.removeEventListener('transitionend',schedule,true);document.removeEventListener('animationend',schedule,true);
    document.removeEventListener('visibilitychange',schedule);window.removeEventListener('resize',schedule);
  };
}
