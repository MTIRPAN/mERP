import { describe, expect, it } from 'vitest';
import { formatDate, formatRelativeTime } from './date';

describe('formatDate', () => {
  it('keeps the calendar day whatever the time zone', () => {
    expect(formatDate('2026-01-01', 'tr-TR')).toBe('1 Oca 2026');
    expect(formatDate('2026-12-31', 'en-US')).toBe('Dec 31, 2026');
  });
});

describe('formatRelativeTime', () => {
  const now = Date.parse('2026-10-10T12:00:00Z');

  it('uses the largest whole unit', () => {
    expect(formatRelativeTime('2026-10-10T11:55:00Z', 'tr-TR', now)).toBe('5 dakika önce');
    expect(formatRelativeTime('2026-10-10T09:00:00Z', 'tr-TR', now)).toBe('3 saat önce');
    expect(formatRelativeTime('2026-10-09T12:00:00Z', 'tr-TR', now)).toBe('dün');
    expect(formatRelativeTime('2026-10-10T11:59:40Z', 'en-US', now)).toBe('now');
  });
});
