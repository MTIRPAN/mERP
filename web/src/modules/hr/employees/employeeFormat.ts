import type { EmploymentItem } from '@/shared/api/generated/model';

/** Name of an ISO 3166-1 alpha-2 country in the user's language, e.g. TR → Türkiye. */
export const countryName = (code: string | null | undefined, locale: string): string | undefined => {
  if (!code) {
    return undefined;
  }

  try {
    return new Intl.DisplayNames([locale], { type: 'region' }).of(code.toUpperCase()) ?? code;
  } catch {
    return code;
  }
};

/** Name of an ISO 639-1 language in the user's language, e.g. en → İngilizce. */
export const languageName = (code: string, locale: string): string => {
  try {
    return new Intl.DisplayNames([locale], { type: 'language' }).of(code) ?? code;
  } catch {
    return code;
  }
};

/** The employment that gives the person's main job: an open primary one, else the latest (D38). */
export const mainEmployment = (employments: EmploymentItem[]): EmploymentItem | undefined =>
  employments.find((e) => e.isPrimary && e.status !== 'Terminated') ??
  employments.find((e) => e.status !== 'Terminated') ??
  employments[0];
