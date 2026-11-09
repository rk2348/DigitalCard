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
