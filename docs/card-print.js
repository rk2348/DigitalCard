/* QR encoder: vendor/qrcode.js (qrcode-generator 1.4.4, MIT). */
const CardPrint = (() => {
  let record = null;
  let dialog = null;
  function setCard(value) {
    record = { ...value };
    attachQr(document.querySelector(".reveal-card-front"));
  }

  /// カード表面の右下のQR枠に、対戦用のQRコードを入れる(画面表示と印刷で共通)。
  function attachQr(front) {
    if (!front) return;
    front.querySelectorAll(".card-qr").forEach(el => el.remove());
    if (!record || !record.cardId || record.seed == null || typeof qrcode === "undefined") return;
    const qr = qrcode(0, "M");
    qr.addData(JSON.stringify({ cardId: record.cardId, seed: record.seed, dataVersion: 1 }));
    qr.make();
    const code = document.createElement("div");
    code.className = "card-qr";
    // The four-module white quiet zone is required for reliable scanning.
    code.innerHTML = qr.createSvgTag({ cellSize: 4, margin: 16, scalable: true });
    code.setAttribute("aria-label", "対戦用QRコード");
    front.append(code);
  }

  async function open(front) {
    if (!record || dialog) return;
    dialog = document.createElement("dialog");
    dialog.className = "card-print-dialog";
    const heading = document.createElement("h2");
    heading.textContent = "カードの印刷";
    const hint = document.createElement("p");
    hint.textContent = "A4用紙にカード1枚を大きく印刷します（約200 × 279 mm）。A4・縦向き・倍率100%・ヘッダーとフッターなしで印刷してください。「PDFに保存」も選べます。";
    const sheet = document.createElement("div");
    sheet.className = "card-print-sheet";
    const card = document.createElement("div");
    card.className = "print-card";
    const clone = front.cloneNode(true);
    clone.removeAttribute("id");
    clone.querySelectorAll("[id]").forEach(el => el.removeAttribute("id"));
    attachQr(clone); // 画面のカードからコピーしたQRを作り直して、必ず1つだけにする
    card.append(clone);
    sheet.append(card);
    const actions = document.createElement("div");
    actions.className = "print-actions";
    const print = document.createElement("button");
    print.textContent = "画像を準備中…";
    print.disabled = true;
    print.onclick = () => window.print();
    const close = document.createElement("button");
    close.textContent = "戻る";
    close.onclick = () => dialog.close();
    actions.append(print, close);
    dialog.append(heading, hint, sheet, actions);
    dialog.addEventListener("close", () => {
      document.body.classList.remove("card-printing");
      dialog.remove();
      dialog = null;
    });
    document.body.append(dialog);
    document.body.classList.add("card-printing");
    dialog.showModal();
    const current = dialog;
    try {
      await Promise.all(Array.from(clone.querySelectorAll("img"))
        .filter(img => img.getAttribute("src") && img.style.display !== "none")
        .map(img => img.decode()));
      await document.fonts.ready;
      if (dialog !== current) return;
      print.disabled = false;
      print.textContent = "印刷 / PDFに保存";
    } catch (error) {
      if (dialog !== current) return;
      hint.textContent = "カード画像を読み込めませんでした。戻ってからもう一度お試しください。";
      print.textContent = "画像の読み込みに失敗";
    }
  }
  return { setCard, open };
})();
