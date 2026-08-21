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

const ELEMENT_TYPES = ["Fire", "Wind", "Dark", "Water", "Earth", "Light"];
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
    skillType,
    ratio,
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
const photoCaptureSectionEl = document.getElementById("photo-capture-section");
const photoCardIdEl = document.getElementById("photo-card-id");
const photoVideoEl = document.getElementById("photo-video");
const photoCaptureCanvasEl = document.getElementById("photo-capture-canvas");
const cutoutPreviewCanvasEl = document.getElementById("cutout-preview-canvas");
const capturePhotoBtn = document.getElementById("capture-photo-btn");
const retakePhotoBtn = document.getElementById("retake-photo-btn");
const usePhotoBtn = document.getElementById("use-photo-btn");
const skipPhotoBtn = document.getElementById("skip-photo-btn");
const cardCharacterCutoutEl = document.getElementById("card-character-cutout");
const revealCardEl = document.getElementById("reveal-card");
const revealFlashEl = document.getElementById("reveal-flash");
const shockwaveRingEl = document.getElementById("shockwave-ring");
const flashBulbsEl = document.getElementById("flash-bulbs");
const mutationVignetteEl = document.getElementById("mutation-vignette");
const sparkleLayerEl = document.getElementById("sparkle-layer");
const cardArtEl = document.getElementById("card-art");
const mutationBadgeEl = document.getElementById("mutation-badge");
const resultCharacterNameEl = document.getElementById("result-character-name");
const resultAttackEl = document.getElementById("result-attack");
const resultDefenseEl = document.getElementById("result-defense");
const resultSpeedEl = document.getElementById("result-speed");
const resultHpEl = document.getElementById("result-hp");
const resultSkillNameEl = document.getElementById("result-skill-name");
const resultSkillDescriptionEl = document.getElementById("result-skill-description");
const nextScanBtn = document.getElementById("next-scan-btn");

let scanner = null;
let isSending = false; // Firebaseへの書き込み〜結果待ちの間（多重送信防止）
let isAwaitingName = false; // QR読み取り済み・名前入力/結果待ち（この間はスキャン結果を無視する）
let scannedCardData = null; // QRから読み取ったカード情報(cardId, seedなど)
let photoStream = null; // 実物撮影用のカメラストリーム(MediaStream)
let capturedCutoutDataUrl = null; // 背景切り抜き後のキャラクター写真(PNG, data URL)。未撮影ならnull

// Unity側から返ってくる属性名(英語)を日本語表示に変換するためのマップ
// 実際のカード(闇・火・光・水・地・風)に合わせてある。Thunder(雷)は実カードに存在しないため
// Dark(闇)に対応させている。
const ELEMENT_LABELS = {
  Fire: "火",
  Wind: "風",
  Dark: "闇",
  Water: "水",
  Earth: "地",
  Light: "光",
};

// 属性ごとの実カード画像ファイル名
const ELEMENT_CARD_IMAGES = {
  Fire: "cards/card-fire.png",
  Wind: "cards/card-wind.png",
  Dark: "cards/card-dark.png",
  Water: "cards/card-water.png",
  Earth: "cards/card-earth.png",
  Light: "cards/card-light.png",
};

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
  capturedCutoutDataUrl = null;
  showPhotoCapture(cardData);
}

/// カード読み取り直後、実物を撮影して背景を切り抜く画面を表示する
function showPhotoCapture(cardData) {
  isAwaitingName = true;

  photoCardIdEl.textContent = cardData.cardId;

  // 表示状態を初期化(2回目以降のスキャンでも正しく表示されるように)
  photoVideoEl.style.display = "block";
  cutoutPreviewCanvasEl.style.display = "none";
  capturePhotoBtn.style.display = "inline-block";
  retakePhotoBtn.style.display = "none";
  usePhotoBtn.style.display = "none";

  readerEl.style.display = "none";
  nameInputSectionEl.style.display = "none";
  photoCaptureSectionEl.style.display = "block";
  setStatus("背景がなるべく無地になるようにキャラクターを置いて撮影してください");

  startPhotoCamera();
}

/// 撮影用のカメラ(通常のgetUserMedia)を起動する。
/// QRスキャン用のHtml5Qrcodeとは別に、videoタグへ直接映像を流し込む。
function startPhotoCamera() {
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    setStatus("このブラウザは撮影に対応していません。「写真なしで進める」を押してください", "error");
    return;
  }

  navigator.mediaDevices
    .getUserMedia({ video: { facingMode: "environment" }, audio: false })
    .then((stream) => {
      photoStream = stream;
      photoVideoEl.srcObject = stream;
    })
    .catch((e) => {
      logDebug("エラー(撮影用カメラ起動): " + e);
      setStatus("カメラを起動できませんでした。「写真なしで進める」を押してください", "error");
    });
}

/// 撮影用のカメラストリームを停止する(名前入力画面やスキャン待ちに戻る際に呼ぶ)。
function stopPhotoCamera() {
  if (photoStream) {
    photoStream.getTracks().forEach((track) => track.stop());
    photoStream = null;
  }
  photoVideoEl.srcObject = null;
}

/// カード読み取り後、キャラクター名を入力してもらう画面を表示する
function showNameInput(cardData) {
  isAwaitingName = true;

  scannedCardIdEl.textContent = cardData.cardId;
  characterNameInput.value = "";

  photoCaptureSectionEl.style.display = "none";
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
  capturedCutoutDataUrl = null;

  stopPhotoCamera();

  mutationVignetteEl.classList.remove("active");
  photoCaptureSectionEl.style.display = "none";
  nameInputSectionEl.style.display = "none";
  statusDisplaySectionEl.style.display = "none";
  readerEl.style.display = "block";
  setStatus("QRコードをカメラにかざしてください");
}

/// Unityから返ってきたキャラクターステータスを、カード開封のステージ演出とともに表示する
function showStatusDisplay(stats) {
  // 表示前に演出用の状態を全リセット(2回目以降のスキャンでも正しく再生されるように)
  revealCardEl.classList.remove("flipped", "mutation", "show", "shine", "impact", "reveal-pop");
  revealFlashEl.classList.remove("fire");
  shockwaveRingEl.classList.remove("pulse");
  mutationVignetteEl.classList.remove("active");
  sparkleLayerEl.innerHTML = "";
  flashBulbsEl.innerHTML = "";

  cardArtEl.src = ELEMENT_CARD_IMAGES[stats.element] || "";

  if (capturedCutoutDataUrl) {
    cardCharacterCutoutEl.src = capturedCutoutDataUrl;
    cardCharacterCutoutEl.style.display = "block";
  } else {
    cardCharacterCutoutEl.src = "";
    cardCharacterCutoutEl.style.display = "none";
  }

  mutationBadgeEl.style.display = stats.isMutation ? "block" : "none";
  resultCharacterNameEl.textContent = stats.characterName;
  resultAttackEl.textContent = stats.attack;
  resultDefenseEl.textContent = stats.defense;
  resultSpeedEl.textContent = stats.speed;
  resultHpEl.textContent = stats.hp;
  resultSkillNameEl.textContent = "スキル「" + stats.skillName + "」";
  resultSkillDescriptionEl.textContent = stats.skillDescription;

  nameInputSectionEl.style.display = "none";
  statusDisplaySectionEl.style.display = "block";
  setStatus("登録しています...");

  if (stats.isMutation) {
    revealCardEl.classList.add("mutation");
    mutationVignetteEl.classList.add("active");
  }

  // 1. カードが上から舞い降りてくる
  requestAnimationFrame(() => {
    revealCardEl.classList.add("show");
  });

  // 2. 着地の瞬間：フラッシュ + 衝撃波 + カメラのフラッシュが焚かれる
  setTimeout(() => {
    revealFlashEl.classList.add("fire");
    shockwaveRingEl.classList.add("pulse");
    spawnFlashBulbs(stats.isMutation ? 6 : 3);
  }, 720);

  // 3. カードをめくって正体を明かす
  setTimeout(() => {
    revealCardEl.classList.add("flipped");

    // 4. めくり切ったところで衝撃・光の帯・文字の焼き付き演出
    setTimeout(() => {
      revealCardEl.classList.add("impact", "shine", "reveal-pop");
      spawnSparkles(stats.isMutation ? 20 : 9);

      setTimeout(() => {
        setStatus(stats.characterName + " を登録しました！", "success");
      }, 700);
    }, 450);
  }, 950);
}

/// 着地の瞬間、カメラのフラッシュのような光を数回ランダムな位置で焚く
function spawnFlashBulbs(count) {
  for (let i = 0; i < count; i++) {
    const el = document.createElement("span");
    el.className = "flash-bulb";
    el.style.left = 20 + Math.random() * 60 + "%";
    el.style.top = Math.random() * 40 + "%";
    el.style.animationDelay = Math.random() * 250 + "ms";
    flashBulbsEl.appendChild(el);
  }
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

/// 撮影した写真の背景を切り抜く(単色〜比較的シンプルな背景を想定した簡易版)。
/// 画像の外周(四辺)から連結している「背景色に近い領域」だけを透明化するバケツ塗りつぶし方式。
/// カード内部に背景と似た色があっても、外周とつながっていなければ消えないため、
/// 本格的なAIセグメンテーションではないが、無地に近い背景であれば実用的な精度が出る。
function removeBackground(ctx, width, height, tolerance = 42) {
  const imageData = ctx.getImageData(0, 0, width, height);
  const data = imageData.data;

  // 背景色の推定: 四隅+各辺の中点をサンプリングして平均を取る
  const samplePoints = [
    [0, 0],
    [width - 1, 0],
    [0, height - 1],
    [width - 1, height - 1],
    [Math.floor(width / 2), 0],
    [Math.floor(width / 2), height - 1],
    [0, Math.floor(height / 2)],
    [width - 1, Math.floor(height / 2)],
  ];
  let sr = 0,
    sg = 0,
    sb = 0;
  for (const [x, y] of samplePoints) {
    const i = (y * width + x) * 4;
    sr += data[i];
    sg += data[i + 1];
    sb += data[i + 2];
  }
  const bg = [sr / samplePoints.length, sg / samplePoints.length, sb / samplePoints.length];

  const idx = (x, y) => y * width + x;
  const colorDistToBg = (i) => {
    const p = i * 4;
    const dr = data[p] - bg[0];
    const dg = data[p + 1] - bg[1];
    const db = data[p + 2] - bg[2];
    return Math.sqrt(dr * dr + dg * dg + db * db);
  };

  const visited = new Uint8Array(width * height);
  const removed = new Uint8Array(width * height); // フェザリング用に「透明化した画素」を記録
  const stack = [];

  for (let x = 0; x < width; x++) {
    stack.push([x, 0]);
    stack.push([x, height - 1]);
  }
  for (let y = 0; y < height; y++) {
    stack.push([0, y]);
    stack.push([width - 1, y]);
  }

  while (stack.length > 0) {
    const [x, y] = stack.pop();
    const i = idx(x, y);
    if (visited[i]) continue;
    visited[i] = 1;

    if (colorDistToBg(i) > tolerance) continue;

    data[i * 4 + 3] = 0;
    removed[i] = 1;

    if (x > 0) stack.push([x - 1, y]);
    if (x < width - 1) stack.push([x + 1, y]);
    if (y > 0) stack.push([x, y - 1]);
    if (y < height - 1) stack.push([x, y + 1]);
  }

  featherEdges(data, removed, width, height);

  ctx.putImageData(imageData, 0, 0);
  return imageData;
}

/// 切り抜きの境界がギザギザに見えないよう、透明画素に隣接する不透明画素の
/// アルファ値を少しだけ弱めてなじませる、簡易的な1パスのフェザリング。
function featherEdges(data, removed, width, height) {
  const idx = (x, y) => y * width + x;

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const i = idx(x, y);
      if (removed[i]) continue;

      let transparentNeighbors = 0;
      const neighbors = [
        [x - 1, y],
        [x + 1, y],
        [x, y - 1],
        [x, y + 1],
      ];
      for (const [nx, ny] of neighbors) {
        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
        if (removed[idx(nx, ny)]) transparentNeighbors++;
      }

      if (transparentNeighbors > 0) {
        const p = i * 4;
        const factor = 1 - transparentNeighbors * 0.18;
        data[p + 3] = Math.round(data[p + 3] * Math.max(0.35, factor));
      }
    }
  }
}

/// 背景切り抜き後、周囲の透明な余白を取り除いて被写体にぴったりのサイズに詰める。
function trimTransparentMargins(sourceCanvas) {
  const width = sourceCanvas.width;
  const height = sourceCanvas.height;
  const ctx = sourceCanvas.getContext("2d");
  const data = ctx.getImageData(0, 0, width, height).data;

  let minX = width,
    minY = height,
    maxX = -1,
    maxY = -1;

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const alpha = data[(y * width + x) * 4 + 3];
      if (alpha > 12) {
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
      }
    }
  }

  // 被写体が見つからなかった場合(背景除去が効きすぎた等)は元画像をそのまま返す
  if (maxX < minX || maxY < minY) {
    return sourceCanvas;
  }

  // 少し余白(パディング)を残す
  const pad = Math.round(Math.max(maxX - minX, maxY - minY) * 0.04);
  minX = Math.max(0, minX - pad);
  minY = Math.max(0, minY - pad);
  maxX = Math.min(width - 1, maxX + pad);
  maxY = Math.min(height - 1, maxY + pad);

  const trimmedWidth = maxX - minX + 1;
  const trimmedHeight = maxY - minY + 1;

  const trimmedCanvas = document.createElement("canvas");
  trimmedCanvas.width = trimmedWidth;
  trimmedCanvas.height = trimmedHeight;
  trimmedCanvas
    .getContext("2d")
    .drawImage(sourceCanvas, minX, minY, trimmedWidth, trimmedHeight, 0, 0, trimmedWidth, trimmedHeight);

  return trimmedCanvas;
}

/// Firebase保存やカード表示用に、指定した最大辺の長さに収まるようリサイズしてPNGのdata URLを返す。
/// (透過を保持する必要があるのでJPEGではなくPNGを使用。Realtime Databaseに直接入れるため
///  サイズを抑える目的で最大辺500px程度に制限している)
function resizeCanvasToDataUrl(sourceCanvas, maxDimension) {
  const scale = Math.min(1, maxDimension / Math.max(sourceCanvas.width, sourceCanvas.height));
  const targetWidth = Math.max(1, Math.round(sourceCanvas.width * scale));
  const targetHeight = Math.max(1, Math.round(sourceCanvas.height * scale));

  const resizedCanvas = document.createElement("canvas");
  resizedCanvas.width = targetWidth;
  resizedCanvas.height = targetHeight;
  resizedCanvas.getContext("2d").drawImage(sourceCanvas, 0, 0, targetWidth, targetHeight);

  return resizedCanvas.toDataURL("image/png");
}

capturePhotoBtn.addEventListener("click", () => {
  if (!photoVideoEl.videoWidth) {
    setStatus("カメラの準備中です。少し待ってから撮影してください", "error");
    return;
  }

  const width = photoVideoEl.videoWidth;
  const height = photoVideoEl.videoHeight;
  photoCaptureCanvasEl.width = width;
  photoCaptureCanvasEl.height = height;

  const ctx = photoCaptureCanvasEl.getContext("2d");
  ctx.drawImage(photoVideoEl, 0, 0, width, height);

  removeBackground(ctx, width, height);
  const trimmedCanvas = trimTransparentMargins(photoCaptureCanvasEl);

  cutoutPreviewCanvasEl.width = trimmedCanvas.width;
  cutoutPreviewCanvasEl.height = trimmedCanvas.height;
  cutoutPreviewCanvasEl.getContext("2d").drawImage(trimmedCanvas, 0, 0);

  capturedCutoutDataUrl = resizeCanvasToDataUrl(trimmedCanvas, 500);

  photoVideoEl.style.display = "none";
  cutoutPreviewCanvasEl.style.display = "block";
  capturePhotoBtn.style.display = "none";
  retakePhotoBtn.style.display = "inline-block";
  usePhotoBtn.style.display = "inline-block";

  setStatus("背景を切り抜きました。よければ「この写真を使う」を押してください");
});

retakePhotoBtn.addEventListener("click", () => {
  capturedCutoutDataUrl = null;

  photoVideoEl.style.display = "block";
  cutoutPreviewCanvasEl.style.display = "none";
  capturePhotoBtn.style.display = "inline-block";
  retakePhotoBtn.style.display = "none";
  usePhotoBtn.style.display = "none";

  setStatus("背景がなるべく無地になるようにキャラクターを置いて撮影してください");
});

usePhotoBtn.addEventListener("click", () => {
  stopPhotoCamera();
  showNameInput(scannedCardData);
});

skipPhotoBtn.addEventListener("click", () => {
  capturedCutoutDataUrl = null;
  stopPhotoCamera();
  showNameInput(scannedCardData);
});

/// 対戦キュー(battleSlots/player1, battleSlots/player2)への参加。
/// PC(Unity)側は一切QRコードを読み取らず、この2枠が両方埋まるのをポーリングで
/// 待つだけの設計にしている。2台のスマホがほぼ同時に登録した場合の競合を避けるため、
/// 単純なset()ではなくtransaction()で「今空いているか」をアトミックに確認してから書き込む。
function joinBattleQueue(recordData) {
  const slot1Ref = db.ref("battleSlots/player1");
  const slot2Ref = db.ref("battleSlots/player2");

  slot1Ref.transaction(
    (current) => (current === null ? recordData : undefined), // undefinedを返すと競合とみなされ書き込まれない
    (error, committed) => {
      if (error) {
        logDebug("エラー(battleSlots/player1のtransaction): " + error);
        return;
      }
      if (committed) {
        onJoinedBattleQueue("player1");
        return;
      }

      // player1が既に埋まっていた場合はplayer2を試す
      slot2Ref.transaction(
        (current) => (current === null ? recordData : undefined),
        (error2, committed2) => {
          if (error2) {
            logDebug("エラー(battleSlots/player2のtransaction): " + error2);
            return;
          }
          if (committed2) {
            onJoinedBattleQueue("player2");
          } else {
            setStatus("現在、対戦の順番待ちが満席です。少し待ってからもう一度お試しください", "error");
          }
        }
      );
    }
  );
}

/// 対戦キューへの参加に成功した後、相手が揃うまでの状況をリアルタイムに表示する。
/// PC側が試合成立と判定するとbattleSlotsをクリアするので、それを検知したら
/// 「対戦が始まりました」と表示してリスナーを解除する。
function onJoinedBattleQueue(mySlot) {
  const slotLabel = mySlot === "player1" ? "プレイヤー1" : "プレイヤー2";
  setStatus(`対戦キューに参加しました(${slotLabel})。相手を待っています…`, "success");

  const slotsRef = db.ref("battleSlots");
  const handler = slotsRef.on("value", (snapshot) => {
    const slots = snapshot.val();

    if (slots && slots.player1 && slots.player2) {
      setStatus("対戦相手が見つかりました！PC画面をご覧ください", "success");
      return;
    }

    // 自分が登録したはずのスロットが消えている ＝ PC側で試合が成立し、
    // 次の組のためにリセットされた合図
    if (!slots || !slots[mySlot]) {
      setStatus("対戦が始まりました。PC画面をご覧ください！", "success");
      slotsRef.off("value", handler);
    }
  });
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
  const recordData = {
    cardId: scannedCardData.cardId,
    seed: scannedCardData.seed,
    timestamp: Date.now(),
    ...stats,
  };

  // 撮影・背景切り抜きした写真があれば一緒に保存する(PNGのdata URL文字列として)。
  // Realtime Databaseの肥大化を避けるため、resizeCanvasToDataUrlで最大辺500px程度に
  // 縮小済みのものを使っている。
  if (capturedCutoutDataUrl) {
    recordData.photoDataUrl = capturedCutoutDataUrl;
  }

  db.ref("characters")
    .push(recordData)
    .catch((e) => {
      logDebug("エラー(Firebase保存、表示は続行します): " + e);
    });

  // Unity(バトルシーン)がQRコードをスキャンした際にcardIdだけでこのキャラクターを
  // 引けるよう、cardIdをキーにした最新スナップショットも別途保存しておく。
  // (同じカードを登録し直した場合は上書きされ、常に最新の内容になる)
  db.ref("characterByCard/" + scannedCardData.cardId)
    .set(recordData)
    .catch((e) => {
      logDebug("エラー(characterByCard保存、表示は続行します): " + e);
    });

  // 対戦キューへの参加。PC(Unity)側は一切QRコードを読み取らず、
  // battleSlots/player1・player2の両方が埋まるのをポーリングで待つだけの設計にしたため、
  // 「スマホでの登録」がそのまま「対戦への参加」を兼ねる。
  joinBattleQueue(recordData);

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
