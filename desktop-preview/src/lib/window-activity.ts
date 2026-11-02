import {listen} from '@tauri-apps/api/event';
import {isDesktop,isMainWindow,nativeOperation} from '@/preview/runtime/platform';
import {connectUiActivity,uiActivity,type WindowActivitySnapshot} from './ui-activity';

export function startWindowActivity() {
  if(!isMainWindow())return()=>{};
  const style=()=>{document.documentElement.dataset.mainWindowActive=String(uiActivity.getSnapshot());};
  const stopStyle=uiActivity.subscribe(style);style();
  if(isDesktop()){
    const stop=connectUiActivity({
    listen:callback=>listen<WindowActivitySnapshot>('main-window-activity',event=>callback(event.payload)),
    read:()=>nativeOperation<WindowActivitySnapshot>('main_window_activity'),
    });
    return()=>{stop();stopStyle();delete document.documentElement.dataset.mainWindowActive;};
  }
  const update=()=>uiActivity.setActive(!document.hidden);
  document.addEventListener('visibilitychange',update);update();
  return()=>{document.removeEventListener('visibilitychange',update);stopStyle();delete document.documentElement.dataset.mainWindowActive;};
}
