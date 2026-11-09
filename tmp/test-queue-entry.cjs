const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync('docs/app.js','utf8');
const start=source.indexOf('async function joinBattleQueue(');
const end=source.indexOf('function onJoinedBattleQueue(',start);
async function run(slots,mode,uid='user') {
  const attempted=[],messages=[],joined=[];
  const context={TABLE_PATH:'tables/table1',onJoinedBattleQueue:s=>joined.push(s),setBattleStatus:m=>messages.push(m),logDebug(){},
    db:{ref:path=>({once:async()=>({val:()=>slots,exists:()=>mode==='race'}),transaction:async update=>{
      attempted.push(path.split('/').pop());
      if(mode==='denied')throw Error('PERMISSION_DENIED');
      if(mode==='race' && attempted.length===1)throw Error('PERMISSION_DENIED');
      assert.equal(update({uid:'other'}),undefined,'occupied slots cannot be overwritten');
      return {committed:!!update(null)};
    }})}};
  vm.createContext(context);vm.runInContext(source.slice(start,end),context);
  await context.joinBattleQueue({uid});return {attempted,messages,joined};
}
(async()=>{
  let result=await run({player1:{uid:'other'}},'ok');assert.deepEqual(result.attempted,['player2']);assert.deepEqual(result.joined,['player2']);
  result=await run({},'race');assert.deepEqual(result.joined,['player2']);
  result=await run({},'denied');assert(result.messages.some(m=>m.includes('PERMISSION_DENIED')));
  result=await run({player1:{},player2:{}},'ok');assert.equal(result.attempted.length,0);assert.equal(result.messages.length,1);
  result=await run({},'ok',null);assert.equal(result.attempted.length,0);assert.equal(result.messages.length,1);
  console.log('PASS: second-slot entry, concurrent entry, visible permissions error, full queue, missing authentication');
})().catch(e=>{console.error(e);process.exitCode=1;});
