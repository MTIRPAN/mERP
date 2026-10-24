import dayjs, { type Dayjs } from 'dayjs';
import type { TFunction } from 'i18next';
import { formatDate } from '@/shared/format/date';

/** The first day of a version in force "from the beginning" (DateOnly.MinValue, D41). */
export const BEGINNING = '0001-01-01';

export const isBeginning = (validFrom: string) => validFrom === BEGINNING;

/**
 * A saved version that has started is history: the server refuses to change it (D41). The browser's day is close
 * enough for the form; the server decides with the company's time zone.
 */
export const isStarted = (validFrom: string, today: Dayjs = dayjs()) =>
  isBeginning(validFrom) || !dayjs(validFrom).isAfter(today, 'day');

/** The earliest first day of a new version. */
export const tomorrow = () => dayjs().add(1, 'day').startOf('day');

export const versionLabel = (validFrom: string, t: TFunction, locale: string) =>
  isBeginning(validFrom)
    ? t('web.hr.leave.versions.from_beginning')
    : t('web.hr.leave.versions.from_date', { date: formatDate(validFrom, locale) });

/** The version of a form row as the API date: the beginning stays as it is. */
export const toValidFrom = (value: Dayjs | string | null | undefined) =>
  !value ? BEGINNING : typeof value === 'string' ? value : value.format('YYYY-MM-DD');

/** A form value: null for a number field left empty. */
export const numberOrNull = (value: number | null | undefined) =>
  value === undefined || value === null ? null : value;
