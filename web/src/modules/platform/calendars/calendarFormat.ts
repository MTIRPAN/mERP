import dayjs, { type Dayjs } from 'dayjs';

/** API time ("08:30:00") → picker value. */
export const toPickerTime = (value: string | null | undefined): Dayjs | undefined =>
  value ? dayjs(`2000-01-01T${value}`) : undefined;

/** Picker value → API time ("08:30:00"). */
export const toApiTime = (value: Dayjs | null | undefined): string | null => (value ? value.format('HH:mm:ss') : null);

/** "13:00:00" → "13:00". */
export const shortTime = (value: string | null | undefined): string => (value ? value.slice(0, 5) : '');

/** Week day names Monday to Sunday in the given culture (2024-01-01 was a Monday). */
export const weekdayNames = (locale: string, style: 'long' | 'short' | 'narrow' = 'long'): string[] =>
  Array.from({ length: 7 }, (_, i) =>
    new Intl.DateTimeFormat(locale, { weekday: style, timeZone: 'UTC' }).format(new Date(Date.UTC(2024, 0, 1 + i))),
  );

/** Month name in the given culture; month is 1–12. */
export const monthName = (locale: string, month: number): string =>
  new Intl.DateTimeFormat(locale, { month: 'long', timeZone: 'UTC' }).format(new Date(Date.UTC(2024, month - 1, 1)));

/** Weeks of a month, Monday first; days outside the month are null. Month is 1–12. */
export const monthWeeks = (year: number, month: number): (number | null)[][] => {
  const first = new Date(Date.UTC(year, month - 1, 1));
  const days = new Date(Date.UTC(year, month, 0)).getUTCDate();
  const offset = (first.getUTCDay() + 6) % 7;
  const cells: (number | null)[] = [
    ...Array<null>(offset).fill(null),
    ...Array.from({ length: days }, (_, i) => i + 1),
  ];
  while (cells.length % 7 !== 0) {
    cells.push(null);
  }

  return Array.from({ length: cells.length / 7 }, (_, week) => cells.slice(week * 7, week * 7 + 7));
};

/** Calendar date as the API writes it. */
export const isoDate = (year: number, month: number, day: number): string =>
  `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
