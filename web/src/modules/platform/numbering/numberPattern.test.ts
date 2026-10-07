import { describe, expect, it } from 'vitest';
import { formatPattern, validatePattern } from './numberPattern';

const date = new Date(2026, 2, 7);

describe('numberPattern', () => {
  it('formats like the API', () => {
    expect(formatPattern('IZN-{yyyy}-{seq:6}', date, 42)).toBe('IZN-2026-000042');
    expect(formatPattern('{yy}{MM}/{seq}', date, 7)).toBe('2603/7');
    expect(formatPattern('{seq:3}', date, 1234)).toBe('1234');
  });

  it('validates like the API', () => {
    expect(validatePattern('IZN-{yyyy}-{seq:6}', 'Yearly')).toBeUndefined();
    expect(validatePattern('{yyyy}/{MM}/{seq:4}', 'Monthly')).toBeUndefined();
    expect(validatePattern('IZN-{seq:6}', 'Yearly')).toBe('period');
    expect(validatePattern('{yyyy}-{seq}', 'Monthly')).toBe('period');
    expect(validatePattern('IZN-{yyyy}', 'Never')).toBe('invalid');
    expect(validatePattern('{seq}-{seq}', 'Never')).toBe('invalid');
    expect(validatePattern('{seq:13}', 'Never')).toBe('invalid');
    expect(validatePattern('{dd}-{seq}', 'Never')).toBe('invalid');
    expect(validatePattern('A{-{seq}', 'Never')).toBe('invalid');
  });
});
