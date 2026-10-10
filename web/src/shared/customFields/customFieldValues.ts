import dayjs from 'dayjs';
import type { TFunction } from 'i18next';
import type { CustomFieldFormItem } from '@/shared/api/generated/model';
import { formatDate } from '@/shared/format/date';

/** Custom field values as the API sends and takes them: field code → JSON value (D12). */
export type CustomFieldValues = Record<string, unknown>;

/** Form value of a stored value: dates become dayjs for the DatePicker; others stay. */
export const toFormValue = (field: Pick<CustomFieldFormItem, 'type'>, value: unknown): unknown =>
  field.type === 'Date' && typeof value === 'string' ? dayjs(value) : value;

/** API value of a form value: dates as yyyy-MM-dd, empty text and lists as null. */
export const toApiValue = (field: Pick<CustomFieldFormItem, 'type'>, value: unknown): unknown => {
  if (value === undefined || value === null || value === '' || (Array.isArray(value) && value.length === 0)) {
    return null;
  }

  if (field.type === 'Date' && dayjs.isDayjs(value)) {
    return value.format('YYYY-MM-DD');
  }

  return value;
};

/** Form values of a record's custom fields, for the fields the form shows. */
export const toFormValues = (fields: CustomFieldFormItem[], values: CustomFieldValues | undefined): CustomFieldValues =>
  Object.fromEntries(fields.map((field) => [field.code, toFormValue(field, values?.[field.code])]));

/** API values of the fields the form shows; the API keeps the values of fields it does not show. */
export const toApiValues = (fields: CustomFieldFormItem[], values: CustomFieldValues | undefined): CustomFieldValues =>
  Object.fromEntries(fields.map((field) => [field.code, toApiValue(field, values?.[field.code])]));

/** Any JSON value as text. */
const text = (value: unknown): string =>
  typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean'
    ? String(value)
    : JSON.stringify(value);

/** A stored value as people read it, for list columns. */
export const displayValue = (
  field: Pick<CustomFieldFormItem, 'type' | 'options'>,
  value: unknown,
  t: TFunction,
  locale: string,
): string => {
  if (value === undefined || value === null || value === '') {
    return '—';
  }

  const option = (code: unknown) => field.options.find((o) => o.code === code)?.label ?? text(code);
  switch (field.type) {
    case 'Bool':
      return t(value ? 'web.common.yes' : 'web.common.no');
    case 'Date':
      return typeof value === 'string' ? formatDate(value, locale) : text(value);
    case 'Number':
      return typeof value === 'number' ? new Intl.NumberFormat(locale).format(value) : text(value);
    case 'Select':
      return option(value);
    case 'MultiSelect':
      return Array.isArray(value) ? value.map(option).join(', ') : text(value);
    default:
      return text(value);
  }
};
