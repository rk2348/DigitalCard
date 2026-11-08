const fs=require('fs'),path=require('path'),{spawnSync}=require('child_process');
const root=path.resolve(__dirname,'..');
const longVersion=process.argv.includes('--90');
const total=longVersion?90:40;
const dir=path.join(root,longVersion?'deliverables/ORISAMO_90s_Revised':'deliverables/ORISAMO_Animated_AspectFixed');
const work=path.join(dir,'assets');fs.mkdirSync(work,{recursive:true});
const prior=path.join(root,'deliverables/ORISAMO_Intro_Draft/assets');
fs.copyFileSync(path.join(root,'Assets/Introduction/Japanese.ttf'),path.join(work,'font.ttf'));
const frames=path.join(root,'Assets/StreamingAssets/Introduction/Frames/frame-%05d.jpg');
const shotFrames=path.join(prior,'web-demo/frame-%04d.jpg');
function ff(a){const r=spawnSync('ffmpeg',['-hide_banner','-loglevel','error','-y',...a],{cwd:work,encoding:'utf8'});if(r.status!==0)throw Error(r.stderr);}
// Fit inside each layout box without stretching the source; use square pixels.
function fit(w,h){return `scale=${w}:${h}:force_original_aspect_ratio=decrease:force_divisible_by=2,setsar=1,pad=${w}:${h}:(ow-iw)/2:(oh-ih)/2:color=0x101320`;}
let serial=0;
function text(s,x,y,size=36,delay=.25,color='0xffe2a3',pop=false){
 const f=`type-${serial++}.txt`;fs.writeFileSync(path.join(work,f),s);
 const font=pop?`'${size}*(1+0.16*exp(-12*max(0,t-${delay})))'`:size;
 return `drawtext=fontfile=font.ttf:textfile=${f}:fontsize=${font}:fontcolor=${color}:x='${x}':y='${y}+22*exp(-9*max(0,t-${delay}))':alpha='min(1,max(0,(t-${delay})/0.3))':shadowcolor=black@0.65:shadowx=2:shadowy=2`;
}
function type(s,x,y,size,start=.2,interval=.105){
 // Each glyph enters independently; full phrase then remains readable.
 return [...s].map((c,i)=>text(c,x+i*size,y,size,start+i*interval)).join(',');
}
const shots=[
 {d:6,compare:true,start:351},
 {d:3,brand:true},
 {d:3,web:0,title:'カードを、読み取る。',sub:'自分の作品をゲームの世界へ。',tag:'01 / SCAN'},
 {d:5,web:45,title:'この絵が、相棒になる。',sub:'撮って、囲んで、切り抜く。',tag:'02 / CAPTURE'},
 {d:3,web:120,title:'名前をつけたら、',sub:'自分だけの相棒。',tag:'03 / NAME'},
 {d:5,start:316,title:'描いた姿で、召喚。',sub:'色も、表情も、キミの作品のまま。',compareSmall:true},
 {d:8,start:1276,title:'その相棒と、勝負する。',sub:'その一手が、勝負を変える。',battle:true},
 {d:4,start:1486,title:'自分の絵だから、うれしい。',sub:'作品が、思い出になる。'},
 {d:3,closing:true}
];
if(longVersion){
 const durations=[10,5,8,14,8,12,20,7,6];
 shots.forEach((s,i)=>{s.sourceDuration=s.d;s.d=durations[i];});
 shots[6].start=1186;
 shots[7].start=1486;
}
for(const [i,s] of shots.entries()){
 let args=[],filter='';let complex=false;
 if(s.compare){
  complex=true;args=['-f','lavfi','-i',`color=c=0x101320:s=1280x720:r=30:d=${s.d}`,'-loop','1','-i',path.join(prior,'web-demo/photo.png'),'-framerate','15','-start_number',String(s.start),'-i',frames];
  filter="[1:v]scale=460:346[p];[2:v]crop=590:555:0:140,scale=520:490,fps=30[g];[0:v][p]overlay=x='90+12*exp(-3*t)':y=205[a];[a][g]overlay=x='690-12*exp(-3*t)':y=153,"+
  type('キミの絵が、',68,60,48,.18,.15)+','+text('動き出す。',710,62,62,1.6,'0xffe2a3',true)+','+
  text('取り込むイラスト',180,157,23,.4,'white')+','+text('ゲームの中の相棒',827,113,23,1.7,'white')+','+
  text('→',590,340,54,1.3)+','+text('描いた作品を、その姿でバトルへ。','(w-tw)/2',655,29,2.5)+','+
  text('デモ絵による紹介 / ゲーム映像はデモ対戦',68,700,12,0,'0xaaaaaa');
 } else if(s.web!==undefined){
  complex=true;args=['-f','lavfi','-i',`color=c=0x101320:s=1280x720:r=30:d=${s.d}`,'-framerate','15','-start_number',String(s.web),'-i',shotFrames];
  filter="[1:v]scale=290:625,fps=30[p];[0:v][p]overlay=x='900+35*exp(-8*t)':y=42,"+
  text(s.tag,65,110,22,.12)+','+text(s.title,65,236,39,.35,'0xffe2a3',true)+','+text(s.sub,65,310,31,.85,'white')+','+
  text('ORISAMO',65,520,50,1.2)+','+text('WEB / 操作デモ（撮影用データ）',65,660,16,0,'0xaaaaaa');
 } else if(s.brand||s.closing){
  args=['-f','lavfi','-i',`color=c=0x101320:s=1280x720:r=30:d=${s.d}`];
  filter=text('ORISAMO','(w-tw)/2',180,96,.1,'0xf1ca73',true)+','+text('オリジナルサモナー','(w-tw)/2',310,29,.4,'white')+','+
  (s.closing?type('キミの作品が、主役になる。',250,430,30,.55,.06):text('つくって、召喚、バトル。','(w-tw)/2',440,36,.7))+','+
  text(s.closing?'描いた絵で、遊ぼう。':'描いた絵が、自分だけの相棒に。','(w-tw)/2',535,25,1.15,'white');
 } else {
  args=['-framerate','15','-start_number',String(s.start),'-i',frames];
  if(s.compareSmall){complex=true;args.push('-loop','1','-i',path.join(prior,'web-demo/photo.png'));
   filter='[0:v]scale=1066:600,pad=1280:720:107:0:color=0x101320,fps=30[b];[1:v]scale=202:152[p];[b][p]overlay=110:425,'+text('取り込んだ絵',120,403,17,.2,'white')+',';
  }else filter='scale=1066:600,pad=1280:720:107:0:color=0x101320,fps=30,';
  filter+=text(s.title,'(w-tw)/2',612,37,.2,'0xffe2a3',true)+','+text(s.sub,'(w-tw)/2',667,23,.7,'white')+','+text('UNITY / デモ対戦',114,10,14,0,'0xcccccc');
  if(s.battle)filter+=','+text('強','340+30*exp(-10*max(0,t-2))',235,65,2,'0xffd375',true)+','+text('普',600,235,65,2.3,'0xffd375',true)+','+text('弱',860,235,65,2.6,'0xffd375',true);
 }
 // Web first segment begins at frame 0, which is falsy: handle explicitly below.
 if(s.web===0){
  complex=true;args=['-f','lavfi','-i',`color=c=0x101320:s=1280x720:r=30:d=${s.d}`,'-framerate','15','-start_number','0','-i',shotFrames];
  filter="[1:v]scale=290:625,fps=30[p];[0:v][p]overlay=x='900+35*exp(-8*t)':y=42,"+text(s.tag,65,110,22,.12)+','+text(s.title,65,236,39,.35,'0xffe2a3',true)+','+text(s.sub,65,310,31,.85,'white')+','+text('ORISAMO',65,520,50,1.2)+','+text('WEB / 操作デモ（撮影用データ）',65,660,16,0,'0xaaaaaa');
 }
 if(longVersion&&s.web!==undefined){
  filter=filter.replace('[1:v]','[1:v]trim=duration='+s.sourceDuration+',setpts=PTS-STARTPTS,tpad=stop_mode=clone:stop_duration='+s.d+',');
 }
 if(longVersion&&i===7)filter='trim=duration=6,setpts=PTS-STARTPTS,tpad=stop_mode=clone:stop_duration=1,'+filter;
 filter=filter.replace(/scale=(\d+):(\d+)/g,(_,w,h)=>fit(w,h));
 filter+=`,fade=t=in:d=0.15,fade=t=out:st=${s.d-.18}:d=0.18,setsar=1,format=yuv420p`;
 ff([...args,complex?'-filter_complex':'-vf',filter,'-t',String(s.d),'-r','30','-an','-c:v','libx264','-preset','fast','-crf','18','-video_track_timescale','15360',`scene-${i}.mp4`]);
 console.log(`Rendered ${i+1}/${shots.length}`);
}
fs.writeFileSync(path.join(work,'concat.txt'),shots.map((_,i)=>`file 'scene-${i}.mp4'`).join('\n'));
const filename=`ORISAMO_紹介動画_文字アニメーション版${total}秒.mp4`;
ff(['-f','concat','-safe','0','-i','concat.txt','-stream_loop','-1','-i',path.join(prior,'music.wav'),'-vf','setsar=1,format=yuv420p','-c:v','libx264','-preset','fast','-crf','23','-maxrate','2500k','-bufsize','5000k','-profile:v','main','-level:v','3.1','-g','60','-color_range','tv','-colorspace','bt709','-color_trc','bt709','-color_primaries','bt709','-af',`afade=t=out:st=${total-3}:d=3`,'-c:a','aac','-b:a','128k','-t',String(total),'-movflags','+faststart','../'+filename]);
ff(['-i','../'+filename,'-vf',`fps=1/${total/10},scale=426:240,tile=5x2`,'-frames:v','1','../確認用一覧.jpg']);
fs.writeFileSync(path.join(dir,'制作メモ.txt'),'40秒 / 1280×720 / 30fps / H.264 + AAC\n既存のデモ絵で作った試作。冒頭に取り込み画像と動くゲームキャラクターを同時表示。召喚場面にも取り込み画像を添える。\n文字は1文字ずつの出現、拡大からの着地、上昇、フェードを使用。\nユーザーの元絵は未提供。取り込み画像は既存の画面資料由来。Webは既存のローカル操作デモ、QR・カメラ・DBは撮影用代替。Unityは既存デモ録画。音楽はオリジナル合成BGM。\n再制作: node tools/make_animated_intro.cjs\n');
console.log(path.join(dir,filename));
if(longVersion)fs.writeFileSync(path.join(dir,'制作メモ.txt'),'90秒 / 1280×720 / 30fps / H.264 Main + AAC\nゲーム映像と文字アニメーションは通常速度。15fpsのゲーム素材を30fpsで出力。Web操作は通常速度で再収録し、各工程の末尾を静止表示。撮影画像は元画像の縦横比を保持。\n最大映像ビットレート2.5Mbps、2秒ごとのキーフレーム、faststart。\n再制作: node tools/capture_web_demo.cjs → node tools/make_animated_intro.cjs --90\n');
