/**
 * 图吧工具箱 · 遥测看板（Cloudflare Worker）
 *
 * 用只读 API 密钥查 Azure Application Insights（工作区型），把常用面板
 * （在线设备 / 活跃趋势 / 版本分布 / 崩溃排行 / 工具热度）一次返回给前端，
 * 免去每次打开 Azure 门户手写 KQL 的麻烦。
 *
 * 密钥与口令都放在 Worker Secrets（AI_API_KEY / DASHBOARD_TOKEN），不进仓库。
 */
import pageHtml from '../public/index.html';

const APP_ID = 'c5cae637-8b3c-43c5-b14e-3d228767c4c8';
const QUERY_ENDPOINT = `https://api.applicationinsights.io/v1/apps/${APP_ID}/query`;
const QUERY_TIMEOUT_MS = 25000;

/** 心跳/启动事件即"这台设备活着"，全站活跃口径都用它们。 */
const ALIVE = 'name in ("heartbeat", "app_start")';

/** 面板定义：KQL + 缓存时长（秒）。
 *  在线与崩溃要新鲜（30s），趋势与榜单分钟级变化没意义（5 分钟）。 */
const PANELS = {
  online: {
    ttl: 30,
    query: `customEvents | where timestamp > ago(15m) and ${ALIVE} | summarize online = dcount(user_Id)`,
  },
  actives: {
    ttl: 30,
    query:
      `customEvents | where ${ALIVE} ` +
      `| summarize dau = dcountif(user_Id, timestamp > ago(1d)), ` +
      `wau = dcountif(user_Id, timestamp > ago(7d)), ` +
      `mau = dcountif(user_Id, timestamp > ago(30d)), ` +
      `total = dcount(user_Id)`,
  },
  crashes24h: {
    ttl: 30,
    query: `exceptions | where timestamp > ago(24h) | summarize crashes = count(), devices = dcount(user_Id)`,
  },
  onlineSeries: {
    ttl: 300,
    query:
      `customEvents | where timestamp > ago(24h) and ${ALIVE} ` +
      `| summarize devices = dcount(user_Id) by bin(timestamp, 30m) | order by timestamp asc`,
  },
  dailySeries: {
    ttl: 300,
    query:
      `customEvents | where timestamp > ago(30d) and ${ALIVE} ` +
      `| summarize devices = dcount(user_Id) by bin(timestamp, 1d) | order by timestamp asc`,
  },
  hourlyStarts: {
    ttl: 300,
    query:
      `customEvents | where timestamp > ago(24h) and name == "app_start" ` +
      `| summarize starts = count() by bin(timestamp, 1h) | order by timestamp asc`,
  },
  crashSeries: {
    ttl: 300,
    query:
      `exceptions | where timestamp > ago(30d) ` +
      `| summarize crashes = count(), devices = dcount(user_Id) by bin(timestamp, 1d) | order by timestamp asc`,
  },
  newDevices: {
    ttl: 300,
    query:
      `customEvents | where ${ALIVE} | summarize firstSeen = min(timestamp) by user_Id ` +
      `| where firstSeen > ago(30d) | summarize devices = count() by bin(firstSeen, 1d) | order by firstSeen asc`,
  },
  activeHours: {
    ttl: 300,
    query:
      `customEvents | where timestamp > ago(7d) and ${ALIVE} ` +
      // 东八区本地时间：0=周日 … 6=周六，h=小时
      `| extend local = timestamp + 8h ` +
      `| extend h = hourofday(local), d = toint(dayofweek(local) / 1d) ` +
      `| summarize devices = dcount(user_Id) by d, h`,
  },
  versions: {
    ttl: 300,
    query:
      `customEvents | where name == "app_start" and timestamp > ago(7d) ` +
      `| summarize devices = dcount(user_Id), starts = count() by version = tostring(customDimensions.version) ` +
      `| order by devices desc`,
  },
  errors: {
    ttl: 300,
    query:
      `exceptions | where timestamp > ago(7d) ` +
      `| summarize cnt = count(), devices = dcount(user_Id), lastSeen = max(timestamp), ` +
      `sample = any(outerMessage), inner = any(innermostMessage), frames = any(tostring(details[0].parsedStack)) ` +
      `by type = tostring(type) | order by cnt desc | take 20`,
  },
  tools: {
    ttl: 300,
    query:
      `customEvents | where name == "tool_launch" and timestamp > ago(7d) ` +
      `| summarize uses = count(), devices = dcount(user_Id) by tool = tostring(customDimensions.tool) ` +
      `| order by uses desc | take 20`,
  },
};

/**
 * 外部数据面板：不走 App Insights，直接调各平台 API。
 * 下载量缓存 6 小时——GitHub 未授权接口按 IP 限 60 次/小时，而 Worker 出口是共享 IP，
 * 拉得越勤越容易被限流；配了 GITHUB_TOKEN（可选密钥）配额升到 5000 次/小时。
 */
const EXTERNAL_PANELS = {
  downloads: { ttl: 21600, load: loadDownloads },
};

const GITHUB_RELEASES = 'https://api.github.com/repos/luolangaga/tubatool/releases?per_page=100';
const GITCODE_STATS = 'https://api.gitcode.com/api/v5/repos/luolangaga/tubatool/download_statistics';

/** 最近一次成功取到的下载量：某个源临时失败（限流/超时）时沿用它，数字不会突然变空。 */
const lastGoodDownloads = { github: null, gitcode: null, at: null };

/**
 * 游客可见的面板：只有聚合口径（台数、趋势、版本分布、下载量），
 * 不含异常类型/消息与工具明细——那些是口令解锁后的「管理视图」。
 */
const PUBLIC_PANELS = [
  'online', 'actives', 'crashes24h', 'onlineSeries', 'dailySeries',
  'newDevices', 'crashSeries', 'activeHours', 'versions', 'downloads',
];

/** 进程内缓存（每个 isolate 一份，够了：小看板，省的是 Azure 查询次数）。 */
const cache = new Map();

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname === '/api/summary') {
      return handleSummary(request, env, url);
    }
    if (url.pathname === '/api/ping') {
      return json({ ok: true, time: new Date().toISOString() });
    }
    if (url.pathname === '/' || url.pathname === '/index.html') {
      // no-cache：改版后立即生效，不会被浏览器/边缘缓存挡住
      return new Response(pageHtml, {
        headers: {
          'content-type': 'text/html; charset=utf-8',
          'cache-control': 'no-cache, must-revalidate',
        },
      });
    }
    return new Response('Not found', { status: 404 });
  },
};

async function handleSummary(request, env, url) {
  const provided = providedToken(request, url);

  // 带了口令但不对：明确回 401，前端好提示"口令不正确"
  if (provided && !isValidToken(provided, env)) {
    return json({ error: 'unauthorized' }, 401);
  }

  // 没带口令 = 游客：只回聚合面板；带对口令 = 管理视图：全部面板
  const authorized = Boolean(provided);

  if (!env.AI_API_KEY) {
    return json({ error: 'AI_API_KEY 未配置（wrangler secret put AI_API_KEY）' }, 500);
  }

  const force = url.searchParams.get('force') === '1';
  const names = authorized
    ? Object.keys(PANELS).concat(Object.keys(EXTERNAL_PANELS))
    : PUBLIC_PANELS;
  const results = await Promise.allSettled(names.map((name) => runPanel(env, name, force)));

  const panels = {};
  const errors = {};
  results.forEach((result, index) => {
    const name = names[index];
    if (result.status === 'fulfilled') panels[name] = result.value;
    // 游客不返回原始错误文本（可能带资源标识等内部信息）
    else errors[name] = authorized
      ? String(result.reason && result.reason.message ? result.reason.message : result.reason)
      : '查询失败';
  });

  return json({
    generatedAt: new Date().toISOString(),
    mode: authorized ? 'full' : 'public',
    panels,
    errors,
  });
}

/** 取请求里携带的口令：优先请求头，其次 ?token=（方便直接用链接打开）。 */
function providedToken(request, url) {
  return request.headers.get('x-dashboard-token') || url.searchParams.get('token') || '';
}

/** 口令校验；未配置 DASHBOARD_TOKEN 时一律视为未授权（避免误开门）。 */
function isValidToken(token, env) {
  return Boolean(env.DASHBOARD_TOKEN) && timingSafeEqual(token, env.DASHBOARD_TOKEN);
}

/** 定长比较，避免逐字符比较带来的时序差异；长度不同直接失败。 */
function timingSafeEqual(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string' || a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

async function runPanel(env, name, force) {
  const panel = PANELS[name] || EXTERNAL_PANELS[name];
  const now = Date.now();
  const hit = cache.get(name);
  if (!force && hit && now - hit.at < panel.ttl * 1000) return hit.value;

  const value = panel.query ? await runQuery(env, panel.query) : await panel.load(env);
  cache.set(name, { at: now, value });
  return value;
}

/**
 * 累计下载量：GitHub Release 资产计数 + GitCode 下载统计。
 * 只统计这两个渠道——微软应用商店、网盘与第三方渠道拿不到数据，页面上已注明。
 * 单个源失败时沿用最近一次成功值（并标记 stale），两个源都失败才报错。
 */
async function loadDownloads(env) {
  const [github, gitcode] = await Promise.allSettled([
    loadGitHubDownloads(env),
    loadGitCodeDownloads(),
  ]);

  const githubOk = github.status === 'fulfilled' ? github.value : null;
  const gitcodeOk = gitcode.status === 'fulfilled' ? gitcode.value : null;

  if (githubOk !== null) lastGoodDownloads.github = githubOk;
  if (gitcodeOk !== null) lastGoodDownloads.gitcode = gitcodeOk;
  if (githubOk !== null || gitcodeOk !== null) lastGoodDownloads.at = new Date().toISOString();

  const githubValue = githubOk !== null ? githubOk : lastGoodDownloads.github;
  const gitcodeValue = gitcodeOk !== null ? gitcodeOk : lastGoodDownloads.gitcode;

  if (githubValue === null && gitcodeValue === null) {
    throw new Error(`两个下载源都失败：GitHub=${reason(github)}；GitCode=${reason(gitcode)}`);
  }

  const errors = {};
  if (githubOk === null) errors.github = reason(github);
  if (gitcodeOk === null) errors.gitcode = reason(gitcode);

  return {
    github: githubValue,
    gitcode: gitcodeValue,
    total: (githubValue || 0) + (gitcodeValue || 0),
    // stale 表示该数字沿用的是上一次成功值，不是刚刚取到的
    stale: { github: githubOk === null, gitcode: gitcodeOk === null },
    errors,
    updatedAt: lastGoodDownloads.at || new Date().toISOString(),
  };
}

function reason(settled) {
  return settled.status === 'rejected' ? String(settled.reason && settled.reason.message || settled.reason) : 'ok';
}

/**
 * GitHub：所有 Release 资产的 download_count 之和（分页抓全）。
 * 配了 GITHUB_TOKEN 走认证请求（5000 次/小时），否则是未授权（60 次/小时，共享 IP 易被限流）。
 */
async function loadGitHubDownloads(env) {
  let total = 0;
  let url = GITHUB_RELEASES;

  const headers = {
    'user-agent': 'tuba-telemetry-dashboard',
    accept: 'application/vnd.github+json',
  };
  if (env && env.GITHUB_TOKEN) headers.authorization = `Bearer ${env.GITHUB_TOKEN}`;

  for (let page = 0; page < 5 && url; page++) {
    const response = await fetch(url, { headers, signal: AbortSignal.timeout(20000) });
    if (!response.ok) {
      // 带上响应正文片段，便于在面板上直接看出是限流、鉴权还是别的原因
      let detail = '';
      try {
        detail = (await response.text()).replace(/\s+/g, ' ').slice(0, 160);
      } catch { }
      throw new Error(`GitHub ${response.status}${detail ? '：' + detail : ''}`);
    }

    const releases = await response.json();
    for (const release of releases) {
      for (const asset of release.assets || []) {
        total += Number(asset.download_count) || 0;
      }
    }

    // 只有还有下一页时才继续（rel="next" 的 Link 头）
    url = nextPageUrl(response.headers.get('link'));
  }

  return total;
}

/** 从 GitHub 的 Link 头里取下一页地址。 */
function nextPageUrl(linkHeader) {
  if (!linkHeader) return null;
  const match = linkHeader.split(',').find((part) => part.includes('rel="next"'));
  if (!match) return null;
  const url = match.match(/<([^>]+)>/);
  return url ? url[1] : null;
}

/** GitCode：download_statistics_history_total 即累计下载量。 */
async function loadGitCodeDownloads() {
  const response = await fetch(GITCODE_STATS, {
    headers: { accept: 'application/json' },
    signal: AbortSignal.timeout(20000),
  });
  if (!response.ok) throw new Error(`GitCode ${response.status}`);

  const payload = await response.json();
  const historyTotal = Number(payload.download_statistics_history_total);
  if (Number.isFinite(historyTotal) && historyTotal > 0) return historyTotal;

  // 兜底：取明细里最新一天的累计值
  const detail = payload.download_statistics_detail || [];
  if (detail.length) return Number(detail[0].total_dl_cnt) || 0;
  throw new Error('GitCode 返回结构异常');
}

async function runQuery(env, query) {
  const response = await fetch(`${QUERY_ENDPOINT}?query=${encodeURIComponent(query)}`, {
    headers: { 'x-api-key': env.AI_API_KEY },
    signal: AbortSignal.timeout(QUERY_TIMEOUT_MS),
  });

  const text = await response.text();
  if (!response.ok) throw new Error(`App Insights ${response.status}: ${text.slice(0, 300)}`);

  let payload;
  try {
    payload = JSON.parse(text);
  } catch {
    throw new Error(`查询结果不是 JSON：${text.slice(0, 200)}`);
  }

  const table = payload.tables && payload.tables[0];
  if (!table) return { columns: [], rows: [] };
  return {
    columns: (table.columns || []).map((c) => c.name),
    rows: table.rows || [],
  };
}

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
    },
  });
}
