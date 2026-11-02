const assert=require('node:assert/strict'),path=require('node:path'),fs=require('node:fs');
const {chromium}=require(require.resolve('playwright',{paths:[process.env.FRAMESCOPE_BROWSER_DEPS||process.cwd()]}));
(async()=>{
 const root=path.resolve(__dirname,'..');process.chdir(root);
 const {createServer}=await import(require('node:url').pathToFileURL(require.resolve('vite',{paths:[root]})).href);
 const server=await createServer({root,plugins:[{name:'installer-native-fixture',enforce:'pre',transform(code,id){
  if(!id.endsWith('/src/App.tsx'))return;
  if(process.env.FRAMESCOPE_INSTALLER_BASELINE_APP)code=fs.readFileSync(process.env.FRAMESCOPE_INSTALLER_BASELINE_APP,'utf8');
  for(const name of ['@tauri-apps/api/core','@tauri-apps/api/event','@tauri-apps/api/window','@tauri-apps/plugin-dialog'])code=code.replaceAll(JSON.stringify(name),'"/tests/safety-api-fixture.ts"');
  return code;
 }}],server:{host:'127.0.0.1',port:1462,strictPort:true,hmr:false,watch:null}});
 await server.listen();const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage();await page.goto(server.resolvedUrls.local[0]);
  const input=page.locator('input[type="text"],input:not([type])').first();
  await input.waitFor();await page.waitForFunction(()=>document.querySelector('input')?.value==='C:\\Program Files\\FrameScope');
  for(const [picked,expected] of [['D:\\','D:\\FrameScope'],['D:\\Games','D:\\Games\\FrameScope'],['D:\\FrameScope','D:\\FrameScope']]){
   await page.evaluate(value=>window.installerProbe.pick(value),picked);
   await page.getByRole('button',{name:'浏览',exact:true}).click();
   assert.equal(await input.inputValue(),expected,'directory picker must select a dedicated product folder');
  }
  console.log('PASS: drive, shared parent and existing product folder select a dedicated destination');
 }finally{await browser.close();await server.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
