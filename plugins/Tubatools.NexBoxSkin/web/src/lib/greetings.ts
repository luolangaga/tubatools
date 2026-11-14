import { Solar } from "lunar-javascript";

export const EASTER_EGG_TEXT = "作者是小南梁awa （出现概率为0.001%）";
export const EASTER_EGG_PROBABILITY = 0.00001;

interface GreetingResult {
  text: string;
  isEasterEgg?: boolean;
}

// 时段问候：key 为开始小时，每时段 3 个变体
const SLOT_VARIANTS: Record<number, string[]> = {
  0: [
    "还在修仙吗，{{name}}？(￣ω￣)zzz",
    "凌晨了哦，{{name}} 早点睡呀 (。-ω-)zzz",
    "修仙伤身，{{name}} 快睡觉！(ง •̀_•́)ง",
  ],
  2: [
    "夜猫子本猫，{{name}} 快睡啦！(。-ω-)zzz",
    "天快亮了，{{name}} 该休息了 (￣o￣) . z Z",
    "早安还是晚安？{{name}} 该睡了！(￣ω￣)zzz",
  ],
  5: [
    "早上好，{{name}} ٩(◕‿◕｡)۶",
    "早安呀，{{name}}！(๑˃ᴗ˂)ﻭ",
    "太阳出来啦，{{name}} 早安 (｡･ω･｡)ﾉ♡",
  ],
  9: [
    "上午好，{{name}} (≧▽≦)",
    "元气满满的上午，{{name}} 加油鸭 (ง •̀_•́)ง",
    "上午好呀，{{name}} ✧٩(ˊωˋ*)و✧",
  ],
  12: [
    "中午好，{{name}} (｡◕‿◕｡)",
    "午饭时间到，{{name}} 记得吃饭哦 (๑´ㅂ`๑)",
    "中午好呀，{{name}} ～(￣▽￣～)~",
  ],
  14: [
    "下午好，{{name}} (´▽`)ﾉ",
    "下午茶时间，{{name}} 休息一下吧 (・∀・)",
    "下午好呀，{{name}} ٩(๑•̀ω•́๑)۶",
  ],
  18: [
    "晚上好，{{name}} (っ˘ω˘ς)",
    "晚上好呀，{{name}} 🌙 ٩(◕‿◕｡)۶",
    "今晚也要开心哦，{{name}} (・ω< )★",
  ],
  22: [
    "夜深了，{{name}} 记得早点休息~ (￣ω￣)zzz",
    "夜深了，{{name}} 该睡觉啦 (。-ω-)zzz",
    "晚安，{{name}} 明天见~ (๑˘︶˘๑).｡.:*♡",
  ],
};

// 周中特供：周一 / 周三 / 周五
const WEEKDAY_VARIANTS: Record<number, string[]> = {
  1: [
    "新的一周，{{name}} 加油！(ง •̀_•́)ง",
    "周一啦，{{name}} 元气满满！(๑•̀ㅂ•́)و✧",
    "这周也要冲鸭，{{name}} (๑˃ᴗ˂)ﻭ",
  ],
  3: [
    "周三啦，一周过一半，{{name}} 坚持住！(ง •̀_•́)ง",
    "撑过今天，周五还会远吗，{{name}} (´▽`)",
    "周三：距离周末只剩两天！(ﾉ◕ヮ◕)ﾉ*:･ﾟ✧",
  ],
  5: [
    "周五咯，{{name}}！马上周末啦 (ﾉ◕ヮ◕)ﾉ*:･ﾟ✧",
    "周五万岁，{{name}} (๑˃ᴗ˂)ﻭ",
    "本周余额不足，周末充值成功！ヽ(•‿•)ノ",
  ],
};

const WEEKEND_VARIANTS: string[] = [
  "周末愉快，{{name}}！ヽ(•‿•)ノ",
  "周末好好放松哦，{{name}} (´• ω •`)",
  "假期快乐，{{name}} (ﾉ◕ヮ◕)ﾉ*:･ﾟ✧",
];

// 随机彩蛋（10% 概率替代时段问候）
const FUN_VARIANTS: string[] = [
  "今天也要加油鸭，{{name}}！(๑•̀ㅂ•́)و✧",
  "被生活捶打也要开心哦 (´• ω •`)",
  "摸摸头，{{name}} (´• ω •`)",
  "今日宜：开心！忌：不开心 ✧(≖ ◡ ≖✿)",
  "运气守恒，好运会来的，{{name}}！(◕ᴗ◕✿)",
];

// 关怀小语：作为标题独立出现的候选，不必非是"早上好"等时段问候
const CARE_VARIANTS: string[] = [
  "今天有没有好好吃饭呀，{{name}}？(｡･ω･｡)ﾉ♡",
  "记得多喝水，{{name}} 要保持水润哦～(๑˃ᴗ˂)ﻭ",
  "今天也要开开心心的，{{name}}！(๑•̀ㅂ•́)و✧",
  "记得早点休息，{{name}} 别熬夜呀 (。-ω-)zzz",
  "少熬夜，{{name}} 身体才是本钱 (ง •̀_•́)ง",
  "工作再忙，{{name}} 也要照顾好自己 (´• ω •`)",
  "久坐记得起来活动活动，{{name}} (๑˃ᴗ˂)ﻭ",
  "屏幕看久了，{{name}} 眺望一下远方吧 (・∀・)",
  "记得给家人打个电话，{{name}} (´▽`)ﾉ",
  "天气多变，{{name}} 记得添衣保暖 (っ˘ω˘ς)",
  "多出去走走，{{name}} 晒晒太阳心情好 (◕ᴗ◕✿)",
  "按时吃饭，{{name}} 规律作息很重要 (๑´ㅂ`๑)",
  "好好照顾自己，{{name}} 你是最棒的 (´• ω •`)",
  "今天也辛苦啦，{{name}} 早点休息 (￣ω￣)zzz",
  "记得吃早餐，{{name}} 一天才有力气 (๑˃ᴗ˂)ﻭ",
  "别太拼了，{{name}} 也要学会放松 (・ω< )★",
  "睡前记得泡个脚，{{name}} 睡得更香 (。-ω-)zzz",
  "周末到了，{{name}} 好好放松一下吧 (ﾉ◕ヮ◕)ﾉ*:･ﾟ✧",
  "今日份的快乐，{{name}} 记得查收～✧(≖ ◡ ≖✿)",
  "天冷了，{{name}} 记得多穿点 (っ˘ω˘ς)",
];

// 公历节日：key 为 "月-日"
const SOLAR_FESTIVALS: Record<string, string> = {
  "1-1": "元旦快乐，{{name}}！新年新气象 🎉 (๑˃ᴗ˂)ﻭ",
  "2-14": "情人节快乐，{{name}}！(๑•́ ₃ •̀๑)💕",
  "3-8": "女神节快乐，{{name}}！🌸 (｡･ω･｡)ﾉ♡",
  "3-12": "植树节快乐，{{name}} 🌱 一起种下好心情！(ง •̀_•́)ง",
  "4-1": "愚人节快乐！今天的话都别信哦 {{name}} (๑•̀ㅂ•́)و",
  "5-1": "劳动节快乐，{{name}}！辛苦啦 (´• ω •`)💐",
  "5-4": "青年节快乐，{{name}}！永远年轻 (◕ᴗ◕✿)",
  "6-1": "儿童节快乐，{{name}}！今天你也是小朋友 🍭 (๑˃ᴗ˂)ﻭ",
  "10-1": "国庆快乐，{{name}}！🇨🇳 (๑•̀ㅂ•́)و✧",
  "10-31": "万圣节快乐，{{name}}！不给糖就捣蛋 🎃 (・ω< )★",
  "11-11": "双十一快乐，{{name}}！钱包辛苦了 (´• ω •`)💸",
  "12-24": "平安夜快乐，{{name}}！平平安安 🍎 (っ˘ω˘ς)",
  "12-25": "圣诞快乐，{{name}}！🎄 (๑˃ᴗ˂)ﻭ",
};

// 农历节日：key 为 lunar-javascript 返回的节日名
const LUNAR_FESTIVALS: Record<string, string> = {
  "除夕": "除夕快乐，{{name}}！辞旧迎新 🧨 (ﾉ◕ヮ◕)ﾉ*:･ﾟ✧",
  "春节": "春节快乐，{{name}}！恭喜发财 🧧 (๑˃ᴗ˂)ﻭ",
  "元宵节": "元宵节快乐，{{name}}！团团圆圆 🏮 (｡･ω･｡)ﾉ♡",
  "端午节": "端午安康，{{name}}！记得吃粽子 🐉 (ง •̀_•́)ง",
  "七夕节": "七夕快乐，{{name}}！💞 (っ˘ω˘ς)",
  "中秋节": "中秋快乐，{{name}}！月圆人团圆 🌕 (｡◕‿◕｡)",
};

// 季节前缀：修饰常规问候
function getSeasonPrefix(month: number): string {
  if (month >= 3 && month <= 5) return "春风十里，";
  if (month >= 6 && month <= 8) return "天气炎热记得补水，";
  if (month >= 9 && month <= 11) return "秋高气爽，";
  return "天冷添衣，";
}

function getSlotKey(hour: number): number {
  if (hour >= 0 && hour < 2) return 0;
  if (hour >= 2 && hour < 5) return 2;
  if (hour >= 5 && hour < 9) return 5;
  if (hour >= 9 && hour < 12) return 9;
  if (hour >= 12 && hour < 14) return 12;
  if (hour >= 14 && hour < 18) return 14;
  if (hour >= 18 && hour < 22) return 18;
  return 22;
}

function renderTemplate(template: string, username: string): string {
  if (username) return template.replace(/\{\{name\}\}/g, username);
  return template.replace(/，\{\{name\}\}/g, "").replace(/\{\{name\}\}/g, "").trim();
}

function pickVariant(variants: string[], now: Date, offset: number): string {
  const seed = now.getDate() + now.getHours() + offset;
  return variants[seed % variants.length];
}

export function rollEasterEgg(): boolean {
  return Math.random() < EASTER_EGG_PROBABILITY;
}

/**
 * 生成问候语，优先级：节日 > 星期特供 > 时段/随机彩蛋
 */
export function getGreeting(now: Date, username: string, variantOffset = 0): GreetingResult {
  const month = now.getMonth() + 1;
  const day = now.getDate();

  // 节日特供
  const solarFestival = SOLAR_FESTIVALS[`${month}-${day}`];
  if (solarFestival) {
    return { text: renderTemplate(solarFestival, username) };
  }
  try {
    const lunarFestivals = Solar.fromYmd(now.getFullYear(), month, day).getLunar().getFestivals();
    for (const name of lunarFestivals) {
      const template = LUNAR_FESTIVALS[name];
      if (template) {
        return { text: renderTemplate(template, username) };
      }
    }
  } catch {
    // 农历转换失败则忽略节日
  }

  // 星期特供
  const weekday = now.getDay();
  let template: string | undefined;
  if (weekday === 0 || weekday === 6) {
    template = pickVariant(WEEKEND_VARIANTS, now, variantOffset);
  } else if (WEEKDAY_VARIANTS[weekday]) {
    template = pickVariant(WEEKDAY_VARIANTS[weekday], now, variantOffset);
  }

  if (template) {
    return { text: renderTemplate(template, username) };
  }

  // 时段问候，10% 概率触发随机彩蛋
  const seasonPrefix = getSeasonPrefix(month);
  if (Math.random() < 0.1) {
    return { text: seasonPrefix + renderTemplate(pickVariant(FUN_VARIANTS, now, variantOffset), username) };
  }
  // 约 40% 概率直接以关怀小语作为标题（独立显示，不拼接时段问候）
  if (Math.random() < 0.4) {
    return { text: renderTemplate(pickVariant(CARE_VARIANTS, now, variantOffset), username) };
  }
  const slotVariants = SLOT_VARIANTS[getSlotKey(now.getHours())];
  return { text: seasonPrefix + renderTemplate(pickVariant(slotVariants, now, variantOffset), username) };
}
