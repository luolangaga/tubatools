import test from 'node:test';
import assert from 'node:assert/strict';
const base='http://127.0.0.1:8791';
test('brand API: empty brands, validation, submit, aggregate, throttle, moderation',async()=>{
 let r=await fetch(base+'/brands'); assert.equal(r.status,200); let brands=await r.json(); assert.equal(brands.length,20);
 r=await fetch(base+'/brands/lenovo/reviews',{method:'POST',headers:{'Content-Type':'application/json'},body:'null'}); assert.equal(r.status,400);
 const device=crypto.randomUUID();
 const body={device,nickname:'测试玩家',model:'测试笔记本',content:'屏幕清晰，风扇噪音一般。',score:9};
 r=await fetch(base+'/brands/lenovo/reviews',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({...body,score:11})}); assert.equal(r.status,400);
 r=await fetch(base+'/brands/lenovo/reviews',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}); assert.equal(r.status,200);
 brands=await (await fetch(base+'/brands')).json(); assert.equal(brands.find(b=>b.id==='lenovo').score,9);
 const reviews=await (await fetch(base+'/brands/lenovo/reviews')).json(); assert.equal(reviews[0].content,body.content); assert.equal(reviews[0].nickname,body.nickname);
 r=await fetch(base+'/brands/lenovo/reviews',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}); assert.equal(r.status,429);
 r=await fetch(base+'/reviews/'+reviews[0].id,{method:'DELETE'}); assert.equal(r.status,401);
 r=await fetch(base+'/missing'); assert.equal(r.status,404);
});
