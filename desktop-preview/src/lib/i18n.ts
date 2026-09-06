import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import { invoke } from "@tauri-apps/api/core";

import zh from "@/locales/zh.json";
import en from "@/locales/en.json";
import zhTW from "@/locales/zh-TW.json";
import fr from "@/locales/fr.json";
import ja from "@/locales/ja.json";
import de from "@/locales/de.json";

const STORAGE_KEY = "i18nextLng";

async function getSystemLocale(): Promise<string> {
  try {
    const locale = await invoke<string>("get_system_locale");
    const localeMap: Record<string, string> = {
      "zh-Hans": "zh",
      "zh-Hant": "zh-TW",
      "fr": "fr",
      "ja": "ja",
      "de": "de",
    };
    return localeMap[locale] || "zh";
  } catch {
    return "zh";
  }
}

async function initI18n() {
  let initialLang = localStorage.getItem(STORAGE_KEY);
  
  if (!initialLang) {
    initialLang = await getSystemLocale();
    localStorage.setItem(STORAGE_KEY, initialLang);
  }

  await i18n.use(initReactI18next).init({
    resources: {
      zh: { translation: zh },
      en: { translation: en },
      "zh-TW": { translation: zhTW },
      fr: { translation: fr },
      ja: { translation: ja },
      de: { translation: de },
    },
    lng: initialLang,
    fallbackLng: "zh",
    supportedLngs: ["zh", "en", "zh-TW", "fr", "ja", "de"],
    interpolation: {
      escapeValue: false,
    },
  });

  i18n.on('languageChanged', (lng) => {
    localStorage.setItem(STORAGE_KEY, lng);
  });

  return i18n;
}

initI18n();

export default i18n;
