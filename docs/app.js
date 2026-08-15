// オリサモ カードスキャン用ページ
// スマホのカメラでQRコード(カードID＋シード値のJSON)を読み取り、
// Unity側で起動しているHTTPサーバー(QRWebServer.cs)に POST /scan で送信する。

const SERVER_URL_KEY = "orisamo_server_url";

const serverUrlInput = document.getElementById("server-url");
const saveServerBtn = document.getElementById("save-server-btn");
const statusEl = document.getElementById("status");
const debugLogEl = document.getElementById("debug-log");

let scanner = null;
let isSending = false;

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

async function onScanSuccess(decodedText) {
  if (isSending) return; // 連続送信を防止
  isSending = true;

  logDebug("QRコードを読み取りました: " + decodedText);

  let cardData;
  try {
    cardData = JSON.parse(decodedText);
  } catch (e) {
    setStatus("読み取れましたが、カードの形式が正しくありません", "error");
    isSending = false;
    return;
  }

  if (!cardData || !cardData.cardId) {
    setStatus("読み取れましたが、カードの形式が正しくありません", "error");
    isSending = false;
    return;
  }

  const serverUrl = getServerUrl();
  setStatus("送信中...");

  try {
    const res = await fetch(serverUrl + "/scan", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: decodedText,
    });

    if (res.ok) {
      setStatus("送信しました！パソコンの画面を確認してください", "success");
    } else {
      setStatus("送信に失敗しました（サーバーエラー: " + res.status + "）", "error");
      logDebug("エラー(送信): サーバーが" + res.status + "を返しました");
    }
  } catch (e) {
    setStatus(
      "サーバーに接続できませんでした。パソコンと同じWi-Fiに接続しているか、アドレスが正しいか確認してください。",
      "error"
    );
    logDebug("エラー(送信): " + e);
  }

  // 少し待ってから再度スキャンできるようにする（同じカードの連続送信防止）
  setTimeout(() => {
    isSending = false;
    setStatus("QRコードをカメラにかざしてください");
  }, 2000);
}

init();
