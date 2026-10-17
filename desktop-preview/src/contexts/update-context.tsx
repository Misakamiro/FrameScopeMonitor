import { createContext,useContext,useEffect,useRef,useState,type ReactNode } from "react";
import { useToast } from "@chakra-ui/react";
import { listen } from "@tauri-apps/api/event";
import { isDesktop,isMainWindow } from "@/preview/runtime/platform";
import { useAppStartup } from "./app-startup-context";
import { updateAction,type UpdateSnapshot } from "@/lib/update-api";

type ContextValue={snapshot:UpdateSnapshot|null;error:string|null;pending:boolean;action:typeof updateAction};
const Context=createContext<ContextValue|null>(null);
export function useUpdate(){const value=useContext(Context);if(!value)throw new Error("UpdateProvider missing");return value;}
export function UpdateProvider({children}:{children:ReactNode}){
  const [snapshot,setSnapshot]=useState<UpdateSnapshot|null>(null);
  const [error,setError]=useState<string|null>(null);
  const [pending,setPending]=useState(false);
  const checked=useRef(false);
  const notified=useRef<number|null>(null);
  const {isStartupComplete}=useAppStartup();
  const toast=useToast();
  const action:typeof updateAction=async(command,args={})=>{
    setPending(true);setError(null);
    try{const next=await updateAction(command,args);if(next?.state)setSnapshot(next);return next;}
    catch(reason){setError(String(reason));throw reason;}
    finally{setPending(false);}
  };
  useEffect(()=>{
    if(!isDesktop()||!isMainWindow()||!isStartupComplete)return;
    let disposed=false;
    let unlisten:(()=>void)|undefined;
    void listen<UpdateSnapshot>("framescope-update",event=>{if(!disposed)setSnapshot(event.payload);})
      .then(fn=>{if(disposed)fn();else unlisten=fn;}).catch(reason=>setError(String(reason)));
    void (async()=>{
      try{
        await updateAction("update_ready");
        const current=await updateAction("update_status");
        if(disposed)return;
        setSnapshot(current);
        if(current.state.preferences.startup_check&&!checked.current){
          checked.current=true;
          const next=await updateAction("update_check");
          if(!disposed)setSnapshot(next);
        }
      }catch(reason){if(!disposed)setError(String(reason));}
    })();
    return()=>{disposed=true;unlisten?.();};
  },[isStartupComplete]);
  useEffect(()=>{
    const release=snapshot?.state.releases[0];
    if(!release||notified.current===release.id)return;
    notified.current=release.id;
    toast({title:`发现 FrameScope ${release.manifest.version}`,description:"在设置 → 关于／更新查看说明，点击后才会下载和安装。",status:"info",duration:10000,isClosable:true});
  },[snapshot,toast]);
  return <Context.Provider value={{snapshot,error,pending,action}}>{children}</Context.Provider>;
}
