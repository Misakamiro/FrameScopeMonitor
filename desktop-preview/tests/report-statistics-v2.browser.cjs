const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),http=require('node:http');
const {chromium}=require(require.resolve('playwright',{paths:[process.env.FRAMESCOPE_BROWSER_DEPS||process.cwd()]}));
(async()=>{
 const folder=process.env.FRAMESCOPE_REPORT_FIXTURE;
 assert.ok(folder,'FRAMESCOPE_REPORT_FIXTURE must name the synthetic charts directory');
 const html=await fs.readFile(path.join(folder,'framescope-interactive-report.html'));
 const data=await fs.readFile(path.join(folder,'framescope-interactive-data.js'),'utf8');
 const server=http.createServer((req,res)=>{
  if(req.url.endsWith('framescope-interactive-data.js')){res.setHeader('Content-Type','text/javascript; charset=utf-8');res.end(req.url.startsWith('/other/')?data.replace(/("inputFingerprint"\s*:\s*")([^"]+)/,'$1other-$2'):data);}
  else {res.setHeader('Content-Type','text/html; charset=utf-8');res.end(html);}
 });
 await new Promise(resolve=>server.listen(1462,'127.0.0.1',resolve));
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto('http://127.0.0.1:1462/');await page.locator('#gauges .ring.big b').waitFor();
  const checkbox=page.locator('#fpsFilter');
  await page.evaluate(()=>{window.testGapSafe=(points,times,mask)=>{let previous=null;for(let i=0;i<points.y.length;i++){if(points.y[i]===null){previous=null;continue}const at=times.indexOf(points.t[i]);if(at>=0&&!mask[at])return false;if(previous!==null&&times.some((t,j)=>!mask[j]&&t>previous&&t<points.t[i]))return false;previous=points.t[i]}return true;};});
  assert.equal(await checkbox.isVisible(),true,'The report filter must be visible for version 2');
  assert.equal(await checkbox.isChecked(),true);
  const reference=await page.evaluate(()=>({filtered:window.FRAMESCOPE_DATA.fps.filteredSummary.average,raw:window.FRAMESCOPE_DATA.fps.rawSummary.average}));
  assert.notEqual(reference.raw,reference.filtered);
  assert.equal(Number(await page.locator('#gauges .ring.big b').textContent()),reference.filtered);
  assert.ok(await page.evaluate(()=>{const mask=window.FRAMESCOPE_DATA.fps.included;return mask.some(v=>!v)&&mask.every((v,i)=>v||currentSeries[0].data[i]===null);}),'Excluded points must break the curve');
  assert.ok(await page.evaluate(()=>{const points=getRenderablePoints(currentSeries[0],1000000,visibleTimeRange());return window.testGapSafe(points,window.FRAMESCOPE_DATA.fps.t,window.FRAMESCOPE_DATA.fps.included);}),'Downsampling must not convert excluded null points into zero FPS');
  await page.locator('#readMode').selectOption('trend');await page.waitForTimeout(100);
  assert.ok(await page.evaluate(()=>{const points=getRenderablePoints(currentSeries[0],1000000,visibleTimeRange());return window.testGapSafe(points,window.FRAMESCOPE_DATA.fps.t,window.FRAMESCOPE_DATA.fps.included);}),'Trend rendering must keep excluded samples missing');
  assert.ok(await page.evaluate(()=>{const saved=currentTimes;currentTimes=Array.from({length:10000},(_,i)=>i);const data=currentTimes.map(()=>100);data[1000]=null;const points=getRenderablePoints({key:'gap-probe',name:'gap-probe',data},20,{start:0,end:9999,span:9999,full:{span:9999}});currentTimes=saved;return points.y.some(v=>v===null);}),'A narrow chart must preserve an excluded interval inside a bucket');
  assert.ok(await page.evaluate(()=>{const p=fpsPlotPoints(currentSeries[0],1000000,visibleTimeRange());return p.y.every(v=>v!==null);}), 'Filtered samples must be bridged by solid lines without plotted values');
  assert.ok(await page.evaluate(()=>{const savedTimes=currentTimes,raw=DATA.fps.avg,mask=DATA.fps.included;try{currentTimes=[0,1,2,3,4,5];DATA.fps.avg=[100,900,10,90,null,80];DATA.fps.included=[true,false,false,true,true,true];renderCache.clear();const p=fpsPlotPoints({key:'fps:avg',data:[100,null,null,90,null,80]},1000000,{start:0,end:5,span:5,full:{span:5}});return !p.t.includes(1)&&!p.t.includes(2)&&p.y.includes(null)&&p.t.includes(4);}finally{currentTimes=savedTimes;DATA.fps.avg=raw;DATA.fps.included=mask;renderCache.clear();}}),'Bridge excluded values only; keep genuine missing-data gaps');
  const excludedHover=await page.evaluate(()=>{const idx=DATA.fps.included.findIndex(v=>!v),range=visibleTimeRange(),rect=chartBox.getBoundingClientRect(),{pw}=chartDims();hover({clientX:rect.left+PAD.l+(currentTimes[idx]-range.start)/range.span*pw,clientY:rect.top+PAD.t+20});return tooltip.textContent;});
  assert.ok(excludedHover.includes('此采样已排除'));assert.ok(!excludedHover.includes('平均 FPS：'));
  await checkbox.uncheck();await page.waitForTimeout(100);
  assert.equal(Number(await page.locator('#gauges .ring.big b').textContent()),reference.raw);
  await page.reload();assert.equal(await checkbox.isChecked(),false);
  await page.goto('http://127.0.0.1:1462/other/');assert.equal(await checkbox.isChecked(),true,'Preference must not leak to a different input fingerprint');
  await page.locator('[data-view="perf"]').click();assert.equal(await checkbox.isVisible(),false);
  assert.equal(await page.locator('#fpsFilterInfo').isVisible(),false,'FPS-only statistics must be hidden on other charts');
  assert.deepEqual(errors,[]);
  console.log('PASS: report filter, summaries, solid bridges, genuine gaps, tooltip, reload and per-report isolation');
 }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
})().catch(e=>{console.error(e);process.exitCode=1;});
