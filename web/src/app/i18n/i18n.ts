import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { getLocalizationBundle } from '@/shared/api/generated/endpoints/platform/platform';
import bootTexts from './tr-TR.json';

export const DEFAULT_CULTURE = 'tr-TR';
const CULTURE_STORAGE_KEY = 'merp.culture';

export const storedCulture = (): string => localStorage.getItem(CULTURE_STORAGE_KEY) ?? DEFAULT_CULTURE;

/**
 * Texts come from the API (D11). The small embedded bundle only covers the start-up and "server unreachable"
 * screens shown before or without the API.
 */
const loadTexts = async (culture: string): Promise<Record<string, string>> => {
  try {
    return { ...bootTexts, ...(await getLocalizationBundle(culture)) };
  } catch {
    return bootTexts;
  }
};

export const initI18n = async (culture: string = storedCulture()) => {
  const texts = await loadTexts(culture);
  await i18n.use(initReactI18next).init({
    lng: culture,
    fallbackLng: DEFAULT_CULTURE,
    resources: { [culture]: { translation: texts } },
    // Keys are flat ("web.shell.menu.home") and placeholders use the backend syntax: {name}
    keySeparator: false,
    nsSeparator: false,
    interpolation: { escapeValue: false, prefix: '{', suffix: '}' },
    returnNull: false,
  });
  document.documentElement.lang = culture.split('-')[0] ?? 'tr';
  return i18n;
};

export const changeCulture = async (culture: string) => {
  localStorage.setItem(CULTURE_STORAGE_KEY, culture);
  i18n.addResourceBundle(culture, 'translation', await loadTexts(culture), true, true);
  await i18n.changeLanguage(culture);
  document.documentElement.lang = culture.split('-')[0] ?? 'tr';
};

export default i18n;
