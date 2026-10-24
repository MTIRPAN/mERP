import type { EventInput } from '@fullcalendar/core';
import dayjs from 'dayjs';
import type { TFunction } from 'i18next';
import type { LeaveCalendar, LeaveCategory } from '@/shared/api/generated/model';

/** One colour per kind of leave, so a month reads at a glance (PLAN L5). */
export const categoryColors: Record<LeaveCategory, string> = {
  Annual: '#1677ff',
  Excuse: '#722ed1',
  Maternity: '#eb2f96',
  Unpaid: '#8c8c8c',
  Arrangement: '#52c41a',
  Sick: '#fa541c',
  Administrative: '#13c2c2',
  Travel: '#faad14',
};

const HOLIDAY_COLOR = '#fff1f0';

/**
 * The calendar's events: a leave runs over its days and its travel days (FullCalendar ends are exclusive); a
 * request waiting for approval is paler and says so; public holidays are background days.
 */
export const toCalendarEvents = (calendar: LeaveCalendar | undefined, t: TFunction): EventInput[] => [
  ...(calendar?.events ?? []).map((leave) => {
    const pending = leave.status !== 'Approved' && leave.status !== 'CancellationPending';
    const notes = [
      pending ? t('web.hr.leave_calendar.pending') : null,
      leave.travelDays > 0 ? t('web.hr.leave_calendar.travel') : null,
      leave.startPart !== 'Full' || leave.endPart !== 'Full' ? t('web.hr.leave_calendar.half_day') : null,
    ].filter(Boolean);
    const color = categoryColors[leave.category];
    return {
      id: leave.requestId,
      title: `${leave.employeeName} · ${leave.leaveTypeName}${notes.length > 0 ? ` (${notes.join(', ')})` : ''}`,
      start: leave.startDate,
      end: dayjs(leave.endDate)
        .add(leave.travelDays + 1, 'day')
        .format('YYYY-MM-DD'),
      allDay: true,
      backgroundColor: pending ? `${color}66` : color,
      borderColor: color,
      textColor: pending ? '#000000' : '#ffffff',
    };
  }),
  ...(calendar?.holidays ?? []).map((holiday) => ({
    id: `holiday-${holiday.date}`,
    title: holiday.name,
    start: holiday.date,
    allDay: true,
    display: 'background',
    backgroundColor: HOLIDAY_COLOR,
  })),
];
