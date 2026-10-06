import type { TFunction } from 'i18next';
import { describe, expect, it } from 'vitest';
import type { AuditItem } from '@/shared/api/generated/model';
import { createTestI18n } from '@/test/render';
import { displayValue, groupOperations } from './auditView';

const t = createTestI18n({
  'web.audit.masked': 'Gizli (değer saklanmaz)',
  'web.common.yes': 'Evet',
  'web.common.no': 'Hayır',
}).t as TFunction;

const item = (overrides: Partial<AuditItem>): AuditItem => ({
  id: crypto.randomUUID(),
  module: 'platform',
  occurredAt: '2026-10-07T09:00:00Z',
  userId: 'u1',
  userName: 'Ayşe Yılmaz',
  entityType: 'Company',
  entityLabel: 'Şirket',
  entityId: 'c1',
  action: 'Update',
  field: 'ShortName',
  fieldLabel: 'Kısa ad',
  oldValue: 'Acme',
  newValue: 'Acme Yeni',
  correlationId: 'trace-2',
  ...overrides,
});

describe('groupOperations', () => {
  it('puts the field changes of one request together', () => {
    const operations = groupOperations([
      item({ field: 'ShortName' }),
      item({ field: 'Address.City', fieldLabel: 'İl' }),
      item({ action: 'Insert', correlationId: 'trace-1', occurredAt: '2026-10-06T09:00:00Z' }),
    ]);

    expect(operations.map((o) => [o.action, o.changes.map((c) => c.label)])).toEqual([
      ['Update', ['Kısa ad', 'İl']],
      ['Insert', ['Kısa ad']],
    ]);
  });

  it('keeps changes of different records apart within a request', () => {
    const operations = groupOperations([item({ entityId: 'c1' }), item({ entityId: 'c2' })]);

    expect(operations).toHaveLength(2);
  });
});

describe('displayValue', () => {
  it('shows values as people read them', () => {
    expect(displayValue(null, t)).toBe('—');
    expect(displayValue('True', t)).toBe('Evet');
    expect(displayValue('***', t)).toBe('Gizli (değer saklanmaz)');
    expect(displayValue('{"tr-TR":"Bordro Uzmanı"}', t)).toBe('Bordro Uzmanı');
    expect(displayValue('{not json', t)).toBe('{not json');
    expect(displayValue('Ankara', t)).toBe('Ankara');
  });
});
