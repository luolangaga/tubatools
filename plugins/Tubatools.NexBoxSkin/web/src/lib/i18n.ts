import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import { invoke } from "@tauri-apps/api/core";

const STORAGE_KEY = "i18nextLng";
const LANGUAGE_MODE_STORAGE_KEY = "nexbox_language_mode";
const SYSTEM_LANGUAGE = "system";

/** 语言包按需加载：静态 import 会让每个常驻窗口都解析并常驻全部 6 份 JSON */
const LOADERS: Record<string, () => Promise<{ default: object }>> = {
  zh: () => import("@/locales/zh.json"),
  en: () => import("@/locales/en.json"),
};

const FALLBACK_LNG = "zh";
const SUPPORTED_LANGUAGES = Object.keys(LOADERS);

function isSupportedLanguage(value: string | null): value is keyof typeof LOADERS {
  return value !== null && Object.prototype.hasOwnProperty.call(LOADERS, value);
}

export function getLanguagePreference(): string {
  const storedMode = localStorage.getItem(LANGUAGE_MODE_STORAGE_KEY);
  if (storedMode === SYSTEM_LANGUAGE || isSupportedLanguage(storedMode)) {
    return storedMode;
  }

  return SYSTEM_LANGUAGE;
}

/** localStorage 里可能是 fr-CA 这类未收录的变体，未收录的一律取回退语言包 */
async function loadMessages(lng: string): Promise<object> {
  const mod = await (LOADERS[lng] ?? LOADERS[FALLBACK_LNG])();
  return mod.default;
}

async function getSystemLocale(): Promise<string> {
  try {
    const locale = await invoke<string>("get_system_locale");
    const localeMap: Record<string, string> = {
      "zh-Hans": "zh",
      "zh-Hant": "zh-TW",
      "en": "en",
      "fr": "fr",
      "ja": "ja",
      "de": "de",
    };
    return localeMap[locale] || "zh";
  } catch {
    return "zh";
  }
}

export async function setLanguagePreference(mode: string): Promise<string> {
  const nextMode = mode === SYSTEM_LANGUAGE || isSupportedLanguage(mode) ? mode : SYSTEM_LANGUAGE;
  const nextLanguage = nextMode === SYSTEM_LANGUAGE ? await getSystemLocale() : nextMode;

  localStorage.setItem(LANGUAGE_MODE_STORAGE_KEY, nextMode);
  await i18n.changeLanguage(nextLanguage);
  return nextLanguage;
}

async function initI18n() {
  const systemLocale = await getSystemLocale();
  let initialMode = getLanguagePreference();

  // Migrate legacy settings: keep an explicit non-system language, otherwise adopt system mode.
  if (!localStorage.getItem(LANGUAGE_MODE_STORAGE_KEY)) {
    const legacyLanguage = localStorage.getItem(STORAGE_KEY);
    initialMode =
      isSupportedLanguage(legacyLanguage) && legacyLanguage !== systemLocale
        ? legacyLanguage
        : SYSTEM_LANGUAGE;
    localStorage.setItem(LANGUAGE_MODE_STORAGE_KEY, initialMode);
  }

  const initialLang = initialMode === SYSTEM_LANGUAGE ? systemLocale : initialMode;
  const lang = isSupportedLanguage(initialLang) ? initialLang : FALLBACK_LNG;

  const resources: Record<string, { translation: object }> = {
    [lang]: { translation: await loadMessages(lang) },
  };
  if (lang !== FALLBACK_LNG) {
    resources[FALLBACK_LNG] = { translation: await loadMessages(FALLBACK_LNG) };
  }

  await i18n.use(initReactI18next).init({
    resources,
    lng: lang,
    fallbackLng: FALLBACK_LNG,
    supportedLngs: SUPPORTED_LANGUAGES,
    interpolation: {
      escapeValue: false,
    },
  });

  i18n.on('languageChanged', (lng) => {
    localStorage.setItem(STORAGE_KEY, lng);
    // 切到尚未加载的语言时补载，addResourceBundle 会驱动订阅组件重渲染
    if (lng && !i18n.hasResourceBundle(lng, "translation")) {
      loadMessages(lng).then((messages) => {
        i18n.addResourceBundle(lng, "translation", messages, true, true);
      });
    }
  });

  return i18n;
}

initI18n();

export default i18n;
