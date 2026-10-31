// 临时迁移入口：仅固定表/时间区间，独立随机口令，迁移后删除 MIGRATION_TOKEN 即关闭。
const ENDPOINT = 'https://api.applicationinsights.io/v1/apps/c5cae637-8b3c-43c5-b14e-3d228767c4c8/query';
const fields = {
  customEvents: 'timestamp, itemId, name, user_Id, session_Id, application_Version, customDimensions, customMeasurements',
  exceptions: 'timestamp, itemId, user_Id, session_Id, application_Version, customDimensions, type, outerMessage, innermostMessage, details',
};
export async function exportAzure(request, env) {
  if (!env.MIGRATION_TOKEN || request.headers.get('authorization') !== `Bearer ${env.MIGRATION_TOKEN}`)
    return Response.json({ error: 'not found' }, { status: 404 });
  if (request.method !== 'POST') return new Response(null, { status: 405 });
  try {
    const body = await request.json();
    if (!Object.hasOwn(fields, body.table)) return Response.json({ error: 'invalid table' }, { status: 400 });
    let query;
    if (body.mode === 'inventory') {
      query = `${body.table} | summarize records=count(), firstSeen=min(timestamp), lastSeen=max(timestamp)`;
    } else {
      const start = new Date(body.start), end = new Date(body.end);
      if (!Number.isFinite(+start) || !Number.isFinite(+end) || start >= end)
        return Response.json({ error: 'invalid time range' }, { status: 400 });
      query = `${body.table} | where timestamp >= datetime(${start.toISOString()}) and timestamp < datetime(${end.toISOString()}) | project ${fields[body.table]} | order by timestamp asc | take 5001`;
    }
    const response = await fetch(ENDPOINT, {
      method: 'POST', headers: { 'x-api-key': env.AI_API_KEY, 'content-type': 'application/json' },
      body: JSON.stringify({ query }), signal: AbortSignal.timeout(25000),
    });
    const payload = await response.json();
    return Response.json(payload, { status: response.status, headers: { 'cache-control': 'no-store' } });
  } catch {
    return Response.json({ error: 'Azure export request failed' }, { status: 502 });
  }
}
