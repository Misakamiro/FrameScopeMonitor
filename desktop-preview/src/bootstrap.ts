import {traceStartup} from './lib/startup-trace';

traceStartup('frontend-module');
const fail=()=>{
  traceStartup('frontend-failed');
  const root=document.getElementById('root');if(!root)return;
  root.innerHTML='<main style="min-height:100vh;display:grid;place-content:center;background:#15171d;color:#f4f5f7;font:16px system-ui;padding:32px;gap:16px"><h1>FrameScope 界面未能加载</h1><p>监测数据和设置已保留。可以重新加载界面，或导出启动诊断。</p><div><button id="framescope-retry" style="padding:10px 18px">重新加载界面</button> <button id="framescope-diagnostics" style="padding:10px 18px">导出启动诊断</button></div></main>';
  document.getElementById('framescope-retry')?.addEventListener('click',()=>location.reload());
  document.getElementById('framescope-diagnostics')?.addEventListener('click',()=>{
    const native=window as unknown as {__TAURI_INTERNALS__?:{invoke:(command:string,args:unknown)=>Promise<unknown>}};
    void native.__TAURI_INTERNALS__?.invoke('preview_native',{command:'export_startup_diagnostics',args:{}}).catch(()=>{});
  });
};
window.addEventListener('error',event=>{
  if(!window.__FRAMESCOPE_STARTUP__?.painted&&(event.error||event.target instanceof HTMLScriptElement))fail();
});
window.addEventListener('unhandledrejection',()=>traceStartup('frontend-warning'));
void import('./main').catch(fail);
