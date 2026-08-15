// オリサモ カードスキャン用ページ
// スマホのカメラでQRコード(カードID＋シード値のJSON)を読み取り、
// Unity側で起動しているHTTPサーバー(QRWebServer.cs)に POST /scan で送信する。

const SERVER_URL_KEY = "orisamo_server_url";

const serverUrlInput = document.getElementById("server-url");
const saveServerBtn = document.getElementById("save-server-btn");
const statusEl = document.getElementById("status");

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

function init() {
  // Unity側で表示する接続用QRコードを読み取って開いた場合、
  // ?server=http://... というクエリパラメータでサーバーアドレスが渡ってくる
  const params = new URLSearchParams(window.location.search);
  const paramServer = params.get("server");
  if (paramServer) {
    setServerUrl(paramServer);
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
  if (scanner) return; // 既に起動済み

  scanner = new Html5Qrcode("reader");
  const config = { fps: 10, qrbox: { width: 250, height: 250 } };

  scanner
    .start({ facingMode: "environment" }, config, onScanSuccess, onScanFailure)
    .catch((err) => {
      setStatus("カメラを起動できませんでした: " + err, "error");
    });
}

function onScanFailure() {
  // 1フレームごとに呼ばれるが、単に読み取れていないだけなので何もしない
}

async function onScanSuccess(decodedText) {
  if (isSending) return; // 連続送信を防止
  isSending = true;

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
    }
  } catch (e) {
    setStatus(
      "サーバーに接続できませんでした。パソコンと同じWi-Fiに接続しているか、アドレスが正しいか確認してください。",
      "error"
    );
  }

  // 少し待ってから再度スキャンできるようにする（同じカードの連続送信防止）
  setTimeout(() => {
    isSending = false;
    setStatus("QRコードをカメラにかざしてください");
  }, 2000);
}

init();
