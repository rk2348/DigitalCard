// Capture the real local HTML/CSS/app event handlers with offline camera/database fixtures.
const fs=require('fs'),path=require('path'),{spawn}=require('child_process');
const root=path.resolve(__dirname,'..'),dir=path.join(root,'deliverables/ORISAMO_Intro_Draft/assets/web-demo');
fs.mkdirSync(dir,{recursive:true});
let html=fs.readFileSync(path.join(root,'docs/index.html'),'utf8').replace(/<script[\s\S]*?<\/script>/g,'').replace(/<link[^>]*href="https:[^>]*>/g,'');
html=html.replace('<head>',`<head><base href="file:///${root.replaceAll('\\','/')}/docs/">`);
const app=fs.readFileSync(path.join(root,'docs/app.js'),'utf8').replace(/init\(\);\s*$/,'');
const harness=`
const fixtureImage=new Image();fixtureImage.src='file:///${dir.replaceAll('\\','/')}/photo.png';
function drawFixture(canvas){
 canvas.width=420;canvas.height=Math.round(420/(photoFrameEl.clientWidth/photoFrameEl.clientHeight||.75));
 const ctx=canvas.getContext('2d');ctx.fillStyle='#e9e5dc';ctx.fillRect(0,0,canvas.width,canvas.height);
 const scale=Math.min(canvas.width/fixtureImage.naturalWidth,canvas.height/fixtureImage.naturalHeight);
 const w=fixtureImage.naturalWidth*scale,h=fixtureImage.naturalHeight*scale;
 ctx.drawImage(fixtureImage,(canvas.width-w)/2,(canvas.height-h)/2,w,h);return ctx;
}
const mockRef={set:async()=>{},push:async()=>{},once:async()=>({val:()=>null}),off(){},on(){},remove:async()=>{}};
db={ref:()=>mockRef};displayCheckUnavailable=true;
startScanner=function(){readerEl.innerHTML='<div style="height:320px;display:grid;place-items:center;background:#15121f;border:2px solid #e2ba65;border-radius:16px"><div style="text-align:center"><div style="font-size:64px;color:#e2ba65">▣</div><p>カードのQRコードを<br>枠内にかざしてください</p><div style="width:200px;height:2px;background:#e2ba65;box-shadow:0 0 15px #e2ba65;margin:30px"></div></div></div>';};
startPhotoCamera=function(){
 Object.defineProperty(photoVideoEl,'videoWidth',{configurable:true,value:420});
 photoVideoEl.style.display='none';cutoutPreviewCanvasEl.width=420;cutoutPreviewCanvasEl.height=560;
 drawFixture(cutoutPreviewCanvasEl);
 cutoutPreviewCanvasEl.style.display='block';setStatus('キャラクターを撮影してください');
};
captureVisibleFrame=drawFixture;
loadSegmenter=async()=>null;removeBackgroundWithAi=async()=>false;
goHome();
window.demoScan=()=>onScanSuccess(JSON.stringify({cardId:'ORISAMO-001',seed:74219,version:1}));
const dot=document.createElement('div');dot.id='demo-touch';dot.style.cssText='position:fixed;width:26px;height:26px;border:3px solid white;border-radius:50%;background:#efc76b66;box-shadow:0 0 0 7px #efc76b22;z-index:99999;pointer-events:none;left:-100px;top:-100px';document.body.appendChild(dot);
window.placeTouch=(x,y,press=false)=>{dot.style.left=(x-13)+'px';dot.style.top=(y-13)+'px';dot.style.background=press?'#efc76baa':'#efc76b44';};
`;
html=html.replace('</body>',`<style>@font-face{font-family:LocalJP;src:url('file:///${root.replaceAll('\\','/')}/Assets/Introduction/Japanese.ttf')}body,input,button{font-family:LocalJP,sans-serif!important}.brand{margin-bottom:14px}.container{padding-top:14px}.photo-instruction{font-size:13px}.photo-frame{aspect-ratio:3/4}.stage-dust{display:none}*{scrollbar-width:none}</style><script>${app}</script><script>${harness}</script></body>`);
fs.writeFileSync(path.join(dir,'demo.html'),html);
const chrome='C:/Program Files/Google/Chrome/Application/chrome.exe';
const proc=spawn(chrome,['--headless=new','--disable-gpu','--no-first-run','--disable-background-networking','--disable-extensions','--disable-sync','--allow-file-access-from-files','--host-resolver-rules=MAP * ~NOTFOUND',`--user-data-dir=${path.join(dir,'profile')}`,'--remote-debugging-port=9237','about:blank'],{windowsHide:true,stdio:'ignore'});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
(async()=>{
 let tabs;for(let n=0;n<50;n++){try{tabs=await(await fetch('http://127.0.0.1:9237/json')).json();break;}catch{await sleep(100);}}
 if(!tabs)throw Error('Headless browser did not start');
 const ws=new WebSocket(tabs.find(t=>t.type==='page').webSocketDebuggerUrl);await new Promise(r=>ws.addEventListener('open',r,{once:true}));
 let id=0;const pending=new Map();ws.addEventListener('message',e=>{const m=JSON.parse(e.data);if(m.id&&pending.has(m.id)){const p=pending.get(m.id);pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result);}});
 const cmd=(method,params={})=>new Promise((resolve,reject)=>{const key=++id;pending.set(key,{resolve,reject});ws.send(JSON.stringify({id:key,method,params}));});
 const ev=async expression=>{const r=await cmd('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value;};
 await cmd('Page.enable');await cmd('Emulation.setDeviceMetricsOverride',{width:520,height:1120,deviceScaleFactor:1,mobile:false});
 await cmd('Page.navigate',{url:'file:///'+path.join(dir,'demo.html').replaceAll('\\','/')});await sleep(900);
 await ev('fixtureImage.decode()');
 const point=sel=>ev(`(()=>{const r=document.querySelector(${JSON.stringify(sel)}).getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
 const move=async(x,y,down=false)=>{await ev(`placeTouch(${x},${y},${down})`);await cmd('Input.dispatchMouseEvent',{type:'mouseMoved',x,y,buttons:down?1:0});};
 const click=async sel=>{const p=await point(sel);await move(p.x,p.y,true);await cmd('Input.dispatchMouseEvent',{type:'mousePressed',x:p.x,y:p.y,button:'left',clickCount:1});await cmd('Input.dispatchMouseEvent',{type:'mouseReleased',x:p.x,y:p.y,button:'left',clickCount:1});};
 let ring;
 for(let f=0;f<165;f++){
  if(f===0){const p=await point('#create-character-btn');await move(p.x+60,p.y+100);}
  if(f===12)await click('#create-character-btn');
  if(f===32){await ev('demoScan()');await sleep(80);}
  if(f===45)await click('#capture-photo-btn');
  if(f===50){ring=await ev(`(()=>{const r=selectionCanvas.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2,rx:r.width*.44,ry:r.height*.46}})()`);const x=ring.x,y=ring.y-ring.ry;await move(x,y,true);await cmd('Input.dispatchMouseEvent',{type:'mousePressed',x,y,button:'left',clickCount:1});}
  if(f>50&&f<=91){const a=(f-50)/41*Math.PI*2-Math.PI/2;await move(ring.x+ring.rx*Math.cos(a),ring.y+ring.ry*Math.sin(a),true);}
  if(f===92){await cmd('Input.dispatchMouseEvent',{type:'mouseReleased',x:ring.x,y:ring.y-ring.ry,button:'left',clickCount:1});}
  if(f===99){await click('#auto-cutout-btn');await sleep(100);}
  if(f===115)await click('#use-photo-btn');
  if(f===120)await click('#character-name');
  if(f>=122&&f<=134&&f%2===0)await cmd('Input.insertText',{text:[...'ホムラドラゴン'][(f-122)/2]||''});
  if(f===146)await click('#register-btn');
  if(f===160)await ev("revealCardEl.classList.add('show','flipped')");
  await sleep(8);
  const shot=await cmd('Page.captureScreenshot',{format:'jpeg',quality:90,captureBeyondViewport:false});
  fs.writeFileSync(path.join(dir,`frame-${String(f).padStart(4,'0')}.jpg`),Buffer.from(shot.data,'base64'));
  if(f%30===0)console.log('Web capture '+f+'/165');
 }
 const state=await ev("({name:characterNameInput.value,registered:resultCharacterNameEl.textContent,photo:!!capturedCutoutDataUrl,selection:selectionPoints.length})");
 fs.writeFileSync(path.join(dir,'capture-result.json'),JSON.stringify(state,null,2));console.log(state);
 await cmd('Browser.close');ws.close();
})().catch(e=>{console.error(e);proc.kill();process.exitCode=1;});
