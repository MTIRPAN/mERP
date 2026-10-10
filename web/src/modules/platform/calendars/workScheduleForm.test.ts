import { describe, expect, it } from 'vitest';
import type { WorkDayDto } from '@/shared/api/generated/model';
import { dayHours, standardWeek, toDayValues, toWorkDay } from './workScheduleForm';

const day = (index: number) => standardWeek()[index] as WorkDayDto;

describe('workScheduleForm', () => {
  it('counts the hours of a day less the break', () => {
    expect(standardWeek()).toHaveLength(7);
    expect(dayHours(day(0))).toBe(8.5);
    expect(dayHours(day(6))).toBe(0);
    expect(dayHours({ ...day(0), breakStart: null, breakEnd: null })).toBe(9.5);
  });

  it('round trips a day through the form', () => {
    expect(toWorkDay(toDayValues(day(0)))).toEqual(day(0));
  });

  it('drops the times of a day off', () => {
    const values = { ...toDayValues(day(0)), isWorkingDay: false };

    expect(toWorkDay(values)).toEqual({
      isWorkingDay: false,
      start: null,
      end: null,
      breakStart: null,
      breakEnd: null,
    });
  });
});
