const fs = require('fs');
const path = require('path');
const {spawnSync} = require('child_process');
const root = path.resolve(__dirname, '..');
const out = path.join(root, 'deliverables/ORISAMO_Intro_Draft');
const work = path.join(out, 'assets');
fs.mkdirSync(work, {recursive:true});
const useWebMotion=fs.existsSync(path.join(work,'web-demo/capture-result.json'));
const movie=useWebMotion?'ORISAMO_紹介動画_Web操作版40秒.mp4':'ORISAMO_紹介動画_試作40秒.mp4';
fs.copyFileSync(path.join(root,'Assets/Introduction/Japanese.ttf'),path.join(work,'font.ttf'));
function ff(args) {
  const r=spawnSync('ffmpeg',['-hide_banner','-loglevel','error','-y',...args],{cwd:work,encoding:'utf8'});
  if(r.status!==0) throw new Error(r.stderr || `ffmpeg ${r.status}`);
}
let serial=0;
function text(s,x,y,size,color='white') {
  const f=`text-${serial++}.txt`; fs.writeFileSync(path.join(work,f),s);
  return `drawtext=fontfile=font.ttf:textfile=${f}:fontsize=${size}:fontcolor=${color}:x=${x}:y=${y}:shadowcolor=black@0.6:shadowx=2:shadowy=2`;
}
const frames=path.join(root,'Assets/StreamingAssets/Introduction/Frames/frame-%05d.jpg');
const web=n=>path.join(root,`Assets/StreamingAssets/Introduction/web-${n}.png`);
const shots=[
 {d:5,web:'selection',title:'キミの絵が、',sub:'相棒になる。',tag:'DRAW YOUR OWN HERO',small:'描いた作品を取り込んで、自分だけのキャラクターに。'},
 {d:4,start:1,title:'オリジナルサモナー',sub:'つくって、召喚、バトル。',brand:true},
 {d:4,web:'home',title:'01  カードを読み取る',sub:'冒険のはじまりは、QRから。',tag:'CREATE',small:'スマートフォンでキャラクターを登録。'},
 {d:4,web:'selection',title:'02  撮って、切り抜く',sub:'作品の姿を、そのまま相棒に。',tag:'MAKE IT YOURS',small:'指で囲んで、イラストを取り込む。'},
 {d:3,web:'name',title:'03  名前をつける',sub:'自分だけの相棒が、誕生。',tag:'YOUR PARTNER',small:'名前を決めたら、対戦の舞台へ。'},
 {d:3,start:316,title:'自分だけの相棒を、召喚。',sub:'カードから、アリーナへ。'},
 {d:5,start:481,title:'その一手が、勝負を変える。',sub:'攻撃？ 防御？ 相手の選択を読もう。'},
 {d:5,start:1126,title:'強・普・弱。選ぶのは、キミ。',sub:'最後まで、勝負はわからない。'},
 {d:4,start:1486,title:'キミの作品が、主役になる。',sub:'ORISAMO  /  オリジナルサモナー',end:true},
 {d:3,title:'キミの作品が、主役になる。',sub:'オリジナルサモナー',closing:true}
];
// 40 seconds; source recording is 15 fps, output is a broadly compatible 30 fps MP4.
for(const [i,s] of shots.entries()) {
 let args=[],filter='';
 if(s.web) {
  args=['-f','lavfi','-i',`color=c=0x101320:s=1280x720:r=30:d=${s.d}`];
  if(useWebMotion && i>=2 && i<=4)args.push('-framerate','15','-start_number',String((i-2)*60),'-i',path.join(work,'web-demo/frame-%04d.jpg'));
  else args.push('-loop','1','-i',web(s.web));
  filter=`[1:v]scale=280:603[p];[0:v][p]overlay=890:48,drawbox=x=68:y=130:w=65:h=4:color=0xf1ca73:t=fill,`+
   text(s.tag,68,85,20,'0xf1ca73')+','+text(s.title,68,210,39,'0xffe1a0')+','+text(s.sub,68,292,31)+','+text(s.small,68,376,21)+','+
   text('ORISAMO',68,520,54,'0xf1ca73')+','+text(useWebMotion&&i>=2?'WEB / 操作デモ（撮影用データ）':'WEB / 登録画面の表示例',68,650,17,'0xaaaaaa');
 } else if(s.closing) {
  args=['-f','lavfi','-i',`color=c=0x101320:s=1280x720:r=30:d=${s.d}`];
  filter=text('ORISAMO','(w-tw)/2',160,94,'0xf1ca73')+','+text(s.sub,'(w-tw)/2',282,28)+',drawbox=x=500:y=350:w=280:h=2:color=0xf1ca73:t=fill,'+text(s.title,'(w-tw)/2',402,43,'0xffe1a0')+','+text('つくって、召喚、バトル。','(w-tw)/2',500,26);
 } else {
  args=['-framerate','15','-start_number',String(s.start),'-i',frames];
  filter='scale=1066:600,pad=1280:720:107:0:color=0x101320,fps=30,'+
    text(s.title,'(w-tw)/2',615,s.end?38:35,'0xffe1a0')+','+text(s.sub,'(w-tw)/2',669,22)+','+
    text('UNITY / デモ対戦',112,9,15,'0xdddddd');
 }
 filter+=`,fade=t=in:st=0:d=0.2,fade=t=out:st=${s.d-0.2}:d=0.2,format=yuv420p`;
 ff([...args, s.web?'-filter_complex':'-vf',filter,'-t',String(s.d),'-an','-c:v','libx264','-preset','fast','-crf','19',`shot-${i}.mp4`]);
 console.log(`Rendered ${i+1}/${shots.length}`);
}
// Original instrumental bed: soft arpeggios, chord pad, bass and a restrained beat.
const sr=44100,duration=40,pcm=Buffer.alloc(sr*duration*4);
const chords=[[48,52,55,59],[45,48,52,55],[41,45,48,52],[43,47,50,55]];
const hz=n=>440*Math.pow(2,(n-69)/12);
for(let i=0;i<sr*duration;i++) {
 const t=i/sr, beat=t*2, step=Math.floor(beat*2),ch=chords[Math.floor(t/4)%4];
 const q=(beat*2)%1, note=ch[[0,1,2,3,2,1,3,2][step%8]]+12;
 let v=.105*Math.sin(2*Math.PI*hz(note)*t)*Math.exp(-q*5);
 for(const n of ch)v+=.018*Math.sin(2*Math.PI*hz(n)*t);
 v+=.065*Math.sin(2*Math.PI*hz(ch[0]-12)*t)*Math.exp(-(beat%1)*3);
 if(t>8 && t<37){const b=beat%1;v+=.10*Math.sin(2*Math.PI*(48*b-16*b*b))*Math.exp(-b*18);v+=.012*Math.sin(2*Math.PI*7200*t)*Math.exp(-q*25);}
 v*=Math.min(1,t/1.5,(40-t)/2.5);
 for(let c=0;c<2;c++)pcm.writeInt16LE(Math.max(-32767,Math.min(32767,Math.round(v*32767))),i*4+c*2);
}
const header=Buffer.alloc(44);header.write('RIFF');header.writeUInt32LE(36+pcm.length,4);header.write('WAVEfmt ',8);header.writeUInt32LE(16,16);header.writeUInt16LE(1,20);header.writeUInt16LE(2,22);header.writeUInt32LE(sr,24);header.writeUInt32LE(sr*4,28);header.writeUInt16LE(4,32);header.writeUInt16LE(16,34);header.write('data',36);header.writeUInt32LE(pcm.length,40);
fs.writeFileSync(path.join(work,'music.wav'),Buffer.concat([header,pcm]));
fs.writeFileSync(path.join(work,'concat.txt'),shots.map((_,i)=>`file 'shot-${i}.mp4'`).join('\n'));
ff(['-f','concat','-safe','0','-i','concat.txt','-i','music.wav','-c:v','copy','-c:a','aac','-b:a','192k','-t','40','-movflags','+faststart','../'+movie]);
ff(['-i','../'+movie,'-vf','fps=1/4,scale=426:240,tile=5x2','-frames:v','1','../確認用一覧.jpg']);
fs.writeFileSync(path.join(out,'制作メモ.txt'),'40秒 / 1280×720 / 30fps / H.264 + AAC\n日本語テロップ・オリジナル合成BGM。ナレーションなし。\n'+(useWebMotion?'Webは実際のHTML・CSS・アプリのクリック／囲み／入力処理をヘッドレスChromeで収録。カメラ・QR入力・DBはローカル撮影用の代替データ。タップ位置を合成表示。本番データへの登録なし。冒頭は静止画。':'Webは既存の登録画面の静止画表示例。')+'Unityは既存のローカルデモ対戦の録画素材を編集。\n再制作: node tools/make_intro_draft.cjs\n');
console.log('Complete: '+out);
