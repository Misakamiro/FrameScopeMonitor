import { useState } from "react";
import { Box,Text,VStack,HStack,Switch,Button,Progress,Alert,AlertIcon } from "@chakra-ui/react";
import { useUpdate } from "@/contexts/update-context";
import { isDesktop } from "@/preview/runtime/platform";
const productVersion=__FRAMESCOPE_VERSION__;
export function UpdateSettings(){
  const {snapshot,error,pending,action}=useUpdate();
  const [confirmRestore,setConfirmRestore]=useState(false);
  const state=snapshot?.state;
  const busy=pending||!!state&&state.phase!=="idle";
  const run=(command:Parameters<typeof action>[0],args?:Record<string,unknown>)=>{void action(command,args).catch(()=>{});};
  return <VStack align="stretch" spacing={4}>
    <Text fontSize="xl" fontWeight="bold">FrameScope {snapshot?.build.version??productVersion.trim()}</Text>
    <Text fontSize="sm">{snapshot?`${snapshot.build.channel==="beta"?"测试版":"正式版"} · ${snapshot.build.git_sha.slice(0,10)}`:"浏览器预览，更新功能在桌面版中提供"}</Text>
    {state&&<>
      {([["startup_check","启动时检查更新"],["include_beta","包含测试版"],["accelerated","使用 GitHub 加速下载"]] as const).map(([key,label])=><HStack key={key} justify="space-between">
        <Text>{label}</Text><Switch aria-label={label} isDisabled={busy} isChecked={state.preferences[key]} onChange={e=>run("update_preferences",{...state.preferences,[key]:e.target.checked})}/>
      </HStack>)}
      <Text fontSize="xs" opacity={.7}>只检查和提醒，不自动下载或安装。加速使用第三方 gh-proxy.org；失败时回退 GitHub 原站。关闭测试版不会自动降级。</Text>
    </>}
    <HStack><Button isDisabled={!isDesktop()||busy} onClick={()=>run("update_check")}>检查更新</Button>
      {snapshot?.build.channel==="beta"&&<Button isDisabled={busy} onClick={()=>run("update_check",{return_stable:true})}>查找可返回的正式版</Button>}
    </HStack>
    {state?.phase==="checking"&&<Text role="status">正在检查更新…</Text>}
    {state?.phase==="cancelling"&&<Text role="status">正在取消下载…</Text>}
    {state?.last_checked&&state.phase==="idle"&&!state.error&&state.releases.length===0&&<Text role="status">已检查，当前没有符合所选渠道的更新。</Text>}
    {state?.phase==="downloading"&&<Box><Text>下载中 {Math.round(state.downloaded/1024/1024)} / {Math.round(state.total/1024/1024)} MB</Text>
      <Progress value={state.total?state.downloaded/state.total*100:0} mt={2}/><Button mt={2} onClick={()=>run("update_cancel")}>取消下载</Button></Box>}
    {state?.releases.map(release=><Box key={release.id} p={4} borderWidth="1px" borderRadius="lg">
      <Text fontWeight="bold">{release.manifest.version} · {release.manifest.channel==="beta"?"测试版":"正式版"}</Text>
      <Text whiteSpace="pre-wrap" fontSize="sm" my={3} maxH="240px" overflowY="auto">{release.notes||"此发行没有更新说明。"}</Text>
      <Button isDisabled={busy} onClick={()=>run(state.ready===release.id?"update_install":"update_download",{release_id:release.id})}>
        {state.ready===release.id?"安装并重启":"下载此版本"}</Button>
    </Box>)}
    <Text fontSize="sm">下载不影响监控。安装或恢复前，请先正常停止监控并等待报告完成。自定义目标、设置、报告和悬浮窗位置将保留。</Text>
    {snapshot?.previous&&<Box><Text>可恢复版本：{snapshot.previous.version}</Text>
      {!confirmRestore?<Button isDisabled={busy} onClick={()=>setConfirmRestore(true)}>恢复上一版</Button>:<HStack mt={2}>
        <Text>将恢复程序并重启，保留当前数据。</Text><Button isDisabled={busy} onClick={()=>{setConfirmRestore(false);run("update_restore");}}>确认恢复</Button><Button onClick={()=>setConfirmRestore(false)}>取消</Button></HStack>}
    </Box>}
    {(error||state?.error)&&<Alert status="error"><AlertIcon/><Text overflowWrap="anywhere">{error||state?.error}</Text></Alert>}
  </VStack>;
}
