import type { Dayjs } from 'dayjs';
import type { TFunction } from 'i18next';

/** A picked day as the API's date, or null. */
export const day = (value: Dayjs | null | undefined): string | null => (value ? value.format('YYYY-MM-DD') : null);

/** Trimmed text, or null when blank. */
export const text = (value: string | null | undefined): string | null => (value?.trim() ? value.trim() : null);

/** Select options of an enum, labelled with <c>{prefix}{value}</c>. */
export const enumOptions = <T extends string>(values: Record<string, T>, prefix: string, t: TFunction) =>
  Object.values(values).map((value) => ({ value, label: t(`${prefix}${value}`) }));
