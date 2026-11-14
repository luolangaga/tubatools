// 三角洲行动 · 随机装备生成 — 地图数据（复刻自 delta-roulette 工具）

export type RouletteDifficulty = "常规" | "机密" | "绝密" | "永夜";

export interface RouletteMap {
  id: number;
  map: string;
  difficulty: RouletteDifficulty;
  pic: string;
}

export const maps: RouletteMap[] = [
  { id: 1, map: "零号大坝", difficulty: "机密", pic: "https://eo.oss.hengj.cn/one/DfGame/MapImages/lhdb-jimi.jpg" },
  { id: 1, map: "零号大坝", difficulty: "常规", pic: "https://eo.oss.hengj.cn/one/DfGame/MapImages/lhdb-changgui.jpg" },
  { id: 5, map: "潮汐监狱", difficulty: "绝密", pic: "https://eo.oss.hengj.cn/one/DfGame/MapImages/cxjy-juemi.jpg" },
  { id: 4, map: "航天基地", difficulty: "绝密", pic: "https://eo.oss.hengj.cn/one/DfGame/MapImages/htjd-juemi.jpg" },
  { id: 3, map: "巴克什", difficulty: "绝密", pic: "https://eo.oss.hengj.cn/one/DfGame/MapImages/bks-juemi.jpg" },
  { id: 2, map: "长弓溪谷", difficulty: "常规", pic: "https://eo.oss.hengj.cn/one/DfGame/MapImages/cgxg-changgui.jpg" },
  { id: 2, map: "长弓溪谷", difficulty: "机密", pic: "https://eo.oss.hengj.cn/one/DfGame/MapImages/cgxg-jimi.jpg" },
  { id: 3, map: "巴克什", difficulty: "机密", pic: "https://eo.oss.hengj.cn/one/DfGame/MapImages/bks-jimi.jpg" },
  { id: 1, map: "零号大坝", difficulty: "永夜", pic: "https://eo.oss.hengj.cn/one/DfGame/MapImages/lhdb-yongye.jpg" },
  { id: 4, map: "航天基地", difficulty: "机密", pic: "https://eo.oss.hengj.cn/one/DfGame/MapImages/htjd-jimi.jpg" },
];

/** 绝密/永夜 难度过滤（「只玩绝密」选项） */
export function isClassifiedDifficulty(d: RouletteDifficulty): boolean {
  return d === "绝密" || d === "永夜";
}

/** 普通难度（游戏里叫「常规」）：不给 6 套、也不给 5 级及以上弹药 */
export function isNormalDifficulty(d: RouletteDifficulty): boolean {
  return d === "常规";
}

/**
 * 普通难度的装备上限（单一数据源，别在页面里另写一份数字）：
 * - maxProtectLevel 5 → 排除 6 级护甲/头盔（即「6 套」）
 * - maxAmmoGrade 4    → 排除 5、6 级弹药（即「5 级蛋及以上」）
 */
export const NORMAL_LOADOUT_LIMITS = { maxProtectLevel: 5, maxAmmoGrade: 4 } as const;