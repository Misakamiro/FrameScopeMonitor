const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const {chromium} = require(require.resolve('playwright',{paths:[process.env.FRAMESCOPE_BROWSER_DEPS || process.cwd()]}));

(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 const results=[];
 try{
  for(const dpr of [1,1.25,1.5,2])for(const navPosition of ['left','top']){
   const page=await browser.newPage({viewport:{width:1280,height:900},deviceScaleFactor:dpr});
   await page.addInitScript(navPosition=>{
    localStorage.setItem('nexbox-theme-mode','dark');
    localStorage.setItem('framescope.preview.store.settings.json',JSON.stringify({'has-launched':true,'background-mode':'preset','active-preset-index':0,'liquid-glass-enabled':true,'liquid-glass-mode':'normal','carousel-enabled':false,'jelly-bounce-enabled':true,'nexbox_nav_position':navPosition}));
   },navPosition);
   await page.goto(process.env.PREVIEW_URL || 'http://127.0.0.1:1456/');
   await page.locator('#main-sidebar').waitFor();await page.waitForTimeout(900);
   for(const trigger of ['route','labels']){
    const samples=await page.evaluate(trigger=>{
     if(trigger==='route'){
      document.body.classList.remove('jelly-bounce-active');void document.body.offsetWidth;document.body.classList.add('jelly-bounce-active');
     }else{
      document.body.classList.remove('jelly-bounce-active');
      window.dispatchEvent(new CustomEvent('sidebar-show-label-changed',{detail:true}));
     }
     return trigger;
    },trigger);
    if(trigger==='labels')await page.waitForTimeout(50);
    const icons=await page.evaluate(()=>{
     for(const a of document.getAnimations()){if(a.animationName?.startsWith('jellyBounce')){a.pause();a.currentTime=110;}}
     return [...document.querySelectorAll('#main-sidebar svg.lucide')].map(icon=>{
      const scaled=[];
      for(let e=icon;e;e=e.parentElement){const c=getComputedStyle(e);if(c.transform!=='none'){const m=new DOMMatrixReadOnly(c.transform);if(Math.abs(m.a-1)>.001||Math.abs(m.d-1)>.001)scaled.push({className:e.className,scale:m.a});}}
      return {icon:icon.getAttribute('class'),scaled};
     });
    });
    for(const icon of icons)assert.equal(icon.scaled.length,1,`${trigger} dpr=${dpr}: ${icon.icon} is scaled more than once: ${JSON.stringify(icon.scaled)}`);
    results.push({dpr,navPosition,trigger:samples,icons});
    await page.evaluate(()=>document.getAnimations().forEach(a=>a.finish()));
    await page.waitForTimeout(750);
    const settled=await page.locator('#main-sidebar svg.lucide').first().boundingBox();
    assert.equal(settled.width,20);assert.equal(settled.height,20);
   }
   await page.close();
  }
 }finally{await browser.close();if(process.argv[2])await fs.writeFile(process.argv[2],JSON.stringify(results,null,2));}
 console.log(`PASS ${results.length} navigation animation cases; one scale, settled SVG 20x20`);
})().catch(e=>{console.error(e);process.exitCode=1;});
