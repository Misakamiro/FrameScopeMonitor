export interface UiActivity {
  getSnapshot:()=>boolean;
  subscribe:(listener:()=>void)=>()=>void;
}

export function createUiActivity(initial=true) {
  let active=initial;const listeners=new Set<()=>void>();
  return {
    getSnapshot:()=>active,
    subscribe:(listener:()=>void)=>{listeners.add(listener);return()=>{listeners.delete(listener);};},
    setActive:(value:boolean)=>{if(active===value)return;active=value;listeners.forEach(listener=>listener());},
  };
}

export const uiActivity=createUiActivity(typeof document==='undefined'||!document.hidden);

export interface WindowActivitySnapshot {visible:boolean;minimized:boolean;revision:number}
interface ActivityBridge {
  listen:(listener:(state:WindowActivitySnapshot)=>void)=>Promise<()=>void>;
  read:()=>Promise<WindowActivitySnapshot>;
}
export function connectUiActivity(bridge:ActivityBridge,target=uiActivity) {
  let disposed=false,lastRevision=-1;let unlisten:(()=>void)|undefined;
  const apply=(state:WindowActivitySnapshot)=>{
    if(disposed||state.revision<lastRevision)return;
    lastRevision=state.revision;target.setActive(state.visible&&!state.minimized);
  };
  void bridge.listen(apply).then(async stop=>{
    if(disposed){stop();return;}unlisten=stop;apply(await bridge.read());
  }).catch(error=>console.error('Main window visibility unavailable',error));
  return()=>{disposed=true;unlisten?.();};
}
