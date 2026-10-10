import dayjs from 'dayjs';
import type { TFunction } from 'i18next';
import { describe, expect, it } from 'vitest';
import type { CustomFieldFormItem } from '@/shared/api/generated/model';
import { createTestI18n } from '@/test/render';
import { displayValue, toApiValues, toFormValues } from './customFieldValues';

const field = (
  code: string,
  type: CustomFieldFormItem['type'],
  options: CustomFieldFormItem['options'] = [],
): CustomFieldFormItem => ({
  code,
  label: code,
  type,
  required: false,
  options,
  maxLength: null,
  min: null,
  max: null,
  wholeNumber: false,
  group: null,
});

const fields = [
  field('size', 'Number'),
  field('since', 'Date'),
  field('tags', 'MultiSelect', [
    { code: 'a', label: 'Alfa' },
    { code: 'b', label: 'Beta' },
  ]),
  field('note', 'Text'),
];

const t = createTestI18n({ 'web.common.yes': 'Evet', 'web.common.no': 'Hayır' }).t as TFunction;

describe('customFieldValues', () => {
  it('turns stored values into form values and back', () => {
    const form = toFormValues(fields, { size: 42, since: '2026-02-28', tags: ['a'], other: 'kept by the API' });

    expect(dayjs.isDayjs(form.since)).toBe(true);
    expect(Object.keys(form)).toEqual(['size', 'since', 'tags', 'note']);
    expect(toApiValues(fields, { ...form, note: '', tags: [] })).toEqual({
      size: 42,
      since: '2026-02-28',
      tags: null,
      note: null,
    });
  });

  it('shows values as people read them', () => {
    expect(displayValue(field('ok', 'Bool'), true, t, 'tr-TR')).toBe('Evet');
    expect(displayValue(fields[0]!, 1234.5, t, 'tr-TR')).toBe('1.234,5');
    expect(displayValue(fields[1]!, '2026-02-28', t, 'tr-TR')).toBe('28 Şub 2026');
    expect(displayValue(fields[2]!, ['a', 'b'], t, 'tr-TR')).toBe('Alfa, Beta');
    expect(displayValue(fields[3]!, null, t, 'tr-TR')).toBe('—');
  });
});
