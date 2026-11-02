const {chromium}=require('C:/fs-build/b203/six-issue-browser-deps/node_modules/playwright');
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const origin=process.argv[2]||'http://127.0.0.1:1459',out=process.argv[3];
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});const results=[];
 try{
  const maker=await browser.newPage();await maker.goto(origin+'/targets');
  const clips=await maker.evaluate(async()=>{
   async function record(alternate){
    const c=document.createElement('canvas');c.width=128;c.height=90;const ctx=c.getContext('2d');ctx.fillStyle=alternate?'#fafafa':'#050505';ctx.fillRect(0,0,128,90);
    const stream=c.captureStream(10),recorder=new MediaRecorder(stream,{mimeType:'video/webm'}),chunks=[];
    recorder.ondataavailable=e=>chunks.push(e.data);const finished=new Promise(resolve=>recorder.onstop=resolve);recorder.start();
    for(let i=0;i<8;i++){ctx.fillStyle=alternate&&i%2===0?'#fafafa':'#050505';ctx.fillRect(0,0,128,90);await new Promise(r=>setTimeout(r,120));}
    recorder.stop();await finished;stream.getTracks().forEach(t=>t.stop());
    return [...new Uint8Array(await new Blob(chunks,{type:'video/webm'}).arrayBuffer())];
   }
   return {alternating:await record(true),dark:await record(false)};
  });await maker.close();
  if(process.argv[4]!=='matrix')await Promise.all(['mr','dynamic'].map(async background=>{
   const page=await browser.newPage({viewport:{width:1280,height:900}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
   await page.addInitScript(background=>{
    localStorage.setItem('nexbox-theme-mode','light');localStorage.setItem('nexbox_page_transition','off');
    localStorage.setItem('framescope.preview.store.settings.json',JSON.stringify({'has-launched':true,'background-mode':background,'dynamic-bg-video':'C:/isolated-foreground.webm','liquid-glass-enabled':true,'liquid-glass-mode':'normal','liquid-glass-blur':1,'carousel-enabled':false,'mr-color-mode':'custom','mr-custom-color':'#bf2ba5'}));
   },background);
   await page.goto(origin+'/targets');await page.getByRole('button',{name:'编辑 Counter-Strike 2'}).waitFor();
   if(background==='dynamic')await page.evaluate(async clips=>{
    window.clipUrls=Object.fromEntries(Object.entries(clips).map(([key,bytes])=>[key,URL.createObjectURL(new Blob([new Uint8Array(bytes)],{type:'video/webm'}))]));
    const v=document.querySelector('video');v.src=window.clipUrls.alternating;await v.play();
   },clips);
   await page.waitForTimeout(800);
   const samples=[];
   const read=async()=>page.locator('[aria-label="搜索目标"]').evaluate(e=>({fg:getComputedStyle(e).color,placeholder:getComputedStyle(e,'::placeholder').color,border:getComputedStyle(e).borderColor,icon:getComputedStyle(e.parentElement.querySelector('svg')).color}));
   const started=Date.now();let dialogBaseline;
   while(Date.now()-started<62000){
    const second=Math.floor((Date.now()-started)/1000);samples.push({second,...await read()});
    if(second===5||second===25){
     await page.getByRole('button',{name:'编辑 Counter-Strike 2'}).click();await page.waitForTimeout(250);
     const color=await page.getByRole('dialog').evaluate(e=>getComputedStyle(e).color);
     if(dialogBaseline)assert.equal(color,dialogBaseline);dialogBaseline=color;
     await page.keyboard.press('Escape');
    }
    if(second===10){await page.mouse.wheel(0,450);await page.setViewportSize({width:1140,height:800});}
    if(second===15){await page.evaluate(()=>{history.pushState({},'','/reports');dispatchEvent(new PopStateEvent('popstate'));});await page.waitForTimeout(200);await page.evaluate(()=>{history.pushState({},'','/targets');dispatchEvent(new PopStateEvent('popstate'));});}
    if(background==='dynamic'&&second===20)await page.evaluate(()=>{const v=document.querySelector('video');v.pause();v.currentTime=.6;void v.play();});
    await page.waitForTimeout(1000);
   }
   const variants=new Set(samples.map(sample=>JSON.stringify({fg:sample.fg,placeholder:sample.placeholder,border:sample.border,icon:sample.icon})));
   results.push({background,durationMs:Date.now()-started,variants:[...variants],samples});
   if(out)await page.screenshot({path:path.join(path.dirname(out),background+'-settled.png')});
   assert.equal(variants.size,1,background+' colors changed with animation, scroll or layout');
   assert.deepEqual(errors,[]);
   if(background==='dynamic'){
    await page.evaluate(async()=>{const v=document.querySelector('video');v.src=window.clipUrls.dark;await v.play();});await page.waitForTimeout(350);
    assert.equal((await read()).fg,'rgb(245, 247, 250)');
    await page.evaluate(async()=>{const v=document.querySelector('video');v.src=window.clipUrls.alternating;await v.play();});await page.waitForTimeout(350);
    assert.equal((await read()).fg,samples[0].fg,'Earlier video reference must survive source switching');
   }
   await page.close();console.log('PASS',background,'stable for',samples.length,'samples over 62 seconds');
  }));
  for(const theme of ['light','dark'])for(const mode of ['off','normal','real']){
   const page=await browser.newPage({viewport:{width:1280,height:900}});
   await page.addInitScript(({theme,mode})=>{localStorage.setItem('nexbox-theme-mode',theme);localStorage.setItem('framescope.preview.store.settings.json',JSON.stringify({'has-launched':true,'background-mode':'dynamic','dynamic-bg-video':'C:/isolated-foreground.webm','liquid-glass-enabled':mode!=='off','liquid-glass-mode':mode==='off'?'normal':mode,'carousel-enabled':false}));},{theme,mode});
   await page.goto(origin+'/targets');await page.getByRole('button',{name:'编辑 Counter-Strike 2'}).waitFor();
   await page.evaluate(async clips=>{const v=document.querySelector('video');window.clipUrl=URL.createObjectURL(new Blob([new Uint8Array(clips.alternating)],{type:'video/webm'}));v.src=window.clipUrl;await v.play();},clips);
   await page.waitForTimeout(300);
   const read=()=>page.locator('[aria-label="搜索目标"]').evaluate(e=>getComputedStyle(e).color);
   const initial=await read();await page.waitForTimeout(1400);assert.equal(await read(),initial);
   await page.evaluate(async()=>{const m=await import('/src/lib/ui-activity.ts');m.uiActivity.setActive(false);const v=document.querySelector('video');v.pause();v.currentTime=.6;});await page.waitForTimeout(150);
   await page.evaluate(async()=>{const m=await import('/src/lib/ui-activity.ts');m.uiActivity.setActive(true);});await page.waitForTimeout(200);assert.equal(await read(),initial);
   await page.evaluate(async({theme,mode})=>{const m=await import('/src/lib/glass-background.ts');const stop=m.watchGlassBackground({scheme:theme,enabled:mode!=='off',mode:mode==='off'?'normal':mode,transparency:100,blur:1},'dynamic');stop();},{theme,mode});assert.equal(await read(),initial);
   results.push({dynamicMatrix:{theme,mode,initial,after:await read(),hiddenResumeStable:true,watcherRestartStable:true}});await page.close();
  }
  console.log('PASS dynamic theme/material matrix, hidden resume and watcher restart');
 }finally{if(out)await fs.writeFile(out,JSON.stringify(results,null,2));await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
