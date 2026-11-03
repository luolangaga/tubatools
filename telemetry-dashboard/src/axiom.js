export const DATASET = 'tuba-telemetry';
export const EDGE = 'https://us-east-1.aws.edge.axiom.co';

/** 保留现有看板的列顺序；Axiom tabular API 返回的是按列排列的数组。 */
export function decodeTable(payload) {
  if (payload.status?.isPartial) throw new Error('Axiom 返回不完整查询结果');
  const table = payload.tables?.[0];
  if (!table) return { columns: [], rows: [] };
  const columns = (table.fields || []).map(f => f.name);
  const count = table.columns?.[0]?.length || 0;
  return { columns, rows: Array.from({ length: count }, (_, i) => columns.map((_, j) => table.columns[j][i])) };
}

export async function queryAxiom(env, apl, options = {}) {
  const response = await fetch(`${EDGE}/v1/query/_apl?format=tabular`, {
    method: 'POST',
    headers: { authorization: `Bearer ${env.AXIOM_QUERY_TOKEN}`, 'content-type': 'application/json' },
    body: JSON.stringify({ apl, startTime: options.startTime || new Date(Date.now() - 30 * 86400000).toISOString(), endTime: options.endTime || new Date().toISOString() }),
    signal: AbortSignal.timeout(25000),
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(`Axiom 查询失败 (${response.status}): ${String(error.message || '').slice(0, 500)}`);
  }
  return decodeTable(await response.json());
}

// 设备去重/最早时间天然不受重传影响；只有次数统计才按 event_id 去重。
// 先过滤再取所需列，避免每次在线查询都把整月事件及异常堆栈放进聚合内存。
const base = `['${DATASET}']`;
const events = `${base} | where kind == "event"`;
const exceptions = `${base} | where kind == "exception" | summarize arg_max(_time, user_Id, type, outerMessage, innermostMessage, frames) by event_id`;
const starts = `${events} | where name == "app_start" | summarize arg_max(_time, user_Id, version) by event_id`;
const launches = `${events} | where name == "tool_launch" | summarize arg_max(_time, user_Id, tool) by event_id`;
const toolUsage = `${launches} | where _time > ago(7d) and isnotempty(tool) | summarize uses = count(), devices = dcount(user_Id) by tool`;
const alive = 'name in ("heartbeat", "app_start")';
export const PANELS = {
  online: { rangeSeconds: 900, ttl: 30, query: `${events} | where _time > ago(15m) and ${alive} | summarize online = dcount(user_Id)` },
  actives: { rangeSeconds: 2592000, ttl: 300, query: `${events} | where ${alive} | summarize dau = dcountif(user_Id, _time > ago(1d)), wau = dcountif(user_Id, _time > ago(7d)), mau = dcountif(user_Id, _time > ago(30d)), total = dcount(user_Id)` },
  crashes24h: { rangeSeconds: 86400, ttl: 30, query: `${exceptions} | where _time > ago(24h) | summarize crashes = count(), devices = dcount(user_Id)` },
  onlineSeries: { rangeSeconds: 86400, ttl: 300, query: `${events} | where _time > ago(24h) and ${alive} | summarize devices = dcount(user_Id) by timestamp = bin(_time, 30m) | order by timestamp asc | project timestamp, devices` },
  dailySeries: { rangeSeconds: 2592000, ttl: 300, query: `${events} | where ${alive} | summarize devices = dcount(user_Id) by timestamp = bin(_time, 1d) | order by timestamp asc | project timestamp, devices` },
  hourlyStarts: { rangeSeconds: 86400, ttl: 300, query: `${starts} | where _time > ago(24h) | summarize starts = count() by timestamp = bin(_time, 1h) | order by timestamp asc | project timestamp, starts` },
  crashSeries: { rangeSeconds: 2592000, ttl: 300, query: `${exceptions} | summarize crashes = count(), devices = dcount(user_Id) by timestamp = bin(_time, 1d) | order by timestamp asc | project timestamp, crashes, devices` },
  newDevices: { rangeSeconds: 2592000, ttl: 300, query: `${events} | where ${alive} | summarize firstSeen = min(_time) by user_Id | summarize devices = count() by timestamp = bin(firstSeen, 1d) | order by timestamp asc | project timestamp, devices` },
  activeHours: { rangeSeconds: 604800, ttl: 300, query: `${events} | where _time > ago(7d) and ${alive} | extend local = _time + 8h | extend h = hourofday(local), d = dayofweek(local) | summarize devices = dcount(user_Id) by d, h | project d, h, devices` },
  versions: { rangeSeconds: 604800, ttl: 300, query: `${starts} | where _time > ago(7d) | summarize devices = dcount(user_Id), starts = count() by version | order by devices desc | project version, devices, starts` },
  errors: { rangeSeconds: 604800, ttl: 300, query: `${exceptions} | where _time > ago(7d) | summarize cnt = count(), devices = dcount(user_Id), arg_max(_time, outerMessage, innermostMessage, frames) by type | order by cnt desc | take 20 | project type, cnt, devices, lastSeen = _time, sample = outerMessage, inner = innermostMessage, frames` },
  tools: { rangeSeconds: 604800, ttl: 300, query: `${toolUsage} | order by uses desc, tool asc | take 10 | project tool, uses, devices` },
  toolsLeast: { rangeSeconds: 604800, ttl: 300, query: `${toolUsage} | order by uses asc, tool asc | take 10 | project tool, uses, devices` },
};
