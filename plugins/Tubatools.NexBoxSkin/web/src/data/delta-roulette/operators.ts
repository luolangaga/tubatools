// 三角洲行动 · 随机装备生成 — 干员数据（复刻自 delta-roulette 工具）

export interface RouletteOperator {
  id: number;
  name: string;
  pic: string;
}

export const operators: RouletteOperator[] = [
  { id: 30, name: "红狼", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000030.png" },
  { id: 25, name: "威龙", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000025.png" },
  { id: 40, name: "银翼", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000040.png" },
  { id: 38, name: "无名", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000038.png" },
  { id: 36, name: "蛊", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000036.png" },
  { id: 39, name: "疾风", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000039.png" },
  { id: 26, name: "骇爪", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000026.png" },
  { id: 28, name: "露娜", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000028.png" },
  { id: 41, name: "比特", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000041.png" },
  { id: 29, name: "牧羊人", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000029.png" },
  { id: 45, name: "蝶", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000045.png" },
  { id: 37, name: "深蓝", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000037.png" },
  { id: 27, name: "蜂医", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000027.png" },
  { id: 35, name: "乌鲁鲁", pic: "https://playerhub.df.qq.com/playerhub/60004/object/p_88000000035.png" },
];