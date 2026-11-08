const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const root = path.resolve(__dirname, '..');
const source = path.join(root, 'deliverables/ORISAMO_Animated_AspectFixed/assets');
const out = path.join(root, 'deliverables/ORISAMO_Animated_90s_AspectFixed');
const assets = path.join(out, 'assets');
fs.mkdirSync(assets, { recursive: true });
const shots = [
  [6, 10, '絵とゲーム内キャラクターの比較'],
  [3, 5, 'タイトル'],
  [3, 8, 'カードを読み取る'],
  [5, 14, '撮影・切り抜き'],
  [3, 8, '相棒に名前をつける'],
  [5, 12, '描いた姿で召喚'],
  [8, 20, 'バトル'],
  [4, 7, '勝利'],
  [3, 6, 'エンディング'],
];
function ff(args) {
  const result = spawnSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', ...args], { cwd: assets, encoding: 'utf8' });
  if (result.status !== 0) throw new Error(result.stderr || String(result.error));
}
for (const [index, [original, duration]] of shots.entries()) {
  ff(['-i', path.join(source, `scene-${index}.mp4`), '-vf',
    `setpts=${duration / original}*(PTS-STARTPTS),fps=30,tpad=stop_mode=clone:stop_duration=1`,
    '-t', String(duration), '-an', '-c:v', 'libx264', '-preset', 'fast', '-crf', '18',
    '-pix_fmt', 'yuv420p', '-video_track_timescale', '15360', `scene-${index}.mp4`]);
  console.log(`Rendered ${index + 1}/${shots.length}`);
}
fs.writeFileSync(path.join(assets, 'concat.txt'), shots.map((_, i) => `file 'scene-${i}.mp4'`).join('\n'));
const filename = 'ORISAMO_紹介動画_文字アニメーション版90秒.mp4';
ff(['-f', 'concat', '-safe', '0', '-i', 'concat.txt', '-stream_loop', '-1', '-i',
  path.join(root, 'deliverables/ORISAMO_Intro_Draft/assets/music.wav'),
  '-map', '0:v:0', '-map', '1:a:0', '-c:v', 'copy', '-af', 'afade=t=out:st=87:d=3',
  '-c:a', 'aac', '-b:a', '192k', '-t', '90', '-movflags', '+faststart', path.join(out, filename)]);
ff(['-reinit_filter', '0', '-i', path.join(out, filename), '-vf', 'fps=1/9,scale=426:240,tile=5x2', '-frames:v', '1', path.join(out, '確認用一覧.jpg')]);
let elapsed = 0;
const timeline = shots.map(([, duration, label]) => {
  const line = `${elapsed}〜${elapsed + duration}秒: ${label}`;
  elapsed += duration;
  return line;
});
fs.writeFileSync(path.join(out, '制作メモ.txt'), [
  '90秒 / 1280×720 / 30fps / H.264 + AAC',
  '40秒版の各シーンをリタイミング。映像・文字アニメーションをゆっくり見せる構成。',
  '既存の合成BGMを繰り返し、最後の3秒でフェードアウト。',
  ...timeline, '再制作: node tools/make_animated_intro_90.cjs',
].join('\n'));
console.log(path.join(out, filename));
