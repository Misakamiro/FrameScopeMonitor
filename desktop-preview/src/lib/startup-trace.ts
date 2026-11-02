export interface StartupTrace {failed:boolean;painted:boolean;events:{phase:string;time:number}[]}
declare global {interface Window {__FRAMESCOPE_STARTUP__?:StartupTrace}}

export function traceStartup(phase:string) {
  if(typeof window==='undefined')return;
  const state=window.__FRAMESCOPE_STARTUP__??={failed:false,painted:false,events:[]};
  if(phase==='frontend-failed')state.failed=true;
  if(phase==='ui-painted')state.painted=true;
  state.events.push({phase,time:performance.now()});if(state.events.length>40)state.events.shift();
  const native=window as unknown as {__TAURI_INTERNALS__?:{invoke:(command:string,args:unknown)=>Promise<unknown>};__FRAMESCOPE_BROWSER_PREVIEW__?:boolean};
  if(native.__TAURI_INTERNALS__&&!native.__FRAMESCOPE_BROWSER_PREVIEW__){
    void native.__TAURI_INTERNALS__.invoke('preview_native',{command:'startup_diagnostic',args:{phase}}).catch(()=>{});
  }
}
