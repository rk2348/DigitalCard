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
    try {usedAi=await removeBackgroundWithAi(ctx,canvas.width,canvas.height,target);}
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

