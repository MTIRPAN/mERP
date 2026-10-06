/** Calendar date ("2026-10-07", a DateOnly) in the given culture. It carries no time zone, so none is applied. */
export const formatDate = (value: string, locale: string): string =>
  new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(`${value}T00:00:00Z`));

/** Moment (ISO 8601 with offset) in the given culture and the browser's time zone. */
export const formatDateTime = (value: string, locale: string): string =>
  new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
