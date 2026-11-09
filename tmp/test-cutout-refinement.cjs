const fs=require('fs'),vm=require('vm'),assert=require('assert');
const context={};vm.createContext(context);
vm.runInContext(fs.readFileSync('tmp/refine-cutout.js','utf8'),context);
const refine=context.refineMaskToAlpha,w=100,h=100;
const c=new Float32Array(w*h);
const rect=(x0,y0,x1,y1,value)=>{for(let y=y0;y<y1;y++)for(let x=x0;x<x1;x++)c[y*w+x]=value;};
rect(30,30,60,70,0.95);
// Low-confidence narrow bridge and a separate confident accessory.
rect(60,48,78,50,0.45);rect(78,45,84,53,0.9);
rect(15,40,20,45,0.9);
// A small genuine hole must remain open, even if enclosed by the body.
rect(42,48,46,52,0.03);
c[5*w+5]=0.99;
const alpha=refine(c,w,h,45,40);
assert(alpha,'foreground detected');
assert(alpha[49*w+70]>0,'thin uncertain bridge retained');
assert(alpha[42*w+17]>200,'separate confident accessory retained');
assert.equal(alpha[50*w+44],0,'real hole stays transparent');
assert.equal(alpha[5*w+5],0,'isolated speck removed');
assert.equal(alpha[0],0,'background stays transparent');
assert.equal(refine(new Float32Array(w*h),w,h,50,50),null,'empty model result rejected');
const diagonal=new Float32Array(w*h);
for(let j=10;j<40;j++) diagonal[j*w+j]=0.9;
assert(refine(diagonal,w,h,20,20)[30*w+30]>200,'diagonal thin part retained');
// The full-file function must be exactly the implementation exercised here.
const source=fs.readFileSync('docs/app.js','utf8');
assert(source.includes(fs.readFileSync('tmp/refine-cutout.js','utf8').trim()));
assert(source.includes('removeBackgroundWithAi(ctx,canvas.width,canvas.height,target,mask)'));
console.log('PASS: thin bridge, detached part, real hole, noise removal, empty mask, diagonal connection, production integration');
