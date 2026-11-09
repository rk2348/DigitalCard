const fs=require('fs'),cp=require('child_process');
function api(path,data){const args=['api','repos/rk2348/DigitalCard/'+path];if(data)args.push('--method','POST','--input','-');return JSON.parse(cp.execFileSync('gh',args,{input:data?JSON.stringify(data):undefined,encoding:'utf8'}));}
const ref=api('git/ref/heads/main'),head=api('git/commits/'+ref.object.sha);
const tree=api('git/trees',{base_tree:head.tree.sha,tree:['docs/app.js','docs/index.html','docs/style.css'].map(path=>({path,mode:'100644',type:'blob',content:fs.readFileSync(path,'utf8')}))});
const commit=api('git/commits',{message:'Add drag selection bounds for automatic character cutout',tree:tree.sha,parents:[ref.object.sha]});
api('git/refs/heads/main',{sha:commit.sha,force:false});
console.log(commit.sha);
