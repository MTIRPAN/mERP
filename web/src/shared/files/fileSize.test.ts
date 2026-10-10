import { describe, expect, it } from 'vitest';
import { formatFileSize } from './fileSize';

describe('formatFileSize', () => {
  it('uses the largest unit below 1024', () => {
    expect(formatFileSize(512, 'en-US')).toBe('512 byte');
    expect(formatFileSize(1536, 'en-US')).toBe('1.5 kB');
    expect(formatFileSize(20 * 1024 * 1024, 'en-US')).toBe('20 MB');
  });

  it('formats numbers in the given culture', () => {
    expect(formatFileSize(1536, 'tr-TR')).toContain('1,5');
  });
});
