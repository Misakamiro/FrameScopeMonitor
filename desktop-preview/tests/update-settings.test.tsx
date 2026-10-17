import React from "react";
import { describe,it,expect,vi } from "vitest";
import { renderToStaticMarkup } from "react-dom/server";
const fixture=vi.hoisted(()=>({snapshot:null as any,error:null,pending:false,action:vi.fn()}));
vi.mock("../src/contexts/update-context",()=>({useUpdate:()=>fixture}));
vi.mock("../src/preview/runtime/platform",()=>({isDesktop:()=>true}));
import { UpdateSettings } from "../src/preview/settings/UpdateSettings";
function reset(){fixture.snapshot={build:{version:"2.0.0-beta.1",channel:"beta",git_sha:"abcdef123456"},previous:null,state:{preferences:{startup_check:true,include_beta:true,accelerated:true},phase:"idle",releases:[],downloaded:0,total:0,ready:null}};}
describe("update settings states",()=>{
  it("shows version, opt-in controls and manual stable return without idle download animation",()=>{
    reset();const html=renderToStaticMarkup(<UpdateSettings/>);
    for(const text of ["2.0.0-beta.1","abcdef1234","启动时检查更新","包含测试版","使用 GitHub 加速下载","查找可返回的正式版"])expect(html).toContain(text);
    expect(html).not.toContain("取消下载");expect(html).not.toContain("下载中");
  });
  it("stable channel offers manual download, completed downloads offer installation",()=>{
    reset();fixture.snapshot.build.channel="stable";
    fixture.snapshot.state.releases=[{id:42,notes:"fixes",manifest:{version:"2.0.1",channel:"stable"}}];
    let html=renderToStaticMarkup(<UpdateSettings/>);
    expect(html).not.toContain("查找可返回的正式版");expect(html).toContain("下载此版本");
    fixture.snapshot.state.ready=42;html=renderToStaticMarkup(<UpdateSettings/>);
    expect(html).toContain("安装并重启");
  });
  it("only active downloads expose cancellation and previous version exposes recovery",()=>{
    reset();fixture.snapshot.state.phase="downloading";fixture.snapshot.state.total=100;fixture.snapshot.state.downloaded=25;
    fixture.snapshot.previous={version:"2.0.0-beta.1"};
    const html=renderToStaticMarkup(<UpdateSettings/>);
    expect(html).toContain("取消下载");expect(html).toContain("恢复上一版");
  });
});
