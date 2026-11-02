import {uiActivity} from './ui-activity';
import {traceStartup} from './startup-trace';

interface UiHealth {mounted:boolean;visible:boolean;failed:boolean;width:number;height:number;documentHidden:boolean}
export function canConfirmUi(state:UiHealth) {
  return state.mounted&&state.visible&&!state.failed&&!state.documentHidden&&state.width>0&&state.height>0;
}
export function createStartupGate() {
  let frames=0,confirmed=false;
  return {
    reset:()=>{frames=0;confirmed=false;},
    observe:(state:UiHealth)=>{
      if(confirmed)return false;
      frames=canConfirmUi(state)?frames+1:0;
      if(frames<2)return false;confirmed=true;return true;
    },
  };
}

export function confirmPaintedUi(confirm:()=>Promise<unknown>,onError:(error:unknown)=>void) {
  const gate=createStartupGate();let stopped=false,done=false,pending=false,frame=0;
  let retry:ReturnType<typeof setTimeout>|undefined;
  const schedule=()=>{if(!stopped&&!done&&!pending&&uiActivity.getSnapshot()&&!frame)frame=requestAnimationFrame(check);};
  const check=()=>{
    frame=0;if(stopped||done||!uiActivity.getSnapshot())return;
    const layout=document.querySelector<HTMLElement>('[data-main-layout]');
    const bounds=layout?.getBoundingClientRect();
    const healthy=gate.observe({mounted:!!layout,visible:uiActivity.getSnapshot(),failed:!!window.__FRAMESCOPE_STARTUP__?.failed,
      width:bounds?.width||0,height:bounds?.height||0,documentHidden:getComputedStyle(document.body).contentVisibility==='hidden'||!!layout&&(getComputedStyle(layout).display==='none'||getComputedStyle(layout).visibility==='hidden')});
    if(!healthy){schedule();return;}
    pending=true;traceStartup('ui-painted');
    void confirm().then(()=>{done=true;traceStartup('update-confirmed');}).catch(error=>{
      onError(error);if(!stopped)retry=setTimeout(()=>{pending=false;gate.reset();schedule();},1000);
    });
  };
  const changed=()=>{if(frame)cancelAnimationFrame(frame);frame=0;gate.reset();schedule();};
  const unsubscribe=uiActivity.subscribe(changed);schedule();
  return()=>{stopped=true;unsubscribe();if(frame)cancelAnimationFrame(frame);if(retry)clearTimeout(retry);};
}
