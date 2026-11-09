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
