import type { TFunction } from 'i18next';
import { describe, expect, it } from 'vitest';
import { createTestI18n } from '@/test/render';
import { assignmentState, periodText, scopeText } from './assignments';

const t = createTestI18n({
  'web.platform.assignments.scope_named': '{type}: {name}',
  'web.platform.assignments.with_sub_units': '{scope} (alt birimler dahil)',
  'web.platform.assignments.period_range': '{from} – {to}',
  'web.platform.assignments.period_from': '{from} itibarıyla',
  'web.platform.assignments.period_to': '{to} tarihine kadar',
  'web.platform.assignments.period_open': 'Süresiz',
}).t as TFunction;

describe('assignments', () => {
  it('names the scope record and the sub-units', () => {
    expect(scopeText({ scopeTypeLabel: 'Tüm şirketler', scopeName: null, includeSubUnits: false }, t)).toBe(
      'Tüm şirketler',
    );
    expect(scopeText({ scopeTypeLabel: 'Birim', scopeName: 'Satış', includeSubUnits: true }, t)).toBe(
      'Birim: Satış (alt birimler dahil)',
    );
  });

  it('describes open and closed periods', () => {
    expect(periodText({ validFrom: null, validTo: null }, t, 'tr-TR')).toBe('Süresiz');
    expect(periodText({ validFrom: '2026-01-01', validTo: null }, t, 'tr-TR')).toBe('1 Oca 2026 itibarıyla');
    expect(periodText({ validFrom: null, validTo: '2026-03-31' }, t, 'tr-TR')).toBe('31 Mar 2026 tarihine kadar');
    expect(periodText({ validFrom: '2026-01-01', validTo: '2026-03-31' }, t, 'tr-TR')).toBe('1 Oca 2026 – 31 Mar 2026');
  });

  it('tells future assignments from expired ones', () => {
    expect(assignmentState({ isCurrent: true, validFrom: null }, '2026-10-07')).toBe('current');
    expect(assignmentState({ isCurrent: false, validFrom: '2026-11-01' }, '2026-10-07')).toBe('future');
    expect(assignmentState({ isCurrent: false, validFrom: '2026-01-01' }, '2026-10-07')).toBe('expired');
  });
});
