import type { EventInput } from '@fullcalendar/core';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { TFunction } from 'i18next';
import type * as ReactRouter from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import type { LeaveCalendar, LeaveCalendarEvent } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { LeaveCalendarPage } from './LeaveCalendarPage';
import { toCalendarEvents } from './leaveCalendar';

const navigate = vi.fn();

const leave = (overrides: Partial<LeaveCalendarEvent>): LeaveCalendarEvent => ({
  requestId: 'r1',
  number: 'IZN-2026-000012',
  employeeId: 'e1',
  employeeName: 'Ayşe Yılmaz',
  companyId: 'c1',
  orgUnitId: 'u1',
  orgUnitName: 'Satış',
  leaveTypeCode: 'YILLIK',
  leaveTypeName: 'Yıllık ücretli izin',
  category: 'Annual',
  startDate: '2026-05-04',
  endDate: '2026-05-06',
  startPart: 'Full',
  endPart: 'Full',
  startTime: null,
  endTime: null,
  travelDays: 0,
  status: 'Approved',
  ...overrides,
});

const calendar: LeaveCalendar = {
  events: [leave({}), leave({ requestId: 'r2', employeeName: 'Mehmet Kaya', status: 'Pending', travelDays: 2 })],
  holidays: [{ date: '2026-05-19', name: 'Atatürk’ü Anma, Gençlik ve Spor Bayramı', halfDay: false }],
  alerts: [{ date: '2026-05-05', orgUnitId: 'u1', orgUnitName: 'Satış', count: 3, threshold: 2 }],
};

vi.mock('react-router', async (original) => ({
  ...(await original<typeof ReactRouter>()),
  useNavigate: () => navigate,
}));

vi.mock('@/app/me/meContext', () => ({ useMe: () => ({ companies: [{ id: 'c1', name: 'Acme' }] }) }));

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useGetLeaveCalendar: () => ({ data: calendar }),
  useGetOrgUnitTree: () => ({ data: undefined, isLoading: false }),
}));

// FullCalendar draws with layout jsdom lacks: a stand-in lists the events and reports a click
vi.mock('@fullcalendar/react', () => ({
  default: ({
    events,
    eventClick,
  }: {
    events: EventInput[];
    eventClick: (info: { event: { id: string } }) => void;
  }) => (
    <ul>
      {events.map((e) => (
        <li key={e.id}>
          <button type="button" onClick={() => eventClick({ event: { id: e.id! } })}>
            {e.title}
          </button>
        </li>
      ))}
    </ul>
  ),
}));

const texts: Record<string, string> = {
  'web.hr.leave_calendar.pending': 'onay bekliyor',
  'web.hr.leave_calendar.travel': 'yol izni',
  'web.hr.leave_calendar.alerts_title': 'Birimde aynı gün çok kişi izinde',
  'web.hr.leave_calendar.alert': '{date} · {unit}: {count} kişi izinde (eşik {threshold})',
};

const t = ((key: string) => texts[key] ?? key) as TFunction;

describe('toCalendarEvents', () => {
  it('runs a leave over its travel days, marks the pending ones and lays the holidays under them', () => {
    const events = toCalendarEvents(calendar, t);

    expect(events[0]).toMatchObject({
      id: 'r1',
      start: '2026-05-04',
      end: '2026-05-07',
      title: 'Ayşe Yılmaz · Yıllık ücretli izin',
    });
    expect(events[1]).toMatchObject({
      end: '2026-05-09',
      title: 'Mehmet Kaya · Yıllık ücretli izin (onay bekliyor, yol izni)',
    });
    expect(events[2]).toMatchObject({ start: '2026-05-19', display: 'background' });
  });
});

describe('LeaveCalendarPage', () => {
  it('shows the crowded days and opens a leave for those who read the requests', async () => {
    renderWithProviders(
      <PermissionProvider permissions={['hr.leave.calendar.read', 'hr.leave.requests.read']}>
        <LeaveCalendarPage />
      </PermissionProvider>,
      texts,
    );

    expect(screen.getByText('Birimde aynı gün çok kişi izinde')).toBeInTheDocument();
    expect(screen.getByText(/Satış: 3 kişi izinde \(eşik 2\)/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Ayşe Yılmaz · Yıllık ücretli izin' }));
    expect(navigate).toHaveBeenCalledWith('/hr/leave-requests/r1');

    navigate.mockClear();
    await userEvent.click(screen.getByRole('button', { name: /Anma/ }));
    expect(navigate).not.toHaveBeenCalled();
  });
});
