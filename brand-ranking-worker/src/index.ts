interface Env { DB: D1Database; ADMIN_TOKEN?: string }
const json = (data: unknown, status = 200) => Response.json(data, {status, headers:{'Cache-Control':'no-store'}});
const text = (v: unknown, max: number) => typeof v === 'string' && v.trim().length <= max ? v.trim() : '';
export default {
 async fetch(request: Request, env: Env): Promise<Response> {
  try {
   const url = new URL(request.url);
   if (request.method === 'GET' && url.pathname === '/brands') {
    const rows = await env.DB.prepare(`SELECT b.id,b.name,COUNT(r.id) AS count,ROUND(AVG(r.score),1) AS score FROM brands b LEFT JOIN reviews r ON b.id=r.brandId GROUP BY b.id ORDER BY count>0 DESC,score DESC,count DESC,b.name ASC`).all();
    return json(rows.results);
   }
   const match = url.pathname.match(/^\/brands\/([a-z]+)\/reviews$/);
   if (match && request.method === 'GET') {
    const offset = Math.max(0, Math.min(10000, Number(url.searchParams.get('offset')) || 0));
    const rows = await env.DB.prepare('SELECT id,nickname,model,content,score,createdAt FROM reviews WHERE brandId=? ORDER BY createdAt DESC LIMIT 30 OFFSET ?').bind(match[1],Math.floor(offset)).all();
    return json(rows.results);
   }
   if (match && request.method === 'POST') {
    if (!request.headers.get('Content-Type')?.includes('application/json')) return json({error:'需要 JSON 请求'},415);
    const raw = await request.text();
    if (raw.length > 6000) return json({error:'点评过长'},413);
    let body: Record<string,unknown>;
    try {body=JSON.parse(raw); if (!body || typeof body !== 'object') throw new Error();} catch {return json({error:'JSON 无效'},400);}
    const nickname=text(body.nickname,24), model=text(body.model,80), content=text(body.content,1000), device=text(body.device,64), score=body.score;
    if (!nickname || !model || content.length<5 || !/^[a-f0-9-]{36}$/.test(device) || typeof score!=='number' || !Number.isInteger(score) || score<1 || score>10) return json({error:'请填写昵称、型号、5–1000 字点评与 1–10 分评分'},400);
    if (!await env.DB.prepare('SELECT id FROM brands WHERE id=?').bind(match[1]).first()) return json({error:'品牌不存在'},404);
    const ip=request.headers.get('CF-Connecting-IP') || 'local';
    const hash=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(ip)))).map(v=>v.toString(16).padStart(2,'0')).join('');
    const now=Date.now();
    await env.DB.prepare('DELETE FROM throttle WHERE touched < ?').bind(now-86400000).run();
    const limit=await env.DB.prepare('INSERT INTO throttle VALUES (?,?) ON CONFLICT(ip) DO UPDATE SET touched=excluded.touched WHERE throttle.touched < ?').bind(hash,now,now-60000).run();
    if (!limit.meta.changes) return json({error:'操作频繁，请一分钟后再试'},429);
    await env.DB.prepare(`INSERT INTO reviews(id,brandId,device,nickname,model,content,score) VALUES (?,?,?,?,?,?,?) ON CONFLICT(brandId,device) DO UPDATE SET nickname=excluded.nickname,model=excluded.model,content=excluded.content,score=excluded.score,createdAt=strftime('%Y-%m-%dT%H:%M:%fZ','now')`).bind(crypto.randomUUID(),match[1],device,nickname,model,content,score).run();
    return json({ok:true});
   }
   if (request.method==='DELETE' && /^\/reviews\/[a-f0-9-]+$/.test(url.pathname)) {
    if (!env.ADMIN_TOKEN || request.headers.get('Authorization')!==`Bearer ${env.ADMIN_TOKEN}`) return json({error:'未授权'},401);
    await env.DB.prepare('DELETE FROM reviews WHERE id=?').bind(url.pathname.split('/')[2]).run(); return json({ok:true});
   }
   return json({error:'接口不存在'},404);
  } catch {return json({error:'服务暂时不可用，请稍后重试'},500);}
 }
};
