import type { Dayjs } from 'dayjs';
import type { WorkDayDto } from '@/shared/api/generated/model';
import { toApiTime, toPickerTime } from './calendarFormat';

export interface WorkDayValues {
  isWorkingDay?: boolean;
  hours?: [Dayjs, Dayjs] | null;
  break?: [Dayjs, Dayjs] | null;
}

const range = (from: string | null, to: string | null): [Dayjs, Dayjs] | null => {
  const start = toPickerTime(from);
  const end = toPickerTime(to);
  return start && end ? [start, end] : null;
};

/** API day → form row. */
export const toDayValues = (day: WorkDayDto): WorkDayValues => ({
  isWorkingDay: day.isWorkingDay,
  hours: range(day.start, day.end),
  break: range(day.breakStart, day.breakEnd),
});

/** Form row → API day; a day off carries no times. */
export const toWorkDay = (day: WorkDayValues | undefined): WorkDayDto =>
  day?.isWorkingDay
    ? {
        isWorkingDay: true,
        start: toApiTime(day.hours?.[0]),
        end: toApiTime(day.hours?.[1]),
        breakStart: toApiTime(day.break?.[0]),
        breakEnd: toApiTime(day.break?.[1]),
      }
    : { isWorkingDay: false, start: null, end: null, breakStart: null, breakEnd: null };

const minutes = (value: string | null) => {
  if (!value) {
    return 0;
  }

  const [hours = 0, mins = 0] = value.split(':').map(Number);
  return hours * 60 + mins;
};

/** Working hours of a day: start to end, less the break. */
export const dayHours = (day: WorkDayDto): number => {
  if (!day.isWorkingDay || !day.start || !day.end) {
    return 0;
  }

  const breakMinutes = day.breakStart && day.breakEnd ? minutes(day.breakEnd) - minutes(day.breakStart) : 0;
  return Math.max(0, minutes(day.end) - minutes(day.start) - breakMinutes) / 60;
};

const weekday = (start: string, end: string, breakStart: string | null, breakEnd: string | null): WorkDayDto => ({
  isWorkingDay: true,
  start,
  end,
  breakStart,
  breakEnd,
});

const off: WorkDayDto = { isWorkingDay: false, start: null, end: null, breakStart: null, breakEnd: null };

/** Monday to Friday 08:30–18:00 with a 12:30–13:30 break: the schedule a new one starts from. */
export const standardWeek = (): WorkDayDto[] => [
  ...Array.from({ length: 5 }, () => weekday('08:30:00', '18:00:00', '12:30:00', '13:30:00')),
  off,
  off,
];
