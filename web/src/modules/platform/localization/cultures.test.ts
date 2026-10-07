import { describe, expect, it } from 'vitest';
import { coverage, cultureLabel } from './cultures';

describe('cultures', () => {
  it('names cultures in the user language', () => {
    expect(cultureLabel('en-US', 'tr-TR')).toMatch(/^İngilizce \(.+\)$/);
    expect(cultureLabel('de-DE', 'en-US')).toBe('German (Germany)');
    expect(cultureLabel('not a code', 'tr-TR')).toBe('not a code');
  });

  it('rounds coverage down, so 100% means complete', () => {
    expect(coverage(999, 1000)).toBe(99);
    expect(coverage(1000, 1000)).toBe(100);
    expect(coverage(0, 0)).toBe(0);
  });
});
