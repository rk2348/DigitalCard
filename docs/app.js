// オリサモ カードスキャン用ページ
// スマホのカメラでQRコード(カードID＋シード値のJSON)を読み取り、
// Unity側で起動しているHTTPサーバー(QRWebServer.cs)に POST /scan で送信する。

const SERVER_URL_KEY = "orisamo_server_url";

const serverUrlInput = document.getElementById("server-url");
const saveServerBtn = document.getElementById("save-server-btn");
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
let isSending = false; // サーバーへの送信中（多重送信防止）
let isAwaitingName = false; // QR読み取り済み・名前入力待ち（この間はスキャン結果を無視する）
let scannedCardData = null; // QRから読み取ったカード情報(cardId, seedなど)。名前入力後にこれへcharacterNameを足して送信する

// Unity側から返ってくる属性名(英語)を日本語表示に変換するためのマップ
const ELEMENT_LABELS = {
  Fire: "炎",
  Wind: "風",
  Thunder: "雷",
  Water: "水",
  Earth: "土",
  Light: "光",
};

function getServerUrl() {
  return localStorage.getItem(SERVER_URL_KEY) || "";
}

function setServerUrl(url) {
  localStorage.setItem(SERVER_URL_KEY, url);
}

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

  // Unity側で表示する接続用QRコードを読み取って開いた場合、
  // ?server=http://... というクエリパラメータでサーバーアドレスが渡ってくる
  const params = new URLSearchParams(window.location.search);
  const paramServer = params.get("server");
  if (paramServer) {
    setServerUrl(paramServer);
    logDebug("URLパラメータからサーバーアドレスを設定しました: " + paramServer);
  }

  const savedUrl = getServerUrl();
  if (savedUrl) {
    serverUrlInput.value = savedUrl;
    setStatus("QRコードをカメラにかざしてください");
    startScanner();
  } else {
    setStatus("サーバーのアドレスを設定してください");
  }
}

saveServerBtn.addEventListener("click", () => {
  logDebug("「保存してスキャン開始」が押されました。");

  const url = serverUrlInput.value.trim().replace(/\/$/, "");
  if (!url) {
    setStatus("サーバーのアドレスを入力してください", "error");
    return;
  }
  setServerUrl(url);
  setStatus("QRコードをカメラにかざしてください");
  startScanner();
});

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
  // 送信中、または既に読み取り済みで名前入力待ちの間は、続けて読み取っても無視する
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

  nameInputSectionEl.style.display = "none";
  statusDisplaySectionEl.style.display = "none";
  readerEl.style.display = "block";
  setStatus("QRコードをカメラにかざしてください");
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

registerBtn.addEventListener("click", async () => {
  const name = characterNameInput.value.trim();
  if (!name) {
    setStatus("名前を入力してください", "error");
    characterNameInput.focus();
    return;
  }

  if (isSending) return;
  isSending = true;
  registerBtn.disabled = true;

  const payload = { ...scannedCardData, characterName: name };
  const serverUrl = getServerUrl();
  setStatus("登録中...");

  try {
    const res = await fetch(serverUrl + "/scan", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });

    let data = null;
    try {
      data = await res.json();
    } catch (parseErr) {
      logDebug("エラー(レスポンス解析): " + parseErr);
    }

    if (res.ok && data && data.status === "ok") {
      showStatusDisplay(data);
    } else {
      const message = data && data.message ? data.message : "サーバーエラー: " + res.status;
      setStatus("登録に失敗しました（" + message + "）", "error");
      logDebug("エラー(登録): " + message);
    }
  } catch (e) {
    setStatus(
      "サーバーに接続できませんでした。パソコンと同じWi-Fi（またはトンネルURL）に接続しているか、アドレスが正しいか確認してください。",
      "error"
    );
    logDebug("エラー(送信): " + e);
  }

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
