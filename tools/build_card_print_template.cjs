// Bundle the existing web print layout for offline use from Unity's gallery.
const fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const root = path.resolve(__dirname, '..');
const source = path.join(root, 'docs'), output = path.join(root, 'Assets/StreamingAssets/CardPrinting');
fs.mkdirSync(output, { recursive: true });
const read = name => fs.readFileSync(path.join(source, name), 'utf8');
const front = read('index.html').match(/<div class="reveal-card-face reveal-card-front">([\s\S]*?)\n            <\/div>/)[0];
const html = `<!doctype html><html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>登録カードの印刷</title>
<style>${read('style.css')}</style></head><body>
<main class="container"><h1>登録カードの印刷</h1><p id="card-name"></p><button id="open-print">カードを見る・印刷</button><p>A4用紙いっぱいにカードを大きく印刷します。A4・縦向き・倍率100%にしてください。このタブを閉じると一覧に戻れます。</p></main>
<div hidden id="card-source">${front}</div>
<script>${read('vendor/qrcode.js')}</script><script>${read('card-print.js')}</script>
<script>
const record = /*CARD_RECORD*/null;
const art = "__CARD_ART__";
const text = (id, value) => document.getElementById(id).textContent = value == null ? '' : value;
text('card-name', record.characterName);
text('result-character-name', record.characterName);
text('result-hp', record.hp || record.maxHp || 0);
text('result-attack', record.attack);
text('result-defense', record.defense);
text('result-speed', record.speed);
text('result-skill-name', 'スキル「' + (record.skillName || '') + '」');
text('result-skill-description', record.skillDescription);
document.getElementById('mutation-badge').style.display = record.isMutation ? 'block' : 'none';
document.querySelectorAll('.card-art, .card-art-layer').forEach(img => img.src = art);
const photo = document.getElementById('card-character-cutout');
if (/^data:image\\/(png|jpeg|webp);base64,/.test(record.photoDataUrl || '')) { photo.src = record.photoDataUrl; photo.style.display = 'block'; }
CardPrint.setCard(record);
document.getElementById('open-print').onclick = () => CardPrint.open(document.querySelector('.reveal-card-front'));
document.getElementById('open-print').click();
</script></body></html>`;
fs.writeFileSync(path.join(output, 'template.html'), html);
for (const name of fs.readdirSync(path.join(source, 'cards'))) {
  if (/^card-.*\.png$/.test(name)) fs.copyFileSync(path.join(source, 'cards', name), path.join(output, name));
}
for (const entry of [output, ...fs.readdirSync(output).filter(n => !n.endsWith('.meta')).map(n => path.join(output, n))]) {
  if (!fs.existsSync(entry + '.meta')) fs.writeFileSync(entry + '.meta', `fileFormatVersion: 2\nguid: ${crypto.randomUUID().replaceAll('-', '')}\n${fs.statSync(entry).isDirectory() ? 'folderAsset: yes\n' : ''}DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`);
}
console.log('Bundled offline gallery print template and card artwork.');
