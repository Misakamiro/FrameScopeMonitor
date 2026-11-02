const assert=require('node:assert/strict'),path=require('node:path'),fs=require('node:fs');
const {chromium}=require(require.resolve('playwright',{paths:[process.env.FRAMESCOPE_BROWSER_DEPS||process.cwd()]}));
(async()=>{
 const output=process.argv[2]&&path.resolve(process.argv[2]);
 const frontend=path.resolve(__dirname,'..');process.chdir(frontend);
 const {createServer}=await import(require('node:url').pathToFileURL(require.resolve('vite',{paths:[frontend]})).href);
 const server=await createServer({root:frontend,server:{host:'127.0.0.1',port:1458,strictPort:true,hmr:false,watch:null}});await server.listen();
 const browser=await chromium.launch({channel:'msedge',headless:true}),results=[];
 try{
  for(const scenario of ['false','throw','save-pending','timeout']){
   const page=await browser.newPage({viewport:{width:1280,height:950}});
   await page.addInitScript(scenario=>{
    window.__FRAMESCOPE_BROWSER_PREVIEW__=true;window.osdProbe={scenario,order:[],starts:0};
    localStorage.setItem('nexbox_page_transition','off');
    localStorage.setItem('framescope.preview.store.settings.json',JSON.stringify({'has-launched':true,'jelly-bounce-enabled':false,'carousel-enabled':false}));
   },scenario);
   await page.route('**/src/preview/runtime/core.ts*',async route=>{
    const response=await route.fetch(),body=await response.text();
    const injection=`export async function invoke(command,args={}) {
     const p=window.osdProbe;
     if(command==='update_overlay_settings') {p.order.push('save-start');if(p.scenario==='save-pending')await new Promise(r=>p.release=r);p.order.push('save-finished');}
     if(command==='start_overlay_panel') {
      p.order.push('start');p.starts++;
      if(p.scenario==='timeout')return new Promise(r=>p.late=()=>r({success:true,message:'迟到启动成功'}));
      if(p.scenario==='throw')throw new Error('受控原生异常');
      return {success:false,message:'受控原生启动失败'};
     }`;
    const patched=body.replace(/export async function invoke\(command, args = \{\}\) \{/,injection);
    assert.notEqual(patched,body);await route.fulfill({response,body:patched});
   });
   await page.goto('http://127.0.0.1:1458/overlay-panel');
   const toggle=page.locator('label.chakra-switch input').first();
   await toggle.waitFor({state:'attached'});await page.locator('label.chakra-switch').first().click();
   if(scenario==='save-pending'){
    await page.waitForFunction(()=>!!window.osdProbe.release);
    assert.equal(await page.evaluate(()=>window.osdProbe.starts),0);
    await page.evaluate(()=>window.osdProbe.release());
   }
   const expected=scenario==='timeout'?'启动悬浮框超时':scenario==='throw'?'受控原生异常':'受控原生启动失败';
   await page.getByRole('status').filter({hasText:expected}).waitFor({timeout:scenario==='timeout'?17000:4000});
   assert.equal(await toggle.isDisabled(),false);assert.equal(await toggle.isChecked(),false);
   if(scenario==='save-pending'){
    const order=await page.evaluate(()=>window.osdProbe.order);
    assert.ok(order.indexOf('save-finished')<order.indexOf('start'));
   }
   if(scenario==='timeout'){
    await page.evaluate(()=>window.osdProbe.late());await page.waitForTimeout(150);
    assert.equal(await toggle.isChecked(),false);
    assert.equal(await page.getByRole('status').filter({hasText:'迟到启动成功'}).count(),0);
   }
   results.push({scenario,passed:true});await page.close();
  }
  console.log(JSON.stringify(results,null,2));
  if(output)fs.writeFileSync(output,JSON.stringify(results,null,2));
 }finally{await browser.close();await server.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
