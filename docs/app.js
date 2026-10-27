// オリサモ カードスキャン用ページ
// スマホのカメラでQRコード(カードID＋シード値のJSON)を読み取り、名前を入力すると、
// その場でJavaScript側でキャラクターのステータスを確定させて表示する。
// 登録内容は Firebase Realtime Database の /characterByCard/{cardId} に保存し、
// 対戦時は /tables/{卓ID}/battleSlots に参加して、/tables/{卓ID}/activeBattle で会場PC(Unity)と同期する。

// アプリのバージョン。Unity側(Player Settings の Version)と同じ番号にそろえておく。
// 上げる時は index.html の style.css?v= / app.js?v= も同じ番号にする(スマホに古いファイルが残らないように)。
const APP_VERSION = "0.7.3";
document.getElementById("app-version").textContent = "ver " + APP_VERSION;

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

// 卓ID。会場で卓ごとに ?table=table2 のようなURL(QR)を配ることで、複数の卓を同時に運用できる。
// Unity側の Resources/FirebaseSettings.json の tableId(または起動引数 -table)と一致させる。
const TABLE_ID = (() => {
  const value = new URLSearchParams(location.search).get("table");
  return value && /^[A-Za-z0-9_-]{1,32}$/.test(value) ? value : "table1";
})();
const TABLE_PATH = `tables/${TABLE_ID}`;

// 開発者モード。URLに ?dev=1 を付けて開くと、QRコードを読み取らずに
// 「カードIDの手入力・テストカードの発行」で登録/対戦したり、CPU相手を呼んだりできる。
// (スマホ1台と会場PCだけで、本番と同じ流れを最後まで確認するための機能)
const DEV_MODE = new URLSearchParams(location.search).get("dev") === "1";
const DEV_LAST_CARD_KEY = "orisamo.devLastCardId";

const ELEMENT_TYPES = ["Fire", "Wind", "Dark", "Water", "Earth", "Light"];

// AttackLevel(C#)の定義順 Weak, Normal, Strong に合わせる
const ATTACK_LEVELS = ["Weak", "Normal", "Strong"];
const ATTACK_LEVEL_LABELS = { Weak: "弱", Normal: "普", Strong: "強" };

/// カードのシード値から必殺技レベルを決める(C#の BattleRules.ComputeSpecialLevel と同じ結果)。
/// 能力値用の乱数列と重ならないよう、シードに固定値を混ぜた別の乱数列を使う。名前には依存しない。
function computeSpecialLevel(seed) {
  const rand = createSeededRandom((seed ^ 0x5bd1e995) >>> 0);
  return ATTACK_LEVELS[Math.floor(rand() * 3)];
}

/// 登録データの必殺技レベル。specialLevel導入前の登録データはシード値から求める。
function specialLevelOf(record) {
  return record.specialLevel || computeSpecialLevel(record.seed);
}
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
    specialLevel: computeSpecialLevel(seed),
  };
}

const statusEl = document.getElementById("status");
const homeSectionEl = document.getElementById("home-section");
const createCharacterBtn = document.getElementById("create-character-btn");
const startBattleBtn = document.getElementById("start-battle-btn");
const backToHomeBtn = document.getElementById("back-to-home-btn");
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
const registeredOverlayEl = document.getElementById("registered-overlay");
const registeredNameEl = document.getElementById("registered-name");
const registeredPhotoEl = document.getElementById("registered-photo");
const registeredCloseBtn = document.getElementById("registered-close-btn");
const photoFrameEl = document.getElementById("photo-frame");
const photoTargetEl = document.getElementById("photo-target");
const cardCharacterCutoutEl = document.getElementById("card-character-cutout");
const revealCardEl = document.getElementById("reveal-card");
const revealFlashEl = document.getElementById("reveal-flash");
const shockwaveRingEl = document.getElementById("shockwave-ring");
const flashBulbsEl = document.getElementById("flash-bulbs");
const mutationVignetteEl = document.getElementById("mutation-vignette");
const sparkleLayerEl = document.getElementById("sparkle-layer");
const cardArtEl = document.getElementById("card-art");
const cardArtLayerEls = document.querySelectorAll(".card-art-layer");
const mutationBadgeEl = document.getElementById("mutation-badge");
const resultCharacterNameEl = document.getElementById("result-character-name");
const resultAttackEl = document.getElementById("result-attack");
const resultDefenseEl = document.getElementById("result-defense");
const resultSpeedEl = document.getElementById("result-speed");
const resultHpEl = document.getElementById("result-hp");
const resultSkillNameEl = document.getElementById("result-skill-name");
const resultSkillDescriptionEl = document.getElementById("result-skill-description");
const nextScanBtn = document.getElementById("next-scan-btn");
const revealHomeBtn = document.getElementById("reveal-home-btn");
const battleWaitSectionEl = document.getElementById("battle-wait-section");
const battleWaitPhotoEl = document.getElementById("battle-wait-photo");
const battleWaitNameEl = document.getElementById("battle-wait-name");
const battleWaitStatusEl = document.getElementById("battle-wait-status");
const battleHomeBtn = document.getElementById("battle-home-btn");

// 対戦中(スマホ側で強/普/弱を選ぶ画面。PC/Unity側は結果を表示するだけ)
const battleTurnSectionEl = document.getElementById("battle-turn-section");
const battleTurnPromptEl = document.getElementById("battle-turn-prompt");
const battleTurnButtonsEl = document.getElementById("battle-turn-buttons");
const battleTurnStrongBtn = document.getElementById("battle-turn-strong-btn");
const battleTurnNormalBtn = document.getElementById("battle-turn-normal-btn");
const battleTurnWeakBtn = document.getElementById("battle-turn-weak-btn");
const battleTurnResultEl = document.getElementById("battle-turn-result");
const battleTurnHomeBtn = document.getElementById("battle-turn-home-btn");
const battleTurnRecordEl = document.getElementById("battle-turn-record");
const battleWinCardBtn = document.getElementById("battle-win-card-btn");
const devPanelEl = document.getElementById("dev-panel");
const devCardIdInput = document.getElementById("dev-card-id");
const devRegisterBtn = document.getElementById("dev-register-btn");
const devBattleBtn = document.getElementById("dev-battle-btn");
const devQuickBattleBtn = document.getElementById("dev-quick-battle-btn");
const devCpuBtn = document.getElementById("dev-cpu-btn");
const battleWaitCpuBtn = document.getElementById("battle-wait-cpu-btn");

let scanner = null;
let scannerStartPromise = null; // カメラ起動中のPromise(起動が終わる前に止めないため)
let isSending = false; // Firebaseへの書き込み〜結果待ちの間（多重送信防止）
let isAwaitingName = false; // QR読み取り済み・名前入力/結果待ち（この間はスキャン結果を無視する）
let scannedCardData = null; // QRから読み取ったカード情報(cardId, seedなど)
let photoStream = null; // 実物撮影用のカメラストリーム(MediaStream)
let capturedCutoutDataUrl = null; // 背景切り抜き後のキャラクター写真(PNG, data URL)。未撮影ならnull
let photoTarget = { x: 0.5, y: 0.5 }; // 切り抜く被写体の位置(撮影枠内の0〜1の座標)。タップで変更する

// "create"(キャラクター作成) または "battle"(対戦開始) のどちらの目的でQRを読むか。
// ホーム画面でボタンを押すまではnull。
let appMode = null;

// 対戦キュー参加後、相手が揃うのを監視しているリスナー(ホームに戻る際に解除するため保持)
let battleQueueListenerRef = null;
let battleQueueListenerHandler = null;

// 対戦本編(activeBattle)の進行を監視しているリスナーまわりの状態。
// 対戦の意思決定はすべてスマホ側で行い、PC(Unity)は結果を演出して表示するだけにする設計。
let battleTurnListenerRef = null;
let battleTurnListenerHandler = null;
let myBattleSlot = null; // "player1" または "player2"(このスマホが対戦キューに参加した時のスロット)
let myQueueJoinedAt = null; // 対戦キューに書き込んだ時刻(自分の枠かどうかの確認用)
let myBattleRecord = null; // 対戦に参加したキャラクターの登録データ
let currentMatchId = null; // 参加中の試合のID(前の試合のデータを読まないための目印)
let turnCountdownTimer = null; // 選択の制限時間の表示用タイマー
let currentBattleTurnNumber = null; // 現在activeBattleに出ているターン番号
let currentBattleTurnRole = null; // このターン、自分が"attacker"(攻撃側)か"defender"(防御側)か
let submittedBattleTurnNumber = -1; // 既に選択を送信済みのターン番号(二重送信・ボタン再表示防止用)

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

// 「既に登録済みです」と表示したカード。しばらくは同じQRを読み取っても無視する
let rejectedCardId = null;
let rejectedCardUntil = 0;

function setStatus(text, type = "info") {
  statusEl.textContent = text;
  statusEl.className = "status " + type;
}

/// コンソールにログを出す（画面上には表示しない）。
function logDebug(text) {
  console.log(text);
}

function init() {
  logDebug(`ページを読み込みました。(ver ${APP_VERSION})`);

  try {
    firebase.initializeApp(firebaseConfig);
    db = firebase.database();
    logDebug("Firebaseの初期化に成功しました。卓ID=" + TABLE_ID);
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

  if (!isSecure && !DEV_MODE) {
    setStatus(
      "この機能はhttps接続でのみ動作します。GitHub PagesのURL(https://...)でアクセスしてください。",
      "error"
    );
    logDebug("エラー: セキュアな接続(https)ではありません。現在のprotocol=" + location.protocol);
    return;
  }

  if (typeof Html5Qrcode === "undefined" && !DEV_MODE) {
    setStatus(
      "QRコード読み取りライブラリの読み込みに失敗しました。通信環境を確認して再読み込みしてください。",
      "error"
    );
    logDebug("エラー: Html5Qrcodeが読み込まれていません（CDNからのスクリプト読み込みに失敗した可能性）");
    return;
  }

  if ((!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) && !DEV_MODE) {
    setStatus("このブラウザはカメラ機能に対応していません。別のブラウザでお試しください。", "error");
    logDebug("エラー: navigator.mediaDevices.getUserMedia が利用できません");
    return;
  }

  // 匿名認証でサインインする(Database Rulesで書き込み元を確認するため)。
  // 匿名認証が無効な場合もエラーにはせず、そのまま利用できるようにする。
  setStatus("接続しています...");
  signInAnonymously().finally(() => {
    watchDisplayPresence();
    goHome();
  });
}

// ==================== PC(Unity)の画面が開いているかの確認 ====================
// Unity(DisplayPresence.cs)は tables/{卓ID}/display/heartbeat に数秒ごとにサーバー時刻を書き込む。
// その時刻が古い間は「PCの画面が開いていない」とみなし、QRコードを読み取らせない。
const DISPLAY_STALE_MS = 15000;

let displayHeartbeatAt = 0; // 最後に受け取ったUnityの生存信号(サーバー時刻のミリ秒)
let serverTimeOffset = 0; // サーバー時刻 - この端末の時刻
let displayCheckUnavailable = false; // 権限などで確認できない時は、読み取りを止めないようにする
let displayOnline = null; // null = まだ確認中
let scanPromptText = ""; // 読み取り中に表示する案内(PCが戻った時に表示し直す)

function watchDisplayPresence() {
  db.ref(".info/serverTimeOffset").on("value", (snapshot) => {
    serverTimeOffset = snapshot.val() || 0;
  });
  db.ref(`${TABLE_PATH}/display/heartbeat`).on(
    "value",
    (snapshot) => {
      displayHeartbeatAt = snapshot.val() || 0;
      updateDisplayOnline();
    },
    (e) => {
      logDebug("PC画面の確認ができないため、確認せずに読み取ります: " + e);
      displayCheckUnavailable = true;
      updateDisplayOnline();
    }
  );
  setInterval(updateDisplayOnline, 2000); // 信号が途絶えたことに気づくための定期確認
}

function isDisplayOnline() {
  if (displayCheckUnavailable) return true;
  return displayHeartbeatAt > 0 && Date.now() + serverTimeOffset - displayHeartbeatAt < DISPLAY_STALE_MS;
}

function updateDisplayOnline() {
  const online = isDisplayOnline();
  if (online === displayOnline) return;
  displayOnline = online;
  logDebug(online ? "PCの画面が開いています" : "PCの画面が開いていません");
  refreshScannerGate();
}

/// QR読み取り画面を表示中なら、PCの画面が開いている時だけカメラを動かす。
function refreshScannerGate() {
  const scanning = (appMode === "create" || appMode === "battle") && readerEl.style.display !== "none" && !isAwaitingName;
  if (!scanning) return;
  if (displayOnline === null) {
    setStatus("PCの画面を確認しています...");
    return;
  }
  if (displayOnline) {
    setStatus(scanPromptText);
    startScanner();
  } else {
    stopScanner();
    setStatus("PCの対戦画面が開いていないため、QRコードを読み取れません。画面が開くと自動で読み取りを始めます", "error");
  }
}

/// QR読み取り画面を表示する(PCの画面が開いていなければ、開くまで読み取りを始めない)。
function beginScanning(promptText) {
  scanPromptText = promptText;
  showReader();
  refreshScannerGate();
}

function signInAnonymously() {
  if (!firebase.auth) {
    logDebug("firebase-auth が読み込まれていないため、認証なしで接続します");
    return Promise.resolve();
  }
  return firebase
    .auth()
    .signInAnonymously()
    .then((cred) => logDebug("匿名認証に成功しました uid=" + cred.user.uid))
    .catch((e) => logDebug("匿名認証を利用できないため、認証なしで接続します: " + e));
}

function currentUid() {
  return firebase.auth && firebase.auth().currentUser ? firebase.auth().currentUser.uid : null;
}

/// キャラクター作成モードを開始する: ホームを隠してQRリーダーを表示する。
function startCreateMode() {
  appMode = "create";
  homeSectionEl.style.display = "none";
  beginScanning("キャラクターにするカードのQRコードをカメラにかざしてください");
}

/// 対戦開始モードを開始する: ホームを隠してQRリーダーを表示する。
/// (このモードでは、既にキャラクター作成済みのカードのQRを読み取ってbattleSlotsに参加する)
function startBattleMode() {
  appMode = "battle";
  homeSectionEl.style.display = "none";
  beginScanning("対戦するキャラクターのカードのQRコードをカメラにかざしてください");
}

function showReader() {
  readerEl.style.display = "block";
  backToHomeBtn.style.display = "inline-block";
}

function hideReader() {
  readerEl.style.display = "none";
  backToHomeBtn.style.display = "none";
}

/// 全セクションを閉じてホーム画面に戻す。実行中のスキャナー・対戦キュー監視も停止する。
function goHome() {
  appMode = null;
  clearTimeout(alreadyRegisteredTimer);
  registeredOverlayEl.classList.remove("show");
  isSending = false;
  isAwaitingName = false;
  scannedCardData = null;
  capturedCutoutDataUrl = null;

  stopPhotoCamera();
  stopScanner();
  detachBattleQueueListener();
  detachBattleTurnListener();
  leaveBattleQueue();
  stopDevBot(true);
  myBattleSlot = null;
  myQueueJoinedAt = null;
  myBattleRecord = null;
  currentMatchId = null;

  mutationVignetteEl.classList.remove("active");
  hideReader();
  photoCaptureSectionEl.style.display = "none";
  nameInputSectionEl.style.display = "none";
  statusDisplaySectionEl.style.display = "none";
  battleWaitSectionEl.style.display = "none";
  battleTurnSectionEl.style.display = "none";
  homeSectionEl.style.display = "block";
  if (devPanelEl) devPanelEl.style.display = DEV_MODE ? "block" : "none";
  if (battleWaitCpuBtn) battleWaitCpuBtn.style.display = "none";
  setStatus(DEV_MODE ? "開発者モード: QRコードなしでも操作できます" : "やりたいことを選んでください");
}

/// QRスキャナーを停止し、カメラを解放する(対戦キュー待機画面やホームに戻る際に呼ぶ)。
function stopScanner() {
  if (!scanner) return;
  const runningScanner = scanner;
  const starting = scannerStartPromise || Promise.resolve();
  scanner = null;
  scannerStartPromise = null;
  // カメラの起動中に止めるとライブラリが例外を出すため、起動が終わってから止める
  return starting
    .then(() => runningScanner.stop())
    .then(() => runningScanner.clear())
    .catch((e) => logDebug("エラー(スキャナー停止): " + e));
}

/// 対戦相手を待っている途中でホームに戻った場合、自分の枠を空ける(他の人が参加できるように)。
/// 既に試合が始まって枠がクリアされた後や、別の人の枠になっている場合は何もしない。
function leaveBattleQueue() {
  stopQueueGuards();
  if (!myBattleSlot || myQueueJoinedAt == null || currentMatchId) return;
  const joinedAt = myQueueJoinedAt;
  db.ref(`${TABLE_PATH}/battleSlots/${myBattleSlot}`)
    .transaction((current) => {
      if (current === null) return null;
      return current.timestamp === joinedAt ? null : undefined;
    })
    .catch((e) => logDebug("エラー(対戦キューからの離脱): " + e));
}

/// 対戦相手待ちのリアルタイム監視リスナーを解除する(二重登録・メモリリーク防止)。
function detachBattleQueueListener() {
  if (battleQueueListenerRef && battleQueueListenerHandler) {
    battleQueueListenerRef.off("value", battleQueueListenerHandler);
  }
  battleQueueListenerRef = null;
  battleQueueListenerHandler = null;
}

// ==================== 対戦待ちの間の見張り ====================
// 大人数で遊ぶと、待機中の人が立ち去って枠が埋まったままになることがある。その対策:
// A. 通信が切れたら(ブラウザを閉じた・圏外など)、Firebaseが自動で自分の枠を消す(onDisconnect)
// B. 待機中は queuePresence/{枠} に数秒ごとに生存信号を書き込む。途絶えた枠や、
//    待ち時間が長すぎる枠は、会場PC(BattleQueueIntake)が消す
const QUEUE_PRESENCE_INTERVAL_MS = 5000;
let queuePresenceTimer = null;
let queueGuardSlot = null;

function queuePresenceRef(slot) {
  return db.ref(`${TABLE_PATH}/queuePresence/${slot}`);
}

function sendQueuePresence() {
  if (!queueGuardSlot) return;
  queuePresenceRef(queueGuardSlot)
    .set({ uid: currentUid(), at: firebase.database.ServerValue.TIMESTAMP })
    .catch((e) => logDebug("エラー(対戦待ちの生存信号): " + e));
}

function startQueueGuards(slot) {
  stopQueueGuards();
  queueGuardSlot = slot;
  // A. 切断されたら枠と生存信号を自動で消す
  db.ref(`${TABLE_PATH}/battleSlots/${slot}`)
    .onDisconnect()
    .remove()
    .catch((e) => logDebug("エラー(切断時の枠の削除の予約): " + e));
  queuePresenceRef(slot)
    .onDisconnect()
    .remove()
    .catch(() => {});
  // B. 生存信号
  sendQueuePresence();
  queuePresenceTimer = setInterval(sendQueuePresence, QUEUE_PRESENCE_INTERVAL_MS);
}

/// 待機が終わった(試合が始まった・ホームに戻った・枠が消された)時に、見張りを止める。
/// 切断時の削除の予約も取り消す(残すと、後で同じ端末が別の枠に入った時に誤って消してしまうため)。
function stopQueueGuards() {
  if (queuePresenceTimer) clearInterval(queuePresenceTimer);
  queuePresenceTimer = null;
  if (!queueGuardSlot) return;
  const slot = queueGuardSlot;
  queueGuardSlot = null;
  db.ref(`${TABLE_PATH}/battleSlots/${slot}`).onDisconnect().cancel().catch(() => {});
  queuePresenceRef(slot).onDisconnect().cancel().catch(() => {});
  queuePresenceRef(slot).remove().catch(() => {});
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

  const startingScanner = scanner;
  scannerStartPromise = scanner
    .start({ facingMode: "environment" }, config, onScanSuccess, onScanFailure)
    .then(() => {
      logDebug("カメラの起動に成功しました。");
    })
    .catch((err) => {
      setStatus("カメラを起動できませんでした: " + err, "error");
      logDebug("エラー(カメラ起動): " + err);
      if (scanner === startingScanner) scanner = null; // 失敗したので再度ボタンを押せば再試行できるようにする
    });
}

function onScanFailure() {
  // 1フレームごとに呼ばれるが、単に読み取れていないだけなので何もしない
}

function onScanSuccess(decodedText) {
  // 送信中、または既に読み取り済みで名前入力/結果待ちの間は、続けて読み取っても無視する
  if (isSending || isAwaitingName) return;
  // PCの画面が開いていない間は読み取らない
  if (!isDisplayOnline()) {
    refreshScannerGate();
    return;
  }

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

  if (appMode === "battle") {
    handleBattleQrScanned(cardData);
    return;
  }

  confirmOverwriteIfRegistered(cardData);
}

const ALREADY_REGISTERED_AUTO_CLOSE_MS = 6000;
let alreadyRegisteredTimer = null;

/// 「既に登録済みです」を画面いっぱいに大きく表示する。ボタンか時間経過で閉じる。
function showAlreadyRegistered(record) {
  registeredNameEl.textContent = record.characterName;
  if (record.photoDataUrl) {
    registeredPhotoEl.src = record.photoDataUrl;
    registeredPhotoEl.style.display = "block";
  } else {
    registeredPhotoEl.removeAttribute("src");
    registeredPhotoEl.style.display = "none";
  }
  registeredOverlayEl.classList.remove("show");
  void registeredOverlayEl.offsetWidth; // 連続で表示した時もアニメーションをやり直す
  registeredOverlayEl.classList.add("show");
  setStatus("このカードは既に登録済みです。別のカードを読み取ってください", "error");
  if (navigator.vibrate) navigator.vibrate([120, 80, 120]);

  clearTimeout(alreadyRegisteredTimer);
  alreadyRegisteredTimer = setTimeout(hideAlreadyRegistered, ALREADY_REGISTERED_AUTO_CLOSE_MS);
}

function hideAlreadyRegistered() {
  clearTimeout(alreadyRegisteredTimer);
  alreadyRegisteredTimer = null;
  if (!registeredOverlayEl.classList.contains("show")) return;
  registeredOverlayEl.classList.remove("show");
  isAwaitingName = false;
  // 同じカードがまだカメラに映っていても、すぐにまた表示しないよう少しだけ無視する
  rejectedCardUntil = Date.now() + 2500;
  if (appMode === "create") setStatus("キャラクターにするカードのQRコードをカメラにかざしてください");
}

registeredCloseBtn.addEventListener("click", hideAlreadyRegistered);

/// 作成モードで読み取ったカードが登録済みなら「既に登録済みです」と表示して登録させない。
/// (上書きできると、別の人のキャラクターが消えてしまうため)
function confirmOverwriteIfRegistered(cardData) {
  if (isSending) return;
  // 登録済みと表示したカードがカメラに映り続けている間は、同じ確認を繰り返さない
  if (cardData.cardId === rejectedCardId && Date.now() < rejectedCardUntil) return;
  isSending = true;
  isAwaitingName = true; // 確認中に同じQRを読み続けないようにする

  db.ref("characterByCard/" + cardData.cardId)
    .once("value")
    .then((snapshot) => {
      const existing = snapshot.val();
      if (existing && existing.characterName) {
        rejectedCardId = cardData.cardId;
        showAlreadyRegistered(existing); // 閉じるまで読み取りは止めたまま(isAwaitingName = true)
        return;
      }
      scannedCardData = cardData;
      capturedCutoutDataUrl = null;
      showPhotoCapture(cardData);
    })
    .catch((e) => {
      isAwaitingName = false;
      logDebug("エラー(登録済みかの確認): " + e);
      setStatus("通信に失敗しました。もう一度読み取ってください", "error");
    })
    .finally(() => {
      isSending = false;
    });
}

/// 対戦開始モードでQRを読み取った時の処理。
/// 既にキャラクター作成済みのカード(characterByCard/{cardId})かどうかを確認し、
/// 見つかればそのデータのままbattleSlotsに参加する(名前・写真は再入力させない)。
function handleBattleQrScanned(cardData) {
  if (isSending) return;
  isSending = true;
  setStatus("カード情報を確認しています...");

  db.ref("characterByCard/" + cardData.cardId)
    .once("value")
    .then((snapshot) => {
      isSending = false;
      const record = snapshot.val();

      if (!record || !record.characterName) {
        setStatus(
          "このカードはまだ作成されていません。先に「カードを登録」で登録してください",
          "error"
        );
        return; // スキャナーは動作を続けるので、そのまま別のカードを読み取れる
      }

      stopScanner();
      myBattleRecord = record;
      showBattleWait(record);
      myQueueJoinedAt = Date.now();
      joinBattleQueue({ ...record, specialLevel: specialLevelOf(record), uid: currentUid(), timestamp: myQueueJoinedAt });
    })
    .catch((e) => {
      isSending = false;
      logDebug("エラー(characterByCard取得): " + e);
      setStatus("通信に失敗しました。もう一度お試しください", "error");
    });
}

/// 対戦キュー参加後の待機画面を表示する。
function showBattleWait(record) {
  hideReader();
  photoCaptureSectionEl.style.display = "none";
  nameInputSectionEl.style.display = "none";
  statusDisplaySectionEl.style.display = "none";
  battleWaitSectionEl.style.display = "block";

  battleWaitNameEl.textContent = `${record.characterName}（必殺技レベル「${ATTACK_LEVEL_LABELS[specialLevelOf(record)]}」）`;
  if (record.photoDataUrl) {
    battleWaitPhotoEl.src = record.photoDataUrl;
    battleWaitPhotoEl.style.display = "block";
  } else {
    battleWaitPhotoEl.src = "";
    battleWaitPhotoEl.style.display = "none";
  }
  battleWaitStatusEl.textContent = `対戦キュー(卓 ${TABLE_ID})に参加しています...`;
  if (battleWaitCpuBtn) battleWaitCpuBtn.style.display = DEV_MODE && !devBot ? "block" : "none";
}

/// 対戦キューの状況メッセージを、下部のステータスバーと待機画面の両方に反映する。
function setBattleStatus(text, type = "info") {
  setStatus(text, type);
  if (battleWaitStatusEl) battleWaitStatusEl.textContent = text;
}

/// カード読み取り直後、実物を撮影して背景を切り抜く画面を表示する
async function showPhotoCapture(cardData) {
  isAwaitingName = true;

  photoCardIdEl.textContent = cardData.cardId;

  // 表示状態を初期化(2回目以降のスキャンでも正しく表示されるように)
  photoVideoEl.style.display = "block";
  cutoutPreviewCanvasEl.style.display = "none";
  photoTargetEl.style.display = "none";
  capturePhotoBtn.style.display = "inline-block";
  retakePhotoBtn.style.display = "none";
  usePhotoBtn.style.display = "none";
  resetPhotoBounds();

  hideReader();
  nameInputSectionEl.style.display = "none";
  photoCaptureSectionEl.style.display = "block";
  setStatus("\u307e\u305a\u30ad\u30e3\u30e9\u30af\u30bf\u30fc\u3092\u64ae\u5f71\u3057\u3066\u304f\u3060\u3055\u3044");

  // Mobile browsers may reject a second camera stream until the QR reader releases it.
  await stopScanner();
  if (photoCaptureSectionEl.style.display === "none" || !isAwaitingName) return;
  startPhotoCamera();
  loadSegmenter(); // 撮影までにAIモデルを読み込んでおく
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
  beginScanning("キャラクターにするカードのQRコードをカメラにかざしてください");
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
  cardArtLayerEls.forEach((el) => (el.src = cardArtEl.src));

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
        setStatus(
          `${stats.characterName} を登録しました！必殺技レベルは「${ATTACK_LEVEL_LABELS[stats.specialLevel]}」です`,
          "success"
        );
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

// ==================== AIによる被写体の切り抜き ====================
// MediaPipe の Interactive Segmenter(magic_touch モデル、約6MB)を使い、
// タップした位置にある「物体」だけをマスクとして取り出す。机やPCなどが写り込んでいても、
// 指定した被写体だけを切り抜ける。モデルを読み込めない環境では従来の色ベースの切り抜きを使う。
const MEDIAPIPE_VERSION = "0.10.35";
const MEDIAPIPE_BASE = `https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@${MEDIAPIPE_VERSION}`;
const SEGMENTER_MODEL_URL =
  "https://storage.googleapis.com/mediapipe-models/interactive_segmenter/magic_touch/float32/1/magic_touch.tflite";
const SEGMENT_MAX_DIMENSION = 1024; // 推論に使う画像の最大辺(大きすぎるとスマホで遅くなる)

let segmenterPromise = null;

/// Interactive Segmenter を一度だけ読み込む(GPUで作れなければCPUで作り直す)。失敗時はnullで解決する。
function loadSegmenter() {
  if (segmenterPromise) return segmenterPromise;
  segmenterPromise = (async () => {
    const { FilesetResolver, InteractiveSegmenter } = await import(`${MEDIAPIPE_BASE}/vision_bundle.mjs`);
    const fileset = await FilesetResolver.forVisionTasks(`${MEDIAPIPE_BASE}/wasm`);
    const create = (delegate) =>
      InteractiveSegmenter.createFromOptions(fileset, {
        baseOptions: { modelAssetPath: SEGMENTER_MODEL_URL, delegate },
        outputConfidenceMasks: true,
        outputCategoryMask: false,
      });
    try {
      return await create("GPU");
    } catch (e) {
      logDebug("GPUで切り抜きモデルを作れなかったのでCPUで作ります: " + e);
      return await create("CPU");
    }
  })().catch((e) => {
    logDebug("エラー(切り抜きモデルの読み込み。従来の方式で切り抜きます): " + e);
    segmenterPromise = null; // 次の撮影で再試行できるようにする
    return null;
  });
  return segmenterPromise;
}

/// 撮影枠内の被写体の位置を設定し、目印を動かす(x, yは枠に対する0〜1)。
function setPhotoTarget(x, y) {
  photoTarget = { x: Math.min(1, Math.max(0, x)), y: Math.min(1, Math.max(0, y)) };
  photoTargetEl.style.left = photoTarget.x * 100 + "%";
  photoTargetEl.style.top = photoTarget.y * 100 + "%";
}

const selectionCanvas = document.getElementById("selection-canvas");
const autoCutoutBtn = document.getElementById("auto-cutout-btn");
const redrawSelectionBtn = document.getElementById("redraw-selection-btn");
let selectionPoints = [], selectionPointer = null, selectingPhoto = false, cutoutBusy = false;

function resetPhotoBounds() {
  selectingPhoto = false;
  selectionPoints = [];
  selectionPointer = null;
  selectionCanvas.style.display = "none";
  autoCutoutBtn.style.display = "none";
  redrawSelectionBtn.style.display = "none";
  autoCutoutBtn.disabled = true;
  capturePhotoBtn.disabled = false;
  photoTargetEl.style.display = "none";
  cutoutPreviewCanvasEl.classList.remove("selecting");
}
function drawSelection() {
  const ctx = selectionCanvas.getContext("2d");
  ctx.clearRect(0, 0, selectionCanvas.width, selectionCanvas.height);
  if (!selectionPoints.length) return;
  ctx.beginPath();
  selectionPoints.forEach((p,i) => i ? ctx.lineTo(p.x,p.y) : ctx.moveTo(p.x,p.y));
  if (selectionPointer === null) ctx.closePath();
  ctx.fillStyle = "rgba(247,207,112,0.18)";
  ctx.fill();
  ctx.strokeStyle = "#f7cf70";
  ctx.lineWidth = Math.max(3,selectionCanvas.width/160);
  ctx.stroke();
}
function selectionPoint(e) {
  const r = selectionCanvas.getBoundingClientRect();
  return {x:Math.max(0,Math.min(selectionCanvas.width,(e.clientX-r.left)/r.width*selectionCanvas.width)),
    y:Math.max(0,Math.min(selectionCanvas.height,(e.clientY-r.top)/r.height*selectionCanvas.height))};
}
selectionCanvas.addEventListener("pointerdown", e => {
  if (!selectingPhoto || cutoutBusy || selectionPointer !== null) return;
  e.preventDefault();
  selectionPointer = e.pointerId;
  selectionCanvas.setPointerCapture(e.pointerId);
  selectionPoints = [selectionPoint(e)];
  autoCutoutBtn.disabled = true;
  drawSelection();
});
selectionCanvas.addEventListener("pointermove", e => {
  if (e.pointerId !== selectionPointer) return;
  const p = selectionPoint(e), last = selectionPoints[selectionPoints.length-1];
  if (Math.hypot(p.x-last.x,p.y-last.y)<3) return;
  selectionPoints.push(p);
  drawSelection();
});
selectionCanvas.addEventListener("pointerup", e => {
  if (e.pointerId !== selectionPointer) return;
  selectionPoints.push(selectionPoint(e));
  selectionPointer = null;
  let area = 0;
  selectionPoints.forEach((p,i) => {const q=selectionPoints[(i+1)%selectionPoints.length];area+=p.x*q.y-q.x*p.y;});
  autoCutoutBtn.disabled = selectionPoints.length<4 || Math.abs(area)/2<selectionCanvas.width*selectionCanvas.height*0.005;
  drawSelection();
  setStatus(autoCutoutBtn.disabled ? "キャラクター全体の周りを、もう少し大きく囲んでください" : "囲みの内側を自動で切り抜けます。なぞり直すと囲みを変更できます");
});
selectionCanvas.addEventListener("pointercancel", () => {
  selectionPointer=null; selectionPoints=[]; autoCutoutBtn.disabled=true; drawSelection();
});
function showPhotoSelection() {
  capturedCutoutDataUrl=null;
  selectingPhoto=true;
  selectionPoints=[];
  selectionPointer=null;
  cutoutPreviewCanvasEl.width=photoCaptureCanvasEl.width;
  cutoutPreviewCanvasEl.height=photoCaptureCanvasEl.height;
  cutoutPreviewCanvasEl.getContext("2d").drawImage(photoCaptureCanvasEl,0,0);
  cutoutPreviewCanvasEl.classList.add("selecting");
  cutoutPreviewCanvasEl.style.display="block";
  selectionCanvas.width=photoCaptureCanvasEl.width;
  selectionCanvas.height=photoCaptureCanvasEl.height;
  selectionCanvas.style.display="block";
  autoCutoutBtn.style.display="inline-block";
  autoCutoutBtn.disabled=true;
  redrawSelectionBtn.style.display="none";
  usePhotoBtn.style.display="none";
  setStatus("写真のキャラクター全体の少し外側を、指でぐるっとなぞってください。指を離すと囲みが閉じます");
}
redrawSelectionBtn.addEventListener("click",showPhotoSelection);

function createSelectedPhoto() {
  let left=Infinity,top=Infinity,right=0,bottom=0;
  for(const p of selectionPoints) {left=Math.min(left,p.x);top=Math.min(top,p.y);right=Math.max(right,p.x);bottom=Math.max(bottom,p.y);}
  left=Math.floor(left);top=Math.floor(top);right=Math.ceil(right);bottom=Math.ceil(bottom);
  const canvas=document.createElement("canvas");
  canvas.width=Math.max(1,right-left);canvas.height=Math.max(1,bottom-top);
  const ctx=canvas.getContext("2d",{willReadFrequently:true});
  ctx.drawImage(photoCaptureCanvasEl,left,top,canvas.width,canvas.height,0,0,canvas.width,canvas.height);
  const mask=document.createElement("canvas");mask.width=canvas.width;mask.height=canvas.height;
  const m=mask.getContext("2d");m.beginPath();
  selectionPoints.forEach((p,i)=>i?m.lineTo(p.x-left,p.y-top):m.moveTo(p.x-left,p.y-top));
  m.closePath();m.fillStyle="white";m.fill();
  // Pick an interior point far from the boundary, including for concave selections.
  const pixels=m.getImageData(0,0,mask.width,mask.height).data;
  const distance=new Float32Array(mask.width*mask.height);
  let best=0;
  for(let y=0;y<mask.height;y++) for(let x=0;x<mask.width;x++) {
    const i=y*mask.width+x;
    if(pixels[i*4+3]<128) continue;
    distance[i]=1+Math.min(x?distance[i-1]:0,y?distance[i-mask.width]:0);
  }
  for(let y=mask.height-1;y>=0;y--) for(let x=mask.width-1;x>=0;x--) {
    const i=y*mask.width+x;if(!distance[i]) continue;
    distance[i]=Math.min(distance[i],1+Math.min(x+1<mask.width?distance[i+1]:0,y+1<mask.height?distance[i+mask.width]:0));
    if(distance[i]>distance[best]) best=i;
  }
  return {canvas,ctx,mask,target:{x:((best%mask.width)+0.5)/mask.width,y:(Math.floor(best/mask.width)+0.5)/mask.height}};
}

function captureVisibleFrame(canvas) {
  const vw = photoVideoEl.videoWidth;
  const vh = photoVideoEl.videoHeight;
  const frameAspect = photoFrameEl.clientWidth / photoFrameEl.clientHeight || 3 / 4;
  let sw = vw,
    sh = vh;
  if (vw / vh > frameAspect) sw = Math.round(vh * frameAspect);
  else sh = Math.round(vw / frameAspect);
  const sx = Math.round((vw - sw) / 2);
  const sy = Math.round((vh - sh) / 2);

  const scale = Math.min(1, SEGMENT_MAX_DIMENSION / Math.max(sw, sh));
  canvas.width = Math.max(1, Math.round(sw * scale));
  canvas.height = Math.max(1, Math.round(sh * scale));
  const ctx = canvas.getContext("2d", { willReadFrequently: true });
  ctx.drawImage(photoVideoEl, sx, sy, sw, sh, 0, 0, canvas.width, canvas.height);
  return ctx;
}

/// AIで被写体を切り抜く。切り抜けた場合はtrue、使えなかった場合はfalseを返す。
async function removeBackgroundWithAi(ctx, width, height, target, selectionMask = null) {
  const segmenter = await loadSegmenter();
  if (!segmenter) return false;

  const result = segmenter.segment(ctx.canvas, { keypoint: { x: target.x, y: target.y } });
  const masks = result.confidenceMasks || [];
  if (masks.length === 0) {
    if (result.close) result.close();
    return false;
  }

  // 被写体側のマスクを選ぶ(タップ位置の値が最も大きいもの)
  const maskWidth = masks[0].width;
  const maskHeight = masks[0].height;
  const tx = Math.min(maskWidth - 1, Math.floor(target.x * maskWidth));
  const ty = Math.min(maskHeight - 1, Math.floor(target.y * maskHeight));
  let confidence = null;
  for (const mask of masks) {
    const values = mask.getAsFloat32Array();
    if (!confidence || values[ty * maskWidth + tx] > confidence[ty * maskWidth + tx]) confidence = values.slice();
  }
  if (result.close) result.close();

  // Apply the actual lasso before connected-component analysis, not just after it.
  if (selectionMask) {
    const roi=document.createElement("canvas");roi.width=maskWidth;roi.height=maskHeight;
    const rc=roi.getContext("2d");rc.drawImage(selectionMask,0,0,maskWidth,maskHeight);
    const pixels=rc.getImageData(0,0,maskWidth,maskHeight).data;
    for(let i=0;i<confidence.length;i++) confidence[i]*=pixels[i*4+3]/255;
  }
  const alpha = refineMaskToAlpha(confidence, maskWidth, maskHeight, tx, ty);
  if (!alpha) return false;

  // Smoothly resample the low-resolution mask instead of copying nearest pixels.
  const alphaCanvas=document.createElement("canvas");
  alphaCanvas.width=maskWidth;alphaCanvas.height=maskHeight;
  const ac=alphaCanvas.getContext("2d"), image=ac.createImageData(maskWidth,maskHeight);
  for(let i=0;i<alpha.length;i++) {image.data[i*4]=255;image.data[i*4+1]=255;image.data[i*4+2]=255;image.data[i*4+3]=alpha[i];}
  ac.putImageData(image,0,0);
  ctx.save();
  ctx.imageSmoothingEnabled=true;ctx.imageSmoothingQuality="high";
  ctx.globalCompositeOperation="destination-in";
  ctx.drawImage(alphaCanvas,0,0,width,height);
  ctx.restore();
  return true;
}

/// 確信度マスク(0〜1)を整えてアルファ値(0〜255)にする。
// Hysteresis keeps uncertain thin edges only when connected to confident foreground.
// Eight-way connectivity preserves diagonal paper folds and narrow limbs.
function refineMaskToAlpha(confidence, width, height, tx, ty) {
  const count=width*height;
  const visited=new Uint8Array(count), retained=new Uint8Array(count);
  const queue=new Int32Array(count);
  const target=Math.max(0,Math.min(height-1,ty))*width+Math.max(0,Math.min(width-1,tx));
  let total=0;
  for(let start=0;start<count;start++) {
    if(visited[start] || confidence[start]<0.32) continue;
    let head=0,tail=1,strong=0,peak=0,containsTarget=false;
    queue[0]=start;visited[start]=1;
    while(head<tail) {
      const i=queue[head++],x=i%width,y=Math.floor(i/width);
      if(confidence[i]>=0.75) strong++;
      peak=Math.max(peak,confidence[i]);
      if(i===target) containsTarget=true;
      for(let dy=-1;dy<=1;dy++) for(let dx=-1;dx<=1;dx++) {
        const nx=x+dx,ny=y+dy;
        if(nx<0 || nx>=width || ny<0 || ny>=height || (!dx && !dy)) continue;
        const n=ny*width+nx;
        if(!visited[n] && confidence[n]>=0.32) {visited[n]=1;queue[tail++]=n;}
      }
    }
    // Preserve separate confident parts inside the user's selection; reject tiny speckles.
    const enough=tail>=Math.max(3,Math.ceil(count*0.00015));
    if(!enough || !(containsTarget ? peak>=0.55 : strong>=Math.max(2,tail*0.08))) continue;
    for(let j=0;j<tail;j++) retained[queue[j]]=1;
    total+=tail;
  }
  if(total<Math.max(3,count*0.001)) return null;
  const near=new Uint8Array(count);
  for(let i=0;i<count;i++) {
    if(!retained[i]) continue;
    const x=i%width,y=Math.floor(i/width);
    for(let dy=-1;dy<=1;dy++) for(let dx=-1;dx<=1;dx++) {
      const nx=x+dx,ny=y+dy;
      if(nx>=0 && nx<width && ny>=0 && ny<height) near[ny*width+nx]=1;
    }
  }
  const alpha=new Uint8ClampedArray(count);
  for(let i=0;i<count;i++) {
    if(!near[i]) continue;
    const t=Math.max(0,Math.min(1,(confidence[i]-0.2)/0.5));
    alpha[i]=Math.round(255*t*t*(3-2*t));
  }
  // Keep real holes transparent instead of automatically filling gaps between limbs.
  return alpha;
}

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
  if (!photoVideoEl.videoWidth) {setStatus("カメラの準備中です", "error");return;}
  captureVisibleFrame(photoCaptureCanvasEl);
  photoVideoEl.pause();
  photoVideoEl.style.display="none";
  capturePhotoBtn.style.display="none";
  retakePhotoBtn.style.display="inline-block";
  showPhotoSelection();
});
autoCutoutBtn.addEventListener("click", async () => {
  if(autoCutoutBtn.disabled || cutoutBusy) return;
  cutoutBusy=true;
  autoCutoutBtn.disabled=true;
  retakePhotoBtn.disabled=true;
  skipPhotoBtn.disabled=true;
  setStatus("囲んだ範囲からキャラクターの輪郭を切り抜いています…");
  try {
    const {canvas,ctx,mask,target}=createSelectedPhoto();
    const original=ctx.getImageData(0,0,canvas.width,canvas.height);
    let usedAi=false;
    try {usedAi=await removeBackgroundWithAi(ctx,canvas.width,canvas.height,target,mask);}
    catch(e) {logDebug("切り抜き: "+e);}
    if(!usedAi) {ctx.putImageData(original,0,0);removeBackground(ctx,canvas.width,canvas.height);}
    ctx.globalCompositeOperation="destination-in";
    ctx.drawImage(mask,0,0);
    ctx.globalCompositeOperation="source-over";
    const pixels=ctx.getImageData(0,0,canvas.width,canvas.height).data;
    if(!pixels.some((v,i)=>i%4===3 && v>128)) throw new Error("被写体が見つかりませんでした。囲みを広げて試してください");
    const trimmed=trimTransparentMargins(canvas);
    cutoutPreviewCanvasEl.width=trimmed.width;cutoutPreviewCanvasEl.height=trimmed.height;
    cutoutPreviewCanvasEl.getContext("2d").drawImage(trimmed,0,0);
    capturedCutoutDataUrl=resizeCanvasToDataUrl(trimmed,500);
    resetPhotoBounds();
    redrawSelectionBtn.style.display="inline-block";
    usePhotoBtn.style.display="inline-block";
    setStatus(usedAi?"輪郭を切り抜きました。「この写真を使う」で進めます":"簡易モードで切り抜きました。結果を確認してください");
  } catch(e) {
    autoCutoutBtn.disabled=false;
    setStatus(e.message || "切り抜きに失敗しました。もう一度試してください", "error");
  } finally {
    cutoutBusy=false;retakePhotoBtn.disabled=false;skipPhotoBtn.disabled=false;
  }
});


retakePhotoBtn.addEventListener("click", () => {
  resetPhotoBounds();
  photoVideoEl.play().catch(() => {});
  capturedCutoutDataUrl = null;

  photoVideoEl.style.display = "block";
  cutoutPreviewCanvasEl.style.display = "none";
  photoTargetEl.style.display = "none";
  capturePhotoBtn.style.display = "inline-block";
  retakePhotoBtn.style.display = "none";
  usePhotoBtn.style.display = "none";

  setStatus("\u307e\u305a\u30ad\u30e3\u30e9\u30af\u30bf\u30fc\u3092\u64ae\u5f71\u3057\u3066\u304f\u3060\u3055\u3044");
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
async function joinBattleQueue(recordData, onJoined = onJoinedBattleQueue, onFull = null) {
  try {
    if (!recordData.uid) throw new Error("認証できていません。ページを再読み込みしてからお試しください");
    // Read before transacting: a fresh client's local null is not proof that a slot is empty.
    // Trying to overwrite an occupied slot is denied by the ownership rules.
    const snapshot = await db.ref(`${TABLE_PATH}/battleSlots`).once("value");
    const slots = snapshot.val() || {};
    for (const slot of ["player1", "player2"]) {
      if (slots[slot]) continue;
      try {
        const result = await db.ref(`${TABLE_PATH}/battleSlots/${slot}`).transaction(
          current => current === null ? recordData : undefined, undefined, false
        );
        if (result.committed) { onJoined(slot); return; }
      } catch (error) {
        // A concurrent entrant may have taken this slot. Try the other slot only in that case.
        const latest = await db.ref(`${TABLE_PATH}/battleSlots/${slot}`).once("value");
        if (!latest.exists()) throw error;
      }
    }
    if (onFull) onFull();
    else setBattleStatus("現在、対戦の順番待ちが満席です。ホームに戻ってからもう一度お試しください", "error");
  } catch (error) {
    logDebug("対戦エントリー失敗: " + error);
    setBattleStatus("対戦に参加できませんでした: " + (error.message || error.code || error) + "。ホームに戻って再度お試しください", "error");
    if (onFull) onFull();
  }
}

/// 対戦キューへの参加に成功した後、相手が揃うまでの状況をリアルタイムに表示する。
/// PC側が試合成立と判定するとbattleSlotsをクリアするので、それを検知したら
/// 「対戦が始まりました」と表示してリスナーを解除する。
function onJoinedBattleQueue(mySlot) {
  const slotLabel = mySlot === "player1" ? "プレイヤー1" : "プレイヤー2";
  setBattleStatus(`対戦キューに参加しました(${slotLabel})。相手を待っています…`, "success");

  myBattleSlot = mySlot;
  startQueueGuards(mySlot);
  const slotsRef = db.ref(`${TABLE_PATH}/battleSlots`);
  const handler = slotsRef.on("value", (snapshot) => {
    const slots = snapshot.val();

    if (slots && slots.player1 && slots.player2) {
      setBattleStatus("対戦相手が見つかりました！これから対戦です", "success");
      return;
    }

    // 自分が登録したはずのスロットが消えている ＝ PC側で試合が成立し、
    // 次の組のためにリセットされた合図。ここからは対戦本編(activeBattle)を見て、
    // 自分の番が来たらこのスマホ上で強/普/弱を選ぶ画面に切り替える。
    // (時間切れ・通信切れ・運営者のリセットで枠が消された場合は、startBattleTurnListener の中で
    //  自分の試合が始まらないことを確かめてから知らせる)
    if (!slots || !slots[mySlot]) {
      detachBattleQueueListener();
      stopQueueGuards();
      startBattleTurnListener(mySlot);
    }
  });
  battleQueueListenerRef = slotsRef;
  battleQueueListenerHandler = handler;
}

/// 対戦本編開始。PC(Unity)側が書き込む/activeBattleを監視し、
/// 自分の番が来たら強/普/弱ボタンを表示する。選択内容はattackerChoice/defenderChoiceに書き込むだけで、
/// ダメージ計算・演出・勝敗判定はすべてPC(Unity)側が行う(このスマホは選択と結果表示のみ)。
function startBattleTurnListener(mySlot) {
  myBattleSlot = mySlot;
  currentMatchId = null;
  currentBattleTurnNumber = null;
  currentBattleTurnRole = null;
  submittedBattleTurnNumber = -1;

  startBattlePresence(mySlot);

  // 試合が成立した時は、会場PCが試合データを用意してから枠を消すので、すぐに自分の試合が見つかる。
  // しばらく見つからなければ、枠は試合以外の理由(待ち時間の上限・通信切れ・運営者のリセット)で消されている。
  clearTimeout(matchConfirmTimer);
  matchConfirmTimer = setTimeout(() => {
    if (currentMatchId || myBattleSlot !== mySlot) return;
    finishBattleView(
      "対戦待ちが解除されました（待ち時間の上限・通信の途切れ・運営者によるリセットのいずれか）。もう一度エントリーしてください",
      "lose"
    );
  }, MATCH_CONFIRM_TIMEOUT_MS);

  battleWaitSectionEl.style.display = "none";
  battleTurnSectionEl.style.display = "block";
  battleTurnResultEl.style.display = "none";
  battleTurnHomeBtn.style.display = "none";
  hideBattleTurnButtons();
  setBattleTurnPrompt("対戦の準備をしています…");

  const ref = db.ref(`${TABLE_PATH}/activeBattle`);
  const handler = ref.on("value", (snapshot) => {
    const data = snapshot.val();

    if (!data || !data.matchId) {
      setBattleTurnPrompt("対戦の準備をしています…");
      hideBattleTurnButtons();
      return;
    }

    // 試合IDの確定。会場PCは枠をクリアする前に新しい試合IDで初期化するので、
    // 最初に見える「開始前/選択中」のデータが自分の試合になる。
    // 前の試合の終了データ(finished/aborted)を自分の試合と取り違えないよう、それらでは確定させない。
    if (!currentMatchId) {
      if (data.status !== "starting" && data.status !== "choosing") return;
      const expectedUid = data[`${mySlot}Uid`];
      const uid = currentUid();
      if (expectedUid && uid && expectedUid !== uid) {
        finishBattleView("この対戦には参加できませんでした。もう一度エントリーしてください", "lose");
        return;
      }
      currentMatchId = data.matchId;
    }
    if (data.matchId !== currentMatchId) return;

    if (data.status === "aborted") {
      finishBattleView("対戦は運営者により中断されました。もう一度エントリーしてください", "lose");
      return;
    }

    if (data.status === "finished") {
      const reason =
        data.endReason === "judgement"
          ? "（残りHPによる判定）"
          : data.endReason === "forfeit"
            ? "（応答なしによる不戦敗/不戦勝）"
            : "";
      if (data.winnerSlot === "draw") {
        finishBattleView("引き分け" + reason, "win");
      } else if (data.winnerSlot === mySlot) {
        finishBattleView("勝利！おめでとうございます🎉" + reason, "win");
      } else {
        finishBattleView("敗北…また挑戦してください" + reason, "lose");
      }
      return;
    }

    if (data.status !== "choosing") {
      setBattleTurnPrompt("対戦の準備をしています…");
      hideBattleTurnButtons();
      return;
    }

    const turnChanged = currentBattleTurnNumber !== data.turn;
    currentBattleTurnNumber = data.turn;

    const isAttacker = data.attackerSlot === mySlot;
    const isDefender = data.defenderSlot === mySlot;

    if (!isAttacker && !isDefender) {
      currentBattleTurnRole = null;
      stopTurnCountdown();
      setBattleTurnPrompt("相手のターンです。PC画面をご覧ください");
      hideBattleTurnButtons();
      return;
    }

    if (submittedBattleTurnNumber === data.turn) {
      stopTurnCountdown();
      setBattleTurnPrompt("相手の選択を待っています…");
      hideBattleTurnButtons();
      return;
    }

    currentBattleTurnRole = isAttacker ? "attacker" : "defender";
    showBattleTurnButtons();
    if (turnChanged) startTurnCountdown(data.timeoutSeconds || 30, isAttacker);
  });

  battleTurnListenerRef = ref;
  battleTurnListenerHandler = handler;
}

/// 対戦の終了(勝敗・中断)を表示して、監視をやめる。
function finishBattleView(text, type) {
  detachBattleTurnListener();
  stopTurnCountdown();
  hideBattleTurnButtons();
  setBattleTurnPrompt("");
  battleTurnResultEl.textContent = text;
  battleTurnResultEl.className = "battle-turn-result " + type;
  battleTurnResultEl.style.display = "block";
  battleTurnHomeBtn.style.display = "block";
}

/// 選択の制限時間を表示する(時間切れの判定は会場PCが行い、未選択の側は「普」になる)。
function startTurnCountdown(seconds, isAttacker) {
  stopTurnCountdown();
  const special = myBattleRecord ? ATTACK_LEVEL_LABELS[specialLevelOf(myBattleRecord)] : null;
  const base = isAttacker
    ? "攻撃の強さを選んでください！" + (special ? `（必殺技レベル「${special}」）` : "")
    : "防御の強さを選んでください！";
  const deadline = Date.now() + seconds * 1000;
  const render = () => {
    const remaining = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
    setBattleTurnPrompt(`${base} 残り${remaining}秒`);
    if (remaining === 0) stopTurnCountdown();
  };
  render();
  turnCountdownTimer = setInterval(render, 500);
}

function stopTurnCountdown() {
  if (turnCountdownTimer) clearInterval(turnCountdownTimer);
  turnCountdownTimer = null;
}

function detachBattleTurnListener() {
  if (battleTurnListenerRef && battleTurnListenerHandler) {
    battleTurnListenerRef.off("value", battleTurnListenerHandler);
  }
  battleTurnListenerRef = null;
  battleTurnListenerHandler = null;
  clearTimeout(matchConfirmTimer);
  stopTurnCountdown();
  stopBattlePresence();
}

const MATCH_CONFIRM_TIMEOUT_MS = 15000;
let matchConfirmTimer = null;

// ==================== 対戦中の生存信号 ====================
// 対戦中は activeBattle/presence/{自分の枠} に数秒ごとにサーバー時刻を書き込む。
// 通信不良や画面ロックで信号が数秒途絶えると、Unity側がこのプレイヤーを自動で操作する。
// 信号が戻れば、またこのスマホで選べるようになる。
const BATTLE_PRESENCE_INTERVAL_MS = 2000;
let battlePresenceTimer = null;
let battlePresenceSlot = null;

function sendBattlePresence() {
  if (!battlePresenceSlot) return;
  db.ref(`${TABLE_PATH}/activeBattle/presence/${battlePresenceSlot}`)
    .set(firebase.database.ServerValue.TIMESTAMP)
    .catch((e) => logDebug("エラー(対戦中の生存信号): " + e));
}

function startBattlePresence(slot) {
  stopBattlePresence();
  battlePresenceSlot = slot;
  sendBattlePresence();
  battlePresenceTimer = setInterval(sendBattlePresence, BATTLE_PRESENCE_INTERVAL_MS);
}

function stopBattlePresence() {
  if (battlePresenceTimer) clearInterval(battlePresenceTimer);
  battlePresenceTimer = null;
  battlePresenceSlot = null;
}

// 画面ロックやアプリ切り替えから戻ったら、すぐに「戻ってきた」ことを伝える
document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "visible") sendBattlePresence();
});

function setBattleTurnPrompt(text) {
  battleTurnPromptEl.textContent = text;
}

function showBattleTurnButtons() {
  battleTurnButtonsEl.style.display = "flex";
}

function hideBattleTurnButtons() {
  battleTurnButtonsEl.style.display = "none";
}

/// 強/普/弱ボタンが押された時の処理。
/// 選択はターン番号ごとの場所(activeBattle/choices/{turn}/{attacker|defender})に書き込むため、
/// 通信が遅れて届いても次のターンの選択として扱われることはない。
function submitBattleTurnChoice(level) {
  if (currentBattleTurnRole == null || currentBattleTurnNumber == null) return;

  const turn = currentBattleTurnNumber;
  submittedBattleTurnNumber = turn;
  stopTurnCountdown();
  hideBattleTurnButtons();
  setBattleTurnPrompt("相手の選択を待っています…");

  db.ref(`${TABLE_PATH}/activeBattle/choices/${turn}/${currentBattleTurnRole}`)
    .set(level)
    .catch((e) => {
      logDebug("エラー(選択の書き込み): " + e);
      // 書き込めなかった場合は、もう一度選べるようにする
      if (currentBattleTurnNumber === turn) {
        submittedBattleTurnNumber = -1;
        setBattleTurnPrompt("送信に失敗しました。もう一度選んでください");
        showBattleTurnButtons();
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

  // 履歴(/characters)には写真を含めない。写真は characterByCard の最新1件だけに保存し、
  // イベント終了後の削除対象を限定する(tools/purge_event_data.py)。
  const { photoDataUrl: _omitPhoto, ...historyData } = recordData;
  db.ref("characters")
    .push(historyData)
    .catch((e) => {
      logDebug("エラー(Firebase保存、表示は続行します): " + e);
    });

  // Unity(バトルシーン)がQRコードをスキャンした際にcardIdだけでこのキャラクターを
  // 引けるよう、cardIdをキーにした最新スナップショットも別途保存しておく。
  // (登録済みのカードは confirmOverwriteIfRegistered で弾くので、ここで上書きされることはない)
  db.ref("characterByCard/" + scannedCardData.cardId)
    .set(recordData)
    .catch((e) => {
      logDebug("エラー(characterByCard保存、表示は続行します): " + e);
    });

  // キャラクター作成はここで完了。対戦キューへの参加は「Battleを始める」モードで
  // 改めてこのカードのQRを読み取った時に行う(characterByCardの内容をそのまま使う)。

  if (DEV_MODE) rememberDevCard(scannedCardData.cardId);

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

createCharacterBtn.addEventListener("click", () => {
  startCreateMode();
});

startBattleBtn.addEventListener("click", () => {
  startBattleMode();
});

backToHomeBtn.addEventListener("click", () => {
  goHome();
});

battleHomeBtn.addEventListener("click", () => {
  goHome();
});

battleTurnStrongBtn.addEventListener("click", () => {
  submitBattleTurnChoice("Strong");
});

battleTurnNormalBtn.addEventListener("click", () => {
  submitBattleTurnChoice("Normal");
});

battleTurnWeakBtn.addEventListener("click", () => {
  submitBattleTurnChoice("Weak");
});

battleTurnHomeBtn.addEventListener("click", () => {
  goHome();
});

revealHomeBtn.addEventListener("click", () => {
  goHome();
});

// ==================== 開発者モード(?dev=1) ====================
// QRコードを読み取る代わりに、カードIDの手入力やテストカードの発行で操作する。
// 「CPU相手を呼ぶ」は、このスマホと同じ匿名ユーザーとしてもう一方の枠に参加し、
// 自分の番が来たら自動で「強/普/弱」を選ぶ。Database Rules上も本人の書き込みとして扱われる。

let devBot = null; // { slot, name, timestamp, specialLevel, ref, handler, answeredTurn, timer }

function randomSeed() {
  const values = new Uint32Array(1);
  crypto.getRandomValues(values);
  return values[0] & 0x7fffffff; // C#のintでも扱える正の値
}

function newDevCardId() {
  return "dev-" + randomSeed().toString(16).padStart(8, "0");
}

/// 未登録のカードIDから、毎回同じシード値を作る(同じIDで登録し直しても能力値が変わらないように)
function seedFromText(text) {
  let hash = 2166136261;
  for (let i = 0; i < text.length; i++) {
    hash ^= text.charCodeAt(i);
    hash = Math.imul(hash, 16777619);
  }
  return (hash >>> 0) & 0x7fffffff;
}

function rememberDevCard(cardId) {
  try {
    localStorage.setItem(DEV_LAST_CARD_KEY, cardId);
  } catch (e) {
    logDebug("最後のテストカードIDを保存できませんでした: " + e);
  }
  if (devCardIdInput) devCardIdInput.value = cardId;
}

function lastDevCard() {
  try {
    return localStorage.getItem(DEV_LAST_CARD_KEY) || "";
  } catch (e) {
    return "";
  }
}

function isValidCardId(cardId) {
  return /^[A-Za-z0-9_-]{1,64}$/.test(cardId);
}

/// 「このIDで登録」: QRを読み取った時と同じく、撮影 → 名前入力 → 登録 の流れに進む。
function devRegister() {
  const typed = devCardIdInput.value.trim();
  if (typed && !isValidCardId(typed)) {
    setStatus("カードIDは英数字・ハイフン・アンダースコアで入力してください", "error");
    return;
  }
  const cardId = typed || newDevCardId();
  setStatus("カード情報を確認しています...");
  db.ref("characterByCard/" + cardId)
    .once("value")
    .then((snapshot) => {
      const existing = snapshot.val();
      const seed = existing && existing.seed != null ? existing.seed : typed ? seedFromText(typed) : randomSeed();
      appMode = "create";
      homeSectionEl.style.display = "none";
      backToHomeBtn.style.display = "inline-block";
      logDebug(`開発者モード: QRの代わりにカード ${cardId} (seed=${seed}) を使います`);
      confirmOverwriteIfRegistered({ cardId, seed });
    })
    .catch((e) => {
      logDebug("エラー(開発者モードの登録確認): " + e);
      setStatus("通信に失敗しました。もう一度お試しください", "error");
    });
}

/// 「このIDで対戦」: 登録済みのカードで対戦キューに参加する(QRを読み取った時と同じ処理)。
function devBattle() {
  const cardId = devCardIdInput.value.trim() || lastDevCard();
  if (!cardId) {
    setStatus("カードIDを入力するか、先に「このIDで登録」でテストカードを登録してください", "error");
    return;
  }
  if (!isValidCardId(cardId)) {
    setStatus("カードIDは英数字・ハイフン・アンダースコアで入力してください", "error");
    return;
  }
  appMode = "battle";
  homeSectionEl.style.display = "none";
  backToHomeBtn.style.display = "inline-block";
  handleBattleQrScanned({ cardId });
}

/// 「テストキャラで今すぐ対戦」: 登録を省略し、その場で作ったキャラクターで対戦キューに参加する。
function devQuickBattle() {
  const seed = randomSeed();
  const stats = generateCharacterStats(seed, "テスト" + String(seed % 1000).padStart(3, "0"));
  const record = { cardId: newDevCardId(), seed, timestamp: Date.now(), ...stats };
  appMode = "battle";
  homeSectionEl.style.display = "none";
  myBattleRecord = record;
  showBattleWait(record);
  myQueueJoinedAt = Date.now();
  joinBattleQueue({ ...record, uid: currentUid(), timestamp: myQueueJoinedAt });
}

/// 「CPU相手を呼ぶ」: 空いている枠にCPUを参加させ、CPUの番では自動で選択する。
function devCallCpu() {
  if (devBot) {
    setStatus("CPU相手は既に参加しています");
    return;
  }
  const seed = randomSeed();
  const stats = generateCharacterStats(seed, "CPU" + String(seed % 1000).padStart(3, "0"));
  const timestamp = Date.now();
  const record = { cardId: newDevCardId(), seed, timestamp, ...stats, uid: currentUid() };
  setStatus("CPU相手を呼んでいます...");
  joinBattleQueue(
    record,
    (slot) => {
      devBot = {
        slot,
        name: record.characterName,
        timestamp,
        specialLevel: record.specialLevel,
        ref: null,
        handler: null,
        answeredTurn: null,
        timer: null,
      };
      startDevBot();
      if (battleWaitCpuBtn) battleWaitCpuBtn.style.display = "none";
      const slotLabel = slot === "player1" ? "プレイヤー1" : "プレイヤー2";
      setStatus(`CPU相手「${record.characterName}」が${slotLabel}として参加しました`, "success");
    },
    () => setStatus("空いている枠がありません。先に対戦キューを空けてください", "error")
  );
}

function startDevBot() {
  const ref = db.ref(`${TABLE_PATH}/activeBattle`);
  const handler = ref.on("value", (snapshot) => {
    const data = snapshot.val();
    if (!devBot || !data || data[`${devBot.slot}Name`] !== devBot.name) return; // CPUが参加している試合だけを見る

    if (data.status === "finished" || data.status === "aborted") {
      logDebug("開発者モード: CPU相手の試合が終わりました");
      stopDevBot(false);
      return;
    }
    if (data.status !== "choosing" || data.turnKey == null || devBot.answeredTurn === data.turnKey) return;

    const role = data.attackerSlot === devBot.slot ? "attacker" : data.defenderSlot === devBot.slot ? "defender" : null;
    if (!role) return;

    const turnKey = data.turnKey;
    devBot.answeredTurn = turnKey;
    // 人が考えているように少し間を置いてから選ぶ。攻撃時はやや必殺技レベルを狙う
    const level =
      role === "attacker" && Math.random() < 0.4 ? devBot.specialLevel : ATTACK_LEVELS[Math.floor(Math.random() * 3)];
    devBot.timer = setTimeout(() => {
      db.ref(`${TABLE_PATH}/activeBattle/choices/${turnKey}/${role}`)
        .set(level)
        .then(() => logDebug(`開発者モード: CPUが${role === "attacker" ? "攻撃" : "防御"}で「${ATTACK_LEVEL_LABELS[level]}」を選びました`))
        .catch((e) => logDebug("エラー(CPUの選択の書き込み): " + e));
    }, 1200 + Math.random() * 2500);
  });
  devBot.ref = ref;
  devBot.handler = handler;
}

/// CPU相手を止める。leaveSlotなら、まだ試合が始まっていない場合に枠を空ける。
function stopDevBot(leaveSlot) {
  if (!devBot) return;
  const bot = devBot;
  devBot = null;
  if (bot.timer) clearTimeout(bot.timer);
  if (bot.ref && bot.handler) bot.ref.off("value", bot.handler);
  if (!leaveSlot) return;
  db.ref(`${TABLE_PATH}/battleSlots/${bot.slot}`)
    .transaction((current) => {
      if (current === null) return null;
      return current.timestamp === bot.timestamp ? null : undefined;
    })
    .catch((e) => logDebug("エラー(CPU相手の枠を空ける): " + e));
}

if (DEV_MODE) {
  devRegisterBtn.addEventListener("click", devRegister);
  devBattleBtn.addEventListener("click", devBattle);
  devQuickBattleBtn.addEventListener("click", devQuickBattle);
  devCpuBtn.addEventListener("click", devCallCpu);
  battleWaitCpuBtn.addEventListener("click", devCallCpu);
  devCardIdInput.value = lastDevCard();
}

init();
