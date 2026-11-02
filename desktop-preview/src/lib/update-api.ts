import { nativeOperation } from "../preview/runtime/platform";
export interface BuildInfo { version:string; git_sha:string; channel:"stable"|"beta"; edition?:"offline"|"full" }
export interface UpdatePreferences { startup_check:boolean; include_beta:boolean; accelerated:boolean }
export interface UpdateRelease {id:number;tag:string;notes:string;manifest:{version:string;channel:string;git_sha:string;installer:{size:number;name:string;sha256:string}}}
export interface UpdateSnapshot {
  build:BuildInfo;
  state:{preferences:UpdatePreferences;phase:string;error:string|null;releases:UpdateRelease[];downloaded:number;total:number;ready:number|null;last_checked?:string|null;download_route?:'origin'|'accelerator'|null;bytes_per_second?:number};
  previous:{version:string;git_sha:string}|null;
}
type Command="update_status"|"update_ready"|"update_preferences"|"update_check"|"update_download"|"update_cancel"|"update_install"|"update_restore";
export async function updateAction(command:Command,args:Record<string,unknown>={}):Promise<UpdateSnapshot>{
  const keys=command==="update_download"||command==="update_install"?["release_id"]:
    command==="update_preferences"?["startup_check","include_beta","accelerated"]:command==="update_check"?["return_stable"]:command==="update_ready"?["ui_ready"]:[];
  if(Object.keys(args).some(key=>!keys.includes(key)))throw new Error("不支持的更新参数");
  if(command==="update_ready"&&args.ui_ready!==true)throw new Error("主界面尚未完成绘制确认");
  if((command==="update_download"||command==="update_install")&&(!Number.isSafeInteger(args.release_id)||Number(args.release_id)<=0))throw new Error("缺少有效发行标识");
  return nativeOperation<UpdateSnapshot>(command,args);
}
