import React,{useEffect,useState} from 'react';
import {createRoot} from 'react-dom/client';
import {nativeOperation} from '@/preview/runtime/platform';
import {LayoutRenderer} from './Renderer';
import type {Layout,Display} from './model';
type Runtime={layout:Layout|null;display:Display|null;data:Record<string,unknown>};
function RuntimeWindow(){
  const [value,setValue]=useState<Runtime|null>(null),[size,setSize]=useState({width:innerWidth,height:innerHeight});
  useEffect(()=>{let alive=true,timer:ReturnType<typeof setTimeout>,ready=false;const generation=(window as unknown as {__FRAMESCOPE_OVERLAY_GENERATION__:number}).__FRAMESCOPE_OVERLAY_GENERATION__;
    const resize=()=>setSize({width:innerWidth,height:innerHeight});window.addEventListener('resize',resize);
    async function poll(){try{const next=await nativeOperation<Runtime>('custom_overlay_runtime',{generation});if(!alive)return;setValue(next);resize();
      if(!ready){ready=true;requestAnimationFrame(()=>requestAnimationFrame(()=>{if(alive)void nativeOperation('custom_overlay_ready',{generation});}));}}
      catch(cause){if(!ready)void nativeOperation('custom_overlay_ready',{generation,error:String(cause)});if(alive)setValue(null);}
      if(alive)timer=setTimeout(poll,500);}
    void poll();return()=>{alive=false;clearTimeout(timer);window.removeEventListener('resize',resize);};
  },[]);
  return value?.layout&&value.display?<LayoutRenderer layout={value.layout} width={size.width} height={size.height} data={value.data}/>:null;
}
export function bootCustomOverlay(){
  document.documentElement.style.cssText='background:transparent!important;overflow:hidden;';document.body.style.cssText='margin:0;background:transparent!important;overflow:hidden;';
  createRoot(document.getElementById('root')!).render(<RuntimeWindow/>);
}
