/** Calendar date ("2026-10-07", a DateOnly) in the given culture. It carries no time zone, so none is applied. */
export const formatDate = (value: string, locale: string): string =>
  new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(`${value}T00:00:00Z`));

/** Moment (ISO 8601 with offset) in the given culture and the browser's time zone. */
export const formatDateTime = (value: string, locale: string): string =>
  new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));

const relativeUnits: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600],
  ['month', 30 * 24 * 3600],
  ['week', 7 * 24 * 3600],
  ['day', 24 * 3600],
  ['hour', 3600],
  ['minute', 60],
];

/** Moment relative to now in the given culture, e.g. "5 dakika önce"; under a minute reads "şimdi". */
export const formatRelativeTime = (value: string, locale: string, now: number = Date.now()): string => {
  const seconds = Math.round((new Date(value).getTime() - now) / 1000);
  const format = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
  for (const [unit, size] of relativeUnits) {
    if (Math.abs(seconds) >= size) {
      return format.format(Math.trunc(seconds / size), unit);
    }
  }

  return format.format(0, 'second');
};
