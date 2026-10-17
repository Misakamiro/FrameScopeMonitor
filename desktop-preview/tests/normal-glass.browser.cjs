const assert=require('node:assert/strict');const fs=require('node:fs/promises');const path=require('node:path');const sharp=require('sharp');const {chromium}=require('playwright');
(async()=>{
 const out=path.resolve(process.argv[2]);await fs.mkdir(out,{recursive:true});const {createServer}=await import('vite');const server=await createServer({root:path.resolve(__dirname,'..'),server:{host:'127.0.0.1',port:1453,strictPort:true,watch:null,hmr:false}});await server.listen();const browser=await chromium.launch({headless:true,channel:'msedge'});const results=[];
 try{
  for(const route of ['/','/hardware','/targets','/reports','/settings']){
   const pixels=[],states=[];
   for(const blur of [1,100]){
    const context=await browser.newContext({viewport:{width:1280,height:900}});const page=await context.newPage();
    await page.addInitScript(blur=>{localStorage.setItem('nexbox_page_transition','slide');localStorage.setItem('framescope.preview.store.settings.json',JSON.stringify({'has-launched':true,'background-mode':'preset','active-preset-index':0,'carousel-enabled':false,'liquid-glass-enabled':true,'liquid-glass-mode':'normal','liquid-glass-blur':blur,'jelly-bounce-enabled':false,nexbox_page_transition:'slide'}))},blur);
    await page.goto(server.resolvedUrls.local[0]+route.slice(1));await page.locator('#main-sidebar').waitFor();await page.waitForTimeout(900);
    if(route==='/settings'){await page.getByText('外观',{exact:true}).click();await page.waitForTimeout(200)}
    await page.evaluate(()=>{const svg='<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="900"><defs><pattern id="p" width="8" height="8" patternUnits="userSpaceOnUse"><rect width="8" height="8" fill="white"/><rect width="4" height="8" fill="black"/></pattern></defs><rect width="1280" height="900" fill="url(#p)"/></svg>';for(const image of document.querySelectorAll('img[src*="backgrounds"]'))image.src='data:image/svg+xml,'+encodeURIComponent(svg)});await page.waitForTimeout(120);
    const card=page.locator('.jelly-bounce-card:visible').first();const rect=await card.boundingBox();const clip={x:rect.x+rect.width*.5,y:rect.y+rect.height*.5,width:Math.min(120,rect.width*.3),height:Math.min(20,rect.height*.2)};
    const buffer=await page.screenshot({clip});await fs.writeFile(path.join(out,route.replaceAll('/','')||'home')+'-'+blur+'.png',buffer);pixels.push(await sharp(buffer).removeAlpha().raw().toBuffer());states.push(await card.evaluate(e=>({filter:getComputedStyle(e,'::before').backdropFilter,bg:getComputedStyle(e).backgroundColor})));
    if(route==='/settings'){
     const slider=page.locator('[role="slider"][aria-valuemax="100"]');await slider.press('Home');await slider.press('End');assert.equal(await slider.getAttribute('aria-valuenow'),'100');await page.waitForTimeout(600);assert.equal(await card.evaluate(e=>getComputedStyle(e,'::before').backdropFilter),'blur(100px) saturate(1.3)');
     const thumb=await slider.boundingBox(),track=await slider.locator('..').boundingBox();await page.mouse.move(thumb.x+thumb.width/2,thumb.y+thumb.height/2);await page.mouse.down();await page.mouse.move(track.x+track.width*.25,thumb.y+thumb.height/2,{steps:8});await page.mouse.up();await page.waitForTimeout(600);const dragged=Number(await slider.getAttribute('aria-valuenow'));assert.ok(dragged>=20&&dragged<=35,'Pointer drag must update blur');assert.equal(await card.evaluate(e=>getComputedStyle(e,'::before').backdropFilter),`blur(${dragged}px) saturate(1.3)`);assert.equal(await page.evaluate(()=>JSON.parse(localStorage.getItem('framescope.preview.store.settings.json'))['liquid-glass-blur']),dragged);
    }
    await context.close();
   }
   const difference=pixels[0].reduce((sum,v,i)=>sum+Math.abs(v-pixels[1][i]),0)/pixels[0].length;results.push({route,passed:difference>3,difference,states});
  }
  console.log(JSON.stringify(results,null,2));await fs.writeFile(path.join(out,'results.json'),JSON.stringify(results,null,2));assert.ok(results.every(r=>r.passed),'Blur must visibly change the page cards, not only their computed CSS');
 }finally{await browser.close();await server.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
