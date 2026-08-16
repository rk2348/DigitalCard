// オリサモ カードスキャン用ページ
// スマホのカメラでQRコード(カードID＋シード値のJSON)を読み取り、
// Firebase Realtime Databaseの pendingRegistrations に書き込む。
// Unity側(FirebaseCardListener.cs)がそれをポーリングしてキャラクターを確定させ、
// results に結果を書き込むので、それをリアルタイムで購読して画面に表示する。

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

// Unity側が結果を書き込むまでの最大待ち時間(ミリ秒)。
// Unity(QRScanシーン)が起動していない場合に、ずっと「登録中...」のままにならないようにする。
const RESULT_TIMEOUT_MS = 20000;

const statusEl = document.getElementById("status");
const debugLogEl = document.getElementById("debug-log");
const readerEl = document.getElementById("reader");
const nameInputSectionEl = document.getElementById("name-input-section");
const scannedCardIdEl = document.getElementById("scanned-card-id");
const characterNameInput = document.getElementById("character-name");
const registerBtn = document.getElementById("register-btn");
const rescanBtn = document.getElementById("rescan-btn");
const statusDisplaySectionEl = document.getElementById("status-display-section");
const mutationBadgeEl = document.getElementById("mutation-badge");
const resultCharacterNameEl = document.getElementById("result-character-name");
const resultElementEl = document.getElementById("result-element");
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
let activeResultRef = null; // 結果待ち中のFirebase参照（タイムアウト時等に購読解除するため保持）
let resultTimeoutHandle = null;

// Unity側から返ってくる属性名(英語)を日本語表示に変換するためのマップ
const ELEMENT_LABELS = {
  Fire: "炎",
  Wind: "風",
  Thunder: "雷",
  Water: "水",
  Earth: "土",
  Light: "光",
};

let db = null;

function setStatus(text, type = "info") {
  statusEl.textContent = text;
  statusEl.className = "status " + type;
}

/// 画面上にもエラーの詳細を表示する（スマホだと開発者ツールが見れないため）。
function logDebug(text) {
  console.log(text);
  if (debugLogEl) {
    debugLogEl.style.display = "block";
    debugLogEl.textContent += text + "\n";
  }
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
  cleanupResultListener();

  isAwaitingName = false;
  scannedCardData = null;

  nameInputSectionEl.style.display = "none";
  statusDisplaySectionEl.style.display = "none";
  readerEl.style.display = "block";
  setStatus("QRコードをカメラにかざしてください");
}

/// 結果待ちの購読とタイムアウトタイマーを片付ける
function cleanupResultListener() {
  if (activeResultRef) {
    activeResultRef.off();
    activeResultRef = null;
  }
  if (resultTimeoutHandle) {
    clearTimeout(resultTimeoutHandle);
    resultTimeoutHandle = null;
  }
}

/// Unityから返ってきたキャラクターステータスを画面に表示する
function showStatusDisplay(stats) {
  mutationBadgeEl.style.display = stats.isMutation ? "block" : "none";
  resultCharacterNameEl.textContent = stats.characterName;
  resultElementEl.textContent = "属性: " + (ELEMENT_LABELS[stats.element] || stats.element);
  resultAttackEl.textContent = stats.attack;
  resultDefenseEl.textContent = stats.defense;
  resultSpeedEl.textContent = stats.speed;
  resultHpEl.textContent = stats.hp + " / " + stats.maxHp;
  resultSkillNameEl.textContent = "スキル「" + stats.skillName + "」";
  resultSkillDescriptionEl.textContent = stats.skillDescription;

  nameInputSectionEl.style.display = "none";
  statusDisplaySectionEl.style.display = "block";
  setStatus(stats.characterName + " が誕生した！", "success");
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
  setStatus("登録中...パソコン側で処理されるまでお待ちください");

  const pendingRef = db.ref("pendingRegistrations").push();
  const payload = {
    cardId: scannedCardData.cardId,
    seed: scannedCardData.seed,
    characterName: name,
    timestamp: Date.now(),
  };

  pendingRef
    .set(payload)
    .then(() => {
      logDebug("Firebaseへ書き込みました: " + JSON.stringify(payload));
      waitForResult(pendingRef.key);
    })
    .catch((e) => {
      setStatus("Firebaseへの送信に失敗しました: " + e.message, "error");
      logDebug("エラー(Firebase書き込み): " + e);
      isSending = false;
      registerBtn.disabled = false;
    });
});

/// pendingRegistrationsへの書き込み後、対応する results/{key} をリアルタイムで購読し、
/// Unity側の処理結果が書き込まれたらステータス画面を表示する。
function waitForResult(key) {
  const resultRef = db.ref("results/" + key);
  activeResultRef = resultRef;

  resultTimeoutHandle = setTimeout(() => {
    cleanupResultListener();
    setStatus(
      "パソコン側からの応答がありませんでした。Unity(QRScanシーン)が起動しているか確認してください。",
      "error"
    );
    logDebug("エラー: results/" + key + " の待受がタイムアウトしました");
    isSending = false;
    registerBtn.disabled = false;
  }, RESULT_TIMEOUT_MS);

  resultRef.on("value", (snapshot) => {
    const data = snapshot.val();
    if (!data) return; // まだUnity側が処理していない

    cleanupResultListener();
    isSending = false;
    registerBtn.disabled = false;

    if (data.status === "ok") {
      showStatusDisplay(data);
    } else {
      setStatus("登録に失敗しました（" + (data.message || "不明なエラー") + "）", "error");
      logDebug("エラー(登録): " + (data.message || "不明なエラー"));
    }

    // 結果ノードは表示に使い終わったら消してよい(Firebase側の掃除)
    resultRef.remove().catch(() => {});
  });
}

rescanBtn.addEventListener("click", () => {
  resetToScanning();
});

nextScanBtn.addEventListener("click", () => {
  resetToScanning();
});

init();
