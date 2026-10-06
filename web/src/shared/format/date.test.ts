import { describe, expect, it } from 'vitest';
import { formatDate } from './date';

describe('formatDate', () => {
  it('keeps the calendar day whatever the time zone', () => {
    expect(formatDate('2026-01-01', 'tr-TR')).toBe('1 Oca 2026');
    expect(formatDate('2026-12-31', 'en-US')).toBe('Dec 31, 2026');
  });
});
