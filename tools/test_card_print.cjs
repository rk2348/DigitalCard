// Offline browser integration test; Firebase is stubbed, no records or print jobs are sent.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const root = path.resolve(__dirname, '..'), out = path.join(root, 'Temp/card-print-test');
fs.mkdirSync(out, { recursive: true });
const base = 'file:///' + root.replaceAll('\\', '/') + '/docs/';
let html = fs.readFileSync(path.join(root, 'docs/index.html'), 'utf8')
  .replace(/<script[\s\S]*?<\/script>/g, '').replace(/<link[^>]*href="https:[^>]*>/g, '')
  .replace('<head>', `<head><base href="${base}">`);
const app = fs.readFileSync(path.join(root, 'docs/app.js'), 'utf8').replace(/init\(\);\s*$/, '');
html = html.replace('</body>', `<script src="vendor/qrcode.js"></script><script src="card-print.js"></script><script>${app}</script><script>
window.saved=[];window.failSave=false;
db={ref:p=>({set:async r=>{if(window.failSave)throw Error('offline');saved.push(r)},push:async()=>{},off(){},remove:async()=>{}})};
startPhotoCamera=()=>{};loadSegmenter=()=>{};window.print=()=>window.printCalls=(window.printCalls||0)+1;
</script></body>`);
fs.writeFileSync(path.join(out, 'fixture.html'), html);
const child = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless=new', '--disable-gpu', '--no-first-run', '--allow-file-access-from-files', '--disable-background-networking', '--host-resolver-rules=MAP * ~NOTFOUND', `--user-data-dir=${path.join(out, 'profile')}`, '--remote-debugging-port=9243', 'about:blank'], { windowsHide: true, stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
(async () => {
 const timeout = setTimeout(() => { console.error('Browser test timed out'); child.kill(); process.exit(1); }, 25000);
 let ws;
 try {
  let tabs;
  for(let i=0;i<60;i++){try{tabs=await(await fetch('http://127.0.0.1:9243/json')).json();break}catch{await sleep(100)}}
  assert.ok(tabs, 'browser started');
  ws=new WebSocket(tabs.find(t=>t.type==='page').webSocketDebuggerUrl);await new Promise(r=>ws.addEventListener('open',r,{once:true}));
  let id=0;const pending=new Map();ws.addEventListener('message',e=>{const m=JSON.parse(e.data);if(pending.has(m.id)){const p=pending.get(m.id);pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result)}});
  const cmd=(method,params={})=>new Promise((resolve,reject)=>{const key=++id;pending.set(key,{resolve,reject});ws.send(JSON.stringify({id:key,method,params}))});
  const ev=async expression=>{const r=await cmd('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value};
  await cmd('Page.enable');await cmd('Emulation.setDeviceMetricsOverride',{width:520,height:900,deviceScaleFactor:1,mobile:false});
  await cmd('Page.navigate',{url:'file:///'+path.join(out,'fixture.html').replaceAll('\\','/')});await sleep(700);
  assert.equal(await ev(`startCreateMode(); /^[a-f0-9]{32}$/.test(scannedCardData.cardId) && photoCaptureSectionEl.style.display==='block'`),true);
  await ev(`capturedCutoutDataUrl='data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="300" height="400"><path fill="orange" stroke="black" stroke-width="8" d="M150 20 L260 170 L230 350 L70 350 L40 170Z"/><circle cx="110" cy="160" r="15"/><circle cx="190" cy="160" r="15"/></svg>'); showNameInput(scannedCardData); characterNameInput.value='テスト召喚獣'; failSave=true; registerBtn.click()`);await sleep(100);
  assert.equal(await ev(`statusDisplaySectionEl.style.display==='none' && !registerBtn.disabled`),true,'failed save stays editable');
  await ev(`failSave=false;registerBtn.click()`);await sleep(100);
  assert.equal(await ev(`saved.length===1 && statusDisplaySectionEl.style.display==='block'`),true);
  await ev(`CardPrint.open(document.querySelector('.reveal-card-front'))`);
  assert.equal(await ev(`!document.querySelector('.print-actions button').disabled`),true);
  const size=await ev(`(()=>{const r=document.querySelector('.print-card').getBoundingClientRect();return [r.width,r.height]})()`);
  assert.ok(size[0] > 300 && Math.abs(size[0]/size[1]-63/88)<0.001,'responsive preview preserves ratio');
  assert.equal(await ev(`getComputedStyle(document.querySelector('.print-card .overlay-hp')).opacity`),'1');
  assert.equal(await ev(`getComputedStyle(document.querySelector('.print-card .card-character-cutout')).opacity`),'1');
  fs.writeFileSync(path.join(out,'expected.json'), await ev(`JSON.stringify({cardId:saved[0].cardId,seed:saved[0].seed,dataVersion:1})`));
  await ev(`document.querySelector('.print-actions button').click()`);assert.equal(await ev('window.printCalls'),1);
  await cmd('Emulation.setEmulatedMedia',{media:'print'});
  assert.equal(await ev(`getComputedStyle(document.querySelector('.container')).display`),'none');
  await cmd('Emulation.setDeviceMetricsOverride',{width:820,height:1140,deviceScaleFactor:1,mobile:false});
  const printSize=await ev(`(()=>{const r=document.querySelector('.print-card').getBoundingClientRect();return [r.width,r.height]})()`);
  assert.ok(Math.abs(printSize[0]-200*96/25.4)<0.1 && Math.abs(printSize[1]-(200*88/63)*96/25.4)<0.1,'large A4 print size');
  const shot=await cmd('Page.captureScreenshot');fs.writeFileSync(path.join(out,'print.png'),Buffer.from(shot.data,'base64'));
  const pdf=await cmd('Page.printToPDF',{preferCSSPageSize:true,printBackground:true});fs.writeFileSync(path.join(out,'card.pdf'),Buffer.from(pdf.data,'base64'));
  assert.equal((Buffer.from(pdf.data,'base64').toString('latin1').match(/\/Type\s*\/Page\b/g)||[]).length,1,'A4 output fits on one page');
  await ev(`document.querySelector('dialog').close()`);
  assert.equal(await ev(`document.body.classList.contains('card-printing')`),false);
  await ev(`startCreateMode();showNameInput(scannedCardData);characterNameInput.value='2枚目';registerBtn.click()`);await sleep(100);
  assert.equal(await ev(`saved.length===2 && saved[0].cardId!==saved[1].cardId`),true);
  const stored = await ev(`(()=>{const r={...saved[0]};const c=document.createElement('canvas');c.width=200;c.height=200;const x=c.getContext('2d');x.fillStyle='orange';x.beginPath();x.arc(100,100,80,0,Math.PI*2);x.fill();r.photoDataUrl=c.toDataURL();r.skillDescription='</script><script>window.injected=true</script>';return r})()`);
  const template = fs.readFileSync(path.join(root,'Assets/StreamingAssets/CardPrinting/template.html'),'utf8');
  const safeJson = JSON.stringify(stored).replaceAll('<','\\u003c').replaceAll('>','\\u003e').replaceAll('&','\\u0026');
  const artwork = 'data:image/png;base64,' + fs.readFileSync(path.join(root,'docs/cards/card-'+stored.element.toLowerCase()+'.png')).toString('base64');
  fs.writeFileSync(path.join(out,'gallery.html'),template.replace('__CARD_ART__',artwork).replace('/*CARD_RECORD*/null',safeJson));
  await cmd('Emulation.setEmulatedMedia',{media:''});
  await cmd('Page.navigate',{url:'file:///'+path.join(out,'gallery.html').replaceAll('\\','/')});await sleep(500);
  assert.equal(await ev(`!!document.querySelector('dialog[open]') && !document.querySelector('.print-actions button').disabled`),true,'offline gallery preview ready');
  assert.equal(await ev(`document.querySelector('.print-card .result-character-name').textContent`),stored.characterName);
  assert.equal(await ev(`document.querySelector('.print-card .overlay-hp').textContent`),String(stored.hp));
  assert.equal(await ev(`document.querySelector('.print-card .card-character-cutout').naturalWidth`),200);
  assert.equal(await ev(`typeof window.injected`),'undefined','stored text cannot run scripts');
  await cmd('Emulation.setEmulatedMedia',{media:'print'});
  assert.ok(Math.abs(await ev(`document.querySelector('.print-card').getBoundingClientRect().width`)-200*96/25.4)<0.1,'gallery also uses large A4 size');
  const galleryShot=await cmd('Page.captureScreenshot');fs.writeFileSync(path.join(out,'gallery-print.png'),Buffer.from(galleryShot.data,'base64'));
  await cmd('Emulation.setEmulatedMedia',{media:''});
  await ev(`document.querySelector('dialog').close()`);await sleep(100);
  await ev(`document.getElementById('open-print').click()`);await sleep(100);
  assert.equal(await ev(`!!document.querySelector('dialog[open]')`),true,'gallery preview reopens');
  console.log('PASS: offline gallery preview, stored art and stats, escaped text, reopen.');
  console.log('PASS: create without QR, failed save retry, saved card printing, physical size, print UI isolation, close and second card.');
 } finally { clearTimeout(timeout); if(ws)ws.close();child.kill(); }
})().catch(e=>{console.error(e);process.exitCode=1});
