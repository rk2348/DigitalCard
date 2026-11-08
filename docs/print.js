// オリサモ カード印刷用ページ
// Firebase の /characterByCard に登録されたカードを一覧にして、選んだカードをまとめて印刷する。
// ・新しく登録されたカードは自動で一覧の先頭に追加される(自動で選ぶこともできる)
// ・印刷の大きさは A4に9枚(カードサイズ) / 4枚 / 1枚 から選べる
// ・印刷したカードは「印刷済み」として、このブラウザに記録する(localStorage)

const firebaseConfig = {
  apiKey: "AIzaSyAQBqecVE538sEoEnB1oJk0-mVCaE2mKL0",
  authDomain: "digitalcard-b825d.firebaseapp.com",
  databaseURL: "https://digitalcard-b825d-default-rtdb.firebaseio.com",
  projectId: "digitalcard-b825d",
  storageBucket: "digitalcard-b825d.firebasestorage.app",
  messagingSenderId: "795229883483",
  appId: "1:795229883483:web:e5e94217986f59b40f037e",
};

const ELEMENT_CARD_IMAGES = {
  Fire: "cards/card-fire.png",
  Wind: "cards/card-wind.png",
  Dark: "cards/card-dark.png",
  Water: "cards/card-water.png",
  Earth: "cards/card-earth.png",
  Light: "cards/card-light.png",
};
const PRINTED_KEY = "orisamo.print.printed";

const statusEl = document.getElementById("ps-status");
const gridEl = document.getElementById("ps-grid");
const emptyEl = document.getElementById("ps-empty");
const printBtn = document.getElementById("ps-print");
const searchEl = document.getElementById("ps-search");
const filterEl = document.getElementById("ps-filter");
const layoutEl = document.getElementById("ps-layout");
const autoSelectEl = document.getElementById("ps-autoselect");
const toastEl = document.getElementById("ps-toast");
const printAreaEl = document.getElementById("print-area");

const cards = new Map();     // cardId -> record
const tiles = new Map();     // cardId -> 一覧のタイル要素
const selected = new Set();  // 選んでいるcardId
let printed = loadPrinted(); // cardId -> 印刷した時刻
let initialLoadDone = false;

// 設定(表示・大きさ・自動選択)はこのブラウザに覚えておく
try {
  const saved = JSON.parse(localStorage.getItem("orisamo.print.settings") || "{}");
  if (saved.filter) filterEl.value = saved.filter;
  if (saved.layout) layoutEl.value = saved.layout;
  if (typeof saved.autoSelect === "boolean") autoSelectEl.checked = saved.autoSelect;
} catch (e) { /* 保存できない環境では毎回初期値 */ }
function saveSettings() {
  try {
    localStorage.setItem("orisamo.print.settings", JSON.stringify({ filter: filterEl.value, layout: layoutEl.value, autoSelect: autoSelectEl.checked }));
  } catch (e) { /* ignore */ }
}

function loadPrinted() {
  try { return JSON.parse(localStorage.getItem(PRINTED_KEY) || "{}"); } catch (e) { return {}; }
}
function savePrinted() {
  try { localStorage.setItem(PRINTED_KEY, JSON.stringify(printed)); } catch (e) { /* ignore */ }
}

// ==================== カード1枚の表面を組み立てる ====================
// スマホのページ(index.html)の完成カードと同じ部品・同じクラス名で作るので、見た目も同じになる。
function buildCard(rec) {
  const card = document.createElement("div");
  card.className = "print-card";
  const front = document.createElement("div");
  front.className = "reveal-card-face reveal-card-front";
  const artSrc = ELEMENT_CARD_IMAGES[rec.element] || ELEMENT_CARD_IMAGES.Fire;

  const art = img("card-art", artSrc);
  front.append(art);
  if (rec.photoDataUrl) front.append(img("card-character-cutout", rec.photoDataUrl));
  front.append(img("card-art-layer card-art-stats", artSrc), img("card-art-layer card-art-emblem", artSrc));

  if (rec.isMutation) front.append(text("p", "mutation-badge", "★ 突然変異 ★"));
  const defense = document.createElement("span");
  defense.className = "overlay-defense";
  defense.append("🛡 ", text("span", "", String(rec.defense ?? "")));
  front.append(defense);
  front.append(text("h2", "result-character-name", rec.characterName || ""));
  front.append(text("span", "overlay-value overlay-hp", String(rec.hp ?? rec.maxHp ?? 100)));
  front.append(text("span", "overlay-value overlay-atk", String(rec.attack ?? "")));
  front.append(text("span", "overlay-value overlay-spd", String(rec.speed ?? "")));
  const skill = document.createElement("div");
  skill.className = "overlay-skill";
  skill.append(text("p", "skill-name", rec.skillName ? `スキル「${rec.skillName}」` : ""), text("p", "skill-description", rec.skillDescription || ""));
  front.append(skill);

  // 対戦用QRコード(カード画像右下のQR枠にぴったり重なる)
  if (rec.cardId && rec.seed != null && typeof qrcode !== "undefined") {
    const qr = qrcode(0, "M");
    qr.addData(JSON.stringify({ cardId: rec.cardId, seed: rec.seed, dataVersion: 1 }));
    qr.make();
    const code = document.createElement("div");
    code.className = "card-qr";
    code.innerHTML = qr.createSvgTag({ cellSize: 4, margin: 16, scalable: true });
    front.append(code);
  }
  card.append(front);
  return card;
}
function img(className, src) {
  const el = document.createElement("img");
  el.className = className;
  el.alt = "";
  el.decoding = "async";
  el.src = src;
  return el;
}
function text(tag, className, value) {
  const el = document.createElement(tag);
  if (className) el.className = className;
  el.textContent = value;
  return el;
}

// ==================== 一覧 ====================
function addOrUpdate(cardId, rec, isNew) {
  if (!rec || typeof rec !== "object") return;
  rec.cardId = rec.cardId || cardId;
  cards.set(cardId, rec);
  const old = tiles.get(cardId);
  const tile = document.createElement("div");
  tile.className = "ps-tile";
  tile.dataset.id = cardId;
  tile.append(buildCard(rec));
  const info = document.createElement("div");
  info.className = "ps-info";
  info.append(text("span", "ps-name", rec.characterName || "(名前なし)"), text("span", "ps-time", formatTime(rec.timestamp)));
  tile.append(info);
  tile.append(text("span", "ps-badge", "印刷済み"));
  tile.append(text("span", "ps-check-mark", "✓"));
  tile.addEventListener("click", () => toggle(cardId));
  if (old) old.replaceWith(tile); else gridEl.prepend(tile);
  tiles.set(cardId, tile);
  if (isNew && initialLoadDone) {
    showToast(`新しいカード：${rec.characterName || ""}`);
    if (autoSelectEl.checked && !printed[cardId]) selected.add(cardId);
  }
  scheduleRefresh();
}

function remove(cardId) {
  cards.delete(cardId);
  selected.delete(cardId);
  const tile = tiles.get(cardId);
  if (tile) tile.remove();
  tiles.delete(cardId);
  scheduleRefresh();
}

let refreshQueued = false;
function scheduleRefresh() {
  if (refreshQueued) return;
  refreshQueued = true;
  requestAnimationFrame(() => { refreshQueued = false; refresh(); });
}

/// 並び順(新しい順)・絞り込み・選択状態・印刷ボタンの表示をまとめて更新する
function refresh() {
  const query = searchEl.value.trim().toLowerCase();
  const filter = filterEl.value;
  const ordered = [...cards.entries()].sort((a, b) => (b[1].timestamp || 0) - (a[1].timestamp || 0));
  let visible = 0;
  for (const [id, rec] of ordered) {
    const tile = tiles.get(id);
    if (!tile) continue;
    gridEl.append(tile); // 新しい順に並べ直す
    const isPrinted = !!printed[id];
    const matches = (!query || (rec.characterName || "").toLowerCase().includes(query)) &&
      (filter === "all" || (filter === "printed" ? isPrinted : !isPrinted));
    tile.style.display = matches ? "" : "none";
    tile.classList.toggle("is-printed", isPrinted);
    tile.classList.toggle("is-selected", selected.has(id));
    if (matches) visible++;
  }
  emptyEl.style.display = initialLoadDone && visible === 0 ? "" : "none";
  const perSheet = Number(layoutEl.value);
  const sheets = Math.ceil(selected.size / perSheet);
  printBtn.disabled = selected.size === 0;
  printBtn.textContent = selected.size ? `選んだ ${selected.size} 枚を印刷（A4 ${sheets} 枚）` : "印刷するカードを選んでください";
  const printedCount = [...cards.keys()].filter((id) => printed[id]).length;
  if (initialLoadDone) statusEl.textContent = `登録カード ${cards.size} 枚（未印刷 ${cards.size - printedCount} 枚）・新しいカードは自動で追加されます`;
}

function toggle(cardId) {
  if (selected.has(cardId)) selected.delete(cardId); else selected.add(cardId);
  scheduleRefresh();
}

function formatTime(ms) {
  if (!ms) return "";
  const d = new Date(ms);
  return `${d.getMonth() + 1}/${d.getDate()} ${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
}

let toastTimer = null;
function showToast(message) {
  toastEl.textContent = message;
  toastEl.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toastEl.classList.remove("show"), 3500);
}

searchEl.addEventListener("input", scheduleRefresh);
filterEl.addEventListener("change", () => { saveSettings(); scheduleRefresh(); });
layoutEl.addEventListener("change", () => { saveSettings(); scheduleRefresh(); });
autoSelectEl.addEventListener("change", saveSettings);
document.getElementById("ps-select-visible").addEventListener("click", () => {
  for (const [id, tile] of tiles) if (tile.style.display !== "none") selected.add(id);
  scheduleRefresh();
});
document.getElementById("ps-clear").addEventListener("click", () => { selected.clear(); scheduleRefresh(); });

// ==================== 印刷 ====================
printBtn.addEventListener("click", async () => {
  if (!selected.size) return;
  const perSheet = Number(layoutEl.value);
  const ids = [...selected].filter((id) => cards.has(id))
    .sort((a, b) => (cards.get(a).timestamp || 0) - (cards.get(b).timestamp || 0)); // 古い順(登録順)に並べる
  printAreaEl.innerHTML = "";
  printAreaEl.dataset.layout = String(perSheet);
  for (let i = 0; i < ids.length; i += perSheet) {
    const sheet = document.createElement("div");
    sheet.className = "print-sheet";
    for (const id of ids.slice(i, i + perSheet)) {
      const slot = document.createElement("div");
      slot.className = "print-slot";
      slot.append(buildCard(cards.get(id)));
      sheet.append(slot);
    }
    printAreaEl.append(sheet);
  }
  printBtn.disabled = true;
  printBtn.textContent = "印刷の準備をしています…";
  // 画像とフォントが揃ってから印刷画面を開く(途中で印刷されて絵が欠けないように)
  await Promise.all([...printAreaEl.querySelectorAll("img")].map((el) => el.decode().catch(() => {})));
  await document.fonts.ready;
  const markPrinted = () => {
    const now = Date.now();
    for (const id of ids) printed[id] = now;
    savePrinted();
    selected.clear();
    showToast(`${ids.length} 枚を印刷済みにしました`);
    scheduleRefresh();
  };
  window.addEventListener("afterprint", markPrinted, { once: true });
  window.print();
});

// 印刷済みの取り消し(印刷に失敗した時など): 「印刷したカード」表示で選んで押す
document.addEventListener("keydown", (e) => {
  if (e.key === "Delete" && filterEl.value === "printed" && selected.size) {
    for (const id of selected) delete printed[id];
    savePrinted();
    selected.clear();
    showToast("印刷済みの記録を取り消しました");
    scheduleRefresh();
  }
});

// ==================== Firebase ====================
firebase.initializeApp(firebaseConfig);
firebase.auth().signInAnonymously().catch((e) => {
  statusEl.textContent = "接続できませんでした。ページを読み込み直してください（" + e.message + "）";
}).then(() => {
  const ref = firebase.database().ref("characterByCard");
  statusEl.textContent = "カードを読み込んでいます…";
  // 最初に全部読み込む(その時点のカードは「新しいカード」扱いにしない)
  ref.once("value").then((snap) => {
    snap.forEach((child) => { addOrUpdate(child.key, child.val(), false); });
    initialLoadDone = true;
    scheduleRefresh();
    // 以降の追加・変更・削除をリアルタイムで反映する
    ref.on("child_added", (child) => { if (!cards.has(child.key)) addOrUpdate(child.key, child.val(), true); });
    ref.on("child_changed", (child) => addOrUpdate(child.key, child.val(), false));
    ref.on("child_removed", (child) => remove(child.key));
  }).catch((e) => {
    statusEl.textContent = "カードを読み込めませんでした（" + e.message + "）";
  });
});
