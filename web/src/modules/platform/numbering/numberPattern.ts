import type { NumberResetPeriod } from '@/shared/api/generated/model';

/**
 * Mirrors Merp.SharedKernel.Numbering.NumberPattern for the live preview; the API validates again.
 * Tokens: {yyyy}, {yy}, {MM} and exactly one {seq} or {seq:N} (N = 1–12).
 */
const TOKEN = /\{([A-Za-z]+)(?::(\d+))?\}/g;
const MAX_LENGTH = 50;

export type PatternProblem = 'invalid' | 'period';

export const validatePattern = (pattern: string, reset: NumberResetPeriod): PatternProblem | undefined => {
  if (!pattern.trim() || pattern.length > MAX_LENGTH) {
    return 'invalid';
  }

  let sequences = 0;
  for (const [, name, width] of pattern.matchAll(TOKEN)) {
    if (name === 'seq') {
      sequences++;
      if (width !== undefined && (Number(width) < 1 || Number(width) > 12)) {
        return 'invalid';
      }
    } else if (!['yyyy', 'yy', 'MM'].includes(name ?? '') || width !== undefined) {
      return 'invalid';
    }
  }

  if (sequences !== 1 || /[{}]/.test(pattern.replace(TOKEN, ''))) {
    return 'invalid';
  }

  const year = pattern.includes('{yyyy}') || pattern.includes('{yy}');
  if ((reset === 'Yearly' && !year) || (reset === 'Monthly' && (!year || !pattern.includes('{MM}')))) {
    return 'period';
  }

  return undefined;
};

/** The number a valid pattern gives for a date and counter value. */
export const formatPattern = (pattern: string, date: Date, value: number): string =>
  pattern.replace(TOKEN, (_, name: string, width?: string) => {
    switch (name) {
      case 'yyyy':
        return String(date.getFullYear());
      case 'yy':
        return String(date.getFullYear() % 100).padStart(2, '0');
      case 'MM':
        return String(date.getMonth() + 1).padStart(2, '0');
      default:
        return width ? String(value).padStart(Number(width), '0') : String(value);
    }
  });
