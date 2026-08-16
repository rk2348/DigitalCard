// オリサモ カードスキャン用ページ
// スマホのカメラでQRコード(カードID＋シード値のJSON)を読み取り、名前を入力すると、
// その場でJavaScript側でキャラクターのステータスを確定させて表示する。
// Unity(QRScanScene)は起動不要。結果はFirebase Realtime Databaseの /characters に
// 記録として保存するので、将来Unity側で読み込んで使うこともできる。

const firebaseConfig = {
  apiKey: "AIzaSyAQBqecVE538sEoEnB1oJk0-mVCaE2mKL0",
  authDomain: "digitalcard-b825d.firebaseapp.com",
  databaseURL: "https://digitalcard-b825d-default-rtdb.firebaseio.com",
  projectId: "digitalcard-b825d",
  storageBucket: "digitalcard-b825d.firebasestorage.app",
  messagingSenderId: "795229883483",
  appId: "1:795229883483:web:e5e94217986f59b40f037e",
  measurementId: "G-8QF2GGVS2C",
};

// Unity側の CharacterStats.AssignRandomStats(seed) と同等のロジックをJSに移植したもの。
// スマホ側だけでキャラクターを確定できるようにするため、Unity(QRScanScene)は不要になる。
// ※C#のSystem.Randomとは異なる乱数アルゴリズムを使うため、同じseedでも
//   C#側とJS側で計算結果が完全には一致しない点に注意（今はJS側だけで完結させる運用のため問題なし）。

/// シード値から決定的な乱数列を生成する(mulberry32)。同じseedからは常に同じ結果が再現される。
function createSeededRandom(seed) {
  let t = seed >>> 0;
  return function () {
    t += 0x6d2b79f5;
    let r = Math.imul(t ^ (t >>> 15), 1 | t);
    r ^= r + Math.imul(r ^ (r >>> 7), 61 | r);
    return ((r ^ (r >>> 14)) >>> 0) / 4294967296;
  };
}

const ELEMENT_TYPES = ["Fire", "Wind", "Thunder", "Water", "Earth", "Light"];
const SKILL_TYPES = ["PowerBoost", "GuardBoost", "LifeDrain", "Overdrive"];
const SKILL_NAME_MAP = {
  PowerBoost: "疾風の一撃",
  GuardBoost: "俊敏なる守り",
  LifeDrain: "生命吸収",
  Overdrive: "渾身の一打",
};

function buildSkillDescription(skillType, ratio) {
  const percent = Math.round(ratio * 100);
  switch (skillType) {
    case "PowerBoost":
      return `素早さの${percent}%を攻撃力に加算`;
    case "GuardBoost":
      return `素早さの${percent}%を防御力に加算`;
    case "LifeDrain":
      return `与えたダメージの${percent}%を体力に回復`;
    case "Overdrive":
      return `攻撃力の${percent}%分、追加ダメージを与える`;
    default:
      return "";
  }
}

/// seedとキャラクター名から、確定したステータスを生成する。
/// 戻り値の形式はUnity側から返していたレスポンスJSONと同じ(showStatusDisplayでそのまま使える)。
function generateCharacterStats(seed, characterName) {
  const rand = createSeededRandom(seed);
  const nextInt = (min, maxExclusive) => min + Math.floor(rand() * (maxExclusive - min));

  let attack = nextInt(10, 31);
  let defense = nextInt(5, 21);
  let speed = nextInt(5, 21);
  const maxHp = 100;
  const hp = maxHp;

  const element = ELEMENT_TYPES[nextInt(0, ELEMENT_TYPES.length)];

  const skillType = SKILL_TYPES[nextInt(0, SKILL_TYPES.length)];
  const ratio = 0.2 + rand() * 0.3; // 0.2〜0.5
  const skillName = SKILL_NAME_MAP[skillType];
  const skillDescription = buildSkillDescription(skillType, ratio);

  const isMutation = rand() < 0.05; // 突然変異(5%)
  let finalName = characterName;
  if (isMutation) {
    const roll = nextInt(0, 3);
    if (roll === 0) attack = Math.round(attack * 1.5);
    else if (roll === 1) defense = Math.round(defense * 1.5);
    else speed = Math.round(speed * 1.5);
    finalName = "★" + finalName;
  }

  return {
    status: "ok",
    characterName: finalName,
    element,
    attack,
    defense,
    speed,
    hp,
    maxHp,
    isMutation,
    skillName,
    skillDescription,
  };
}

const statusEl = document.getElementById("status");
const readerEl = document.getElementById("reader");
const nameInputSectionEl = document.getElementById("name-input-section");
const scannedCardIdEl = document.getElementById("scanned-card-id");
const characterNameInput = document.getElementById("character-name");
const registerBtn = document.getElementById("register-btn");
const rescanBtn = document.getElementById("rescan-btn");
const statusDisplaySectionEl = document.getElementById("status-display-section");
const revealCardEl = document.getElementById("reveal-card");
const revealFlashEl = document.getElementById("reveal-flash");
const mutationVignetteEl = document.getElementById("mutation-vignette");
const sparkleLayerEl = document.getElementById("sparkle-layer");
const mutationBadgeEl = document.getElementById("mutation-badge");
const elementChipEl = document.getElementById("element-chip");
const elementIconEl = document.getElementById("element-icon");
const resultCharacterNameEl = document.getElementById("result-character-name");
const resultElementEl = document.getElementById("result-element");
const resultAttackEl = document.getElementById("result-attack");
const resultDefenseEl = document.getElementById("result-defense");
const resultSpeedEl = document.getElementById("result-speed");
const resultHpEl = document.getElementById("result-hp");
const barAttackEl = document.getElementById("bar-attack");
const barDefenseEl = document.getElementById("bar-defense");
const barSpeedEl = document.getElementById("bar-speed");
const barHpEl = document.getElementById("bar-hp");
const resultSkillNameEl = document.getElementById("result-skill-name");
const resultSkillDescriptionEl = document.getElementById("result-skill-description");
const nextScanBtn = document.getElementById("next-scan-btn");

let scanner = null;
let isSending = false; // Firebaseへの書き込み〜結果待ちの間（多重送信防止）
let isAwaitingName = false; // QR読み取り済み・名前入力/結果待ち（この間はスキャン結果を無視する）
let scannedCardData = null; // QRから読み取ったカード情報(cardId, seedなど)

// Unity側から返ってくる属性名(英語)を日本語表示に変換するためのマップ
const ELEMENT_LABELS = {
  Fire: "炎",
  Wind: "風",
  Thunder: "雷",
  Water: "水",
  Earth: "土",
  Light: "光",
};

const ELEMENT_ICONS = {
  Fire: "🔥",
  Wind: "🌪️",
  Thunder: "⚡",
  Water: "💧",
  Earth: "🪨",
  Light: "✨",
};

// ステータスバーを何%まで伸ばすかの基準値(突然変異で1.5倍された値でも収まる余裕を持たせている)
const BAR_MAX = { attack: 45, defense: 30, speed: 30, hp: 100 };

let db = null;

function setStatus(text, type = "info") {
  statusEl.textContent = text;
  statusEl.className = "status " + type;
}

/// コンソールにログを出す（画面上には表示しない）。
function logDebug(text) {
  console.log(text);
}

function init() {
  logDebug("ページを読み込みました。");

  try {
    firebase.initializeApp(firebaseConfig);
    db = firebase.database();
    logDebug("Firebaseの初期化に成功しました。");
  } catch (e) {
    setStatus("Firebaseの初期化に失敗しました: " + e.message, "error");
    logDebug("エラー(Firebase初期化): " + e);
    return;
  }

  // セキュアな接続(https、またはlocalhost)でないとカメラAPIが使えないため、先にチェックする
  const isSecure =
    window.isSecureContext ||
    location.protocol === "https:" ||
    location.hostname === "localhost" ||
    location.hostname === "127.0.0.1";

  if (!isSecure) {
    setStatus(
      "この機能はhttps接続でのみ動作します。GitHub PagesのURL(https://...)でアクセスしてください。",
      "error"
    );
    logDebug("エラー: セキュアな接続(https)ではありません。現在のprotocol=" + location.protocol);
    return;
  }

  if (typeof Html5Qrcode === "undefined") {
    setStatus(
      "QRコード読み取りライブラリの読み込みに失敗しました。通信環境を確認して再読み込みしてください。",
      "error"
    );
    logDebug("エラー: Html5Qrcodeが読み込まれていません（CDNからのスクリプト読み込みに失敗した可能性）");
    return;
  }

  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    setStatus("このブラウザはカメラ機能に対応していません。別のブラウザでお試しください。", "error");
    logDebug("エラー: navigator.mediaDevices.getUserMedia が利用できません");
    return;
  }

  setStatus("QRコードをカメラにかざしてください");
  startScanner();
}

function startScanner() {
  if (scanner) {
    logDebug("スキャナーは既に起動済みです。");
    return;
  }

  try {
    scanner = new Html5Qrcode("reader");
  } catch (e) {
    setStatus("QRリーダーの初期化に失敗しました: " + e.message, "error");
    logDebug("エラー(Html5Qrcode初期化): " + e);
    scanner = null;
    return;
  }

  const config = { fps: 10, qrbox: { width: 250, height: 250 } };

  logDebug("カメラの起動を試みます...");

  scanner
    .start({ facingMode: "environment" }, config, onScanSuccess, onScanFailure)
    .then(() => {
      logDebug("カメラの起動に成功しました。");
    })
    .catch((err) => {
      setStatus("カメラを起動できませんでした: " + err, "error");
      logDebug("エラー(カメラ起動): " + err);
      scanner = null; // 失敗したので再度ボタンを押せば再試行できるようにする
    });
}

function onScanFailure() {
  // 1フレームごとに呼ばれるが、単に読み取れていないだけなので何もしない
}

function onScanSuccess(decodedText) {
  // 送信中、または既に読み取り済みで名前入力/結果待ちの間は、続けて読み取っても無視する
  if (isSending || isAwaitingName) return;

  logDebug("QRコードを読み取りました: " + decodedText);

  let cardData;
  try {
    cardData = JSON.parse(decodedText);
  } catch (e) {
    setStatus("読み取れましたが、カードの形式が正しくありません", "error");
    return;
  }

  if (!cardData || !cardData.cardId) {
    setStatus("読み取れましたが、カードの形式が正しくありません", "error");
    return;
  }

  scannedCardData = cardData;
  showNameInput(cardData);
}

/// カード読み取り後、キャラクター名を入力してもらう画面を表示する
function showNameInput(cardData) {
  isAwaitingName = true;

  scannedCardIdEl.textContent = cardData.cardId;
  characterNameInput.value = "";

  readerEl.style.display = "none";
  nameInputSectionEl.style.display = "block";
  setStatus("キャラクターの名前を入力してください");

  // 表示直後に入力欄へフォーカス（スマホだとキーボードが自動で出る場合がある）
  setTimeout(() => characterNameInput.focus(), 100);
}

/// 名前入力画面・ステータス表示画面を閉じて、スキャン待ち状態に戻す
function resetToScanning() {
  isAwaitingName = false;
  scannedCardData = null;

  mutationVignetteEl.classList.remove("active");
  nameInputSectionEl.style.display = "none";
  statusDisplaySectionEl.style.display = "none";
  readerEl.style.display = "block";
  setStatus("QRコードをカメラにかざしてください");
}

/// Unityから返ってきたキャラクターステータスを、カード開封の演出とともに表示する
function showStatusDisplay(stats) {
  // 表示前に演出用の状態を全リセット(2回目以降のスキャンでも正しく再生されるように)
  revealCardEl.classList.remove("flipped", "mutation", "show", "shine", "impact");
  revealFlashEl.classList.remove("fire");
  mutationVignetteEl.classList.remove("active");
  sparkleLayerEl.innerHTML = "";
  [barAttackEl, barDefenseEl, barSpeedEl, barHpEl].forEach((el) => {
    el.style.width = "0%";
    el.classList.remove("pulse");
  });

  mutationBadgeEl.style.display = stats.isMutation ? "block" : "none";
  resultCharacterNameEl.textContent = stats.characterName;
  resultElementEl.textContent = ELEMENT_LABELS[stats.element] || stats.element;
  elementIconEl.textContent = ELEMENT_ICONS[stats.element] || "";
  resultAttackEl.textContent = stats.attack;
  resultDefenseEl.textContent = stats.defense;
  resultSpeedEl.textContent = stats.speed;
  resultHpEl.textContent = stats.hp + " / " + stats.maxHp;
  resultSkillNameEl.textContent = "スキル「" + stats.skillName + "」";
  resultSkillDescriptionEl.textContent = stats.skillDescription;

  nameInputSectionEl.style.display = "none";
  statusDisplaySectionEl.style.display = "block";
  setStatus("封を開いています...");

  if (stats.isMutation) {
    revealCardEl.classList.add("mutation");
    mutationVignetteEl.classList.add("active");
  }

  // 1. フラッシュ + カードが弾けるように登場
  requestAnimationFrame(() => {
    revealFlashEl.classList.add("fire");
    revealCardEl.classList.add("show");
  });

  // 2. カードをめくる
  setTimeout(() => {
    revealCardEl.classList.add("flipped");

    // 3. めくり切ったところで衝撃・光の帯・バーの演出
    setTimeout(() => {
      revealCardEl.classList.add("impact", "shine");

      barAttackEl.style.width = Math.min(100, (stats.attack / BAR_MAX.attack) * 100) + "%";
      barDefenseEl.style.width = Math.min(100, (stats.defense / BAR_MAX.defense) * 100) + "%";
      barSpeedEl.style.width = Math.min(100, (stats.speed / BAR_MAX.speed) * 100) + "%";
      barHpEl.style.width = Math.min(100, (stats.hp / BAR_MAX.hp) * 100) + "%";

      spawnSparkles(stats.isMutation ? 18 : 8);

      setTimeout(() => {
        [barAttackEl, barDefenseEl, barSpeedEl, barHpEl].forEach((el) => el.classList.add("pulse"));
        setStatus(stats.characterName + " が誕生した！", "success");
      }, 700);
    }, 450);
  }, 550);
}

/// カードめくりの瞬間に、キラキラした演出用の要素を数個ランダムな位置に散らす
function spawnSparkles(count) {
  const glyphs = ["✦", "✧", "★"];
  for (let i = 0; i < count; i++) {
    const el = document.createElement("span");
    el.className = "sparkle";
    el.textContent = glyphs[Math.floor(Math.random() * glyphs.length)];
    el.style.left = Math.random() * 100 + "%";
    el.style.top = Math.random() * 100 + "%";
    el.style.animationDelay = Math.random() * 400 + "ms";
    sparkleLayerEl.appendChild(el);
  }
}

registerBtn.addEventListener("click", () => {
  const name = characterNameInput.value.trim();
  if (!name) {
    setStatus("名前を入力してください", "error");
    characterNameInput.focus();
    return;
  }

  if (isSending) return;
  isSending = true;
  registerBtn.disabled = true;
  setStatus("登録中...");

  const stats = generateCharacterStats(scannedCardData.seed, name);

  // 記録として保存しておく(将来Unity側で読み込んで使う場合などに利用できる)。
  // 保存に失敗しても、スマホ側の表示自体は続行してよい。
  db.ref("characters")
    .push({
      cardId: scannedCardData.cardId,
      seed: scannedCardData.seed,
      timestamp: Date.now(),
      ...stats,
    })
    .catch((e) => {
      logDebug("エラー(Firebase保存、表示は続行します): " + e);
    });

  showStatusDisplay(stats);
  isSending = false;
  registerBtn.disabled = false;
});

rescanBtn.addEventListener("click", () => {
  resetToScanning();
});

nextScanBtn.addEventListener("click", () => {
  resetToScanning();
});

init();
