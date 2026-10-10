import { describe, expect, it } from 'vitest';
import { isoDate, monthName, monthWeeks, shortTime, toApiTime, toPickerTime, weekdayNames } from './calendarFormat';

describe('calendarFormat', () => {
  it('converts times between the API and the picker', () => {
    expect(toApiTime(toPickerTime('08:30:00'))).toBe('08:30:00');
    expect(toPickerTime(null)).toBeUndefined();
    expect(toApiTime(undefined)).toBeNull();
    expect(shortTime('13:00:00')).toBe('13:00');
  });

  it('names the week days Monday first and the months in the culture', () => {
    expect(weekdayNames('tr-TR')).toEqual(['Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi', 'Pazar']);
    expect(monthName('tr-TR', 10)).toBe('Ekim');
  });

  it('lays out a month in weeks starting on Monday', () => {
    // 1 October 2026 is a Thursday
    const weeks = monthWeeks(2026, 10);
    expect(weeks[0]).toEqual([null, null, null, 1, 2, 3, 4]);
    expect(weeks.at(-1)).toEqual([26, 27, 28, 29, 30, 31, null]);
    expect(weeks.flat().filter((day) => day !== null)).toHaveLength(31);
  });

  it('writes dates as the API does', () => {
    expect(isoDate(2026, 5, 9)).toBe('2026-05-09');
  });
});
