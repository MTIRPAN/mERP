/** Cultures offered when adding a language; any other specific culture code can be typed in. */
export const COMMON_CULTURES = [
  'en-US',
  'en-GB',
  'de-DE',
  'fr-FR',
  'es-ES',
  'it-IT',
  'nl-NL',
  'ru-RU',
  'uk-UA',
  'ar-SA',
  'fa-IR',
  'az-AZ',
  'ka-GE',
  'bg-BG',
  'ro-RO',
  'el-GR',
  'pl-PL',
  'zh-CN',
  'ja-JP',
  'ko-KR',
] as const;

/** "İngilizce (ABD)" style name of a culture in the user's language; the code when the browser cannot name it. */
export const cultureLabel = (code: string, locale: string): string => {
  try {
    return new Intl.DisplayNames([locale], { type: 'language', languageDisplay: 'standard' }).of(code) ?? code;
  } catch {
    return code;
  }
};

/** Share of the catalog translated, 0–100. */
export const coverage = (translated: number, total: number): number =>
  total === 0 ? 0 : Math.floor((translated * 100) / total);
