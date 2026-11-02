import {describe,it,expect,vi} from 'vitest';
import {createUiActivity,connectUiActivity,type WindowActivitySnapshot} from '../src/lib/ui-activity';

describe('authoritative main-window activity',()=>{
  it('keeps the latest native transition when an initial read completes late',async()=>{
    let deliver:(state:WindowActivitySnapshot)=>void=()=>{};
    let finish:(state:WindowActivitySnapshot)=>void=()=>{};
    const state=createUiActivity(),listener=vi.fn();state.subscribe(listener);
    const stop=connectUiActivity({listen:async callback=>{deliver=callback;return()=>{};},read:()=>new Promise(resolve=>{finish=resolve;})},state);
    await Promise.resolve();deliver({visible:false,minimized:false,revision:2});
    finish({visible:true,minimized:false,revision:1});await Promise.resolve();
    expect(state.getSnapshot()).toBe(false);expect(listener).toHaveBeenCalledTimes(1);stop();
  });
  it('pauses on minimized or hidden state but not merely losing focus',async()=>{
    let deliver:(state:WindowActivitySnapshot)=>void=()=>{};const state=createUiActivity();
    const stop=connectUiActivity({listen:async callback=>{deliver=callback;return()=>{};},read:async()=>({visible:true,minimized:false,revision:0})},state);
    await Promise.resolve();await Promise.resolve();
    deliver({visible:true,minimized:true,revision:1});expect(state.getSnapshot()).toBe(false);
    deliver({visible:true,minimized:false,revision:2});expect(state.getSnapshot()).toBe(true);
    deliver({visible:false,minimized:false,revision:3});expect(state.getSnapshot()).toBe(false);stop();
  });
  it('releases a subscription that arrives after root unmount',async()=>{
    let ready:(stop:()=>void)=>void=()=>{};const unlisten=vi.fn(),read=vi.fn();
    const stop=connectUiActivity({listen:()=>new Promise(resolve=>{ready=resolve;}),read},createUiActivity());stop();ready(unlisten);
    await Promise.resolve();expect(unlisten).toHaveBeenCalledOnce();expect(read).not.toHaveBeenCalled();
  });
});
