import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { CreateWorkScheduleMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { MeResponse, WorkScheduleItem } from '@/shared/api/generated/model';
import { ApiError } from '@/shared/api/problem';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { standardWeek } from './workScheduleForm';
import { WorkSchedulesPage } from './WorkSchedulesPage';

const createSchedule = vi.fn<(variables: CreateWorkScheduleMutationVariables) => Promise<unknown>>();

const schedule: WorkScheduleItem = {
  id: 's1',
  code: 'STANDART',
  name: { 'tr-TR': 'Standart' },
  displayName: 'Standart',
  companyId: null,
  companyName: null,
  holidayCalendarId: 'tr',
  days: standardWeek(),
  workingDaysPerWeek: 5,
  weeklyHours: 42.5,
  isDefault: true,
  isActive: true,
  version: 1,
};

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  getListWorkSchedulesQueryKey: () => ['/api/platform/work-schedules'],
  useListHolidayCalendars: () => ({ data: [{ id: 'tr', code: 'TR', name: 'Türkiye' }], isLoading: false }),
  useListWorkSchedules: () => ({ data: [schedule], isFetching: false }),
  useCreateWorkSchedule: () => ({ mutateAsync: createSchedule }),
  useUpdateWorkSchedule: () => ({ mutateAsync: vi.fn() }),
}));

const texts: Record<string, string> = {
  'web.calendars.schedules_title': 'Çalışma takvimleri',
  'web.calendars.schedule_new': 'Yeni çalışma takvimi',
  'web.calendars.name': 'Ad',
  'web.calendars.default': 'Varsayılan',
  'web.calendars.all_companies': 'Tüm şirketler',
  'web.calendars.weekly_summary': '{days} gün, {hours} saat',
  'web.calendars.weekly_hours': 'Haftalık toplam: {hours} saat',
  'web.platform.fields.code': 'Kod',
  'web.platform.fields.required': 'Bu alan zorunludur.',
  'web.common.save': 'Kaydet',
};

const me = { cultures: [{ code: 'tr-TR', name: 'Türkçe' }], companies: [] } as unknown as MeResponse;

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <WorkSchedulesPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

const openNew = async () => {
  await userEvent.click(screen.getByRole('button', { name: /Yeni çalışma takvimi/ }));
  const drawer = within(await screen.findByRole('dialog'));
  await userEvent.type(drawer.getByLabelText(/^Kod/), 'VARDIYA');
  await userEvent.type(drawer.getByLabelText('Ad'), 'Vardiya');
  return drawer;
};

describe('WorkSchedulesPage', () => {
  beforeEach(() => {
    createSchedule.mockReset().mockResolvedValue({ id: 's2' });
  });

  it('lists the schedules with their week', () => {
    render(['platform.calendars.read']);

    expect(screen.getByText('Standart')).toBeInTheDocument();
    expect(screen.getByText('Varsayılan')).toBeInTheDocument();
    expect(screen.getByText('Tüm şirketler')).toBeInTheDocument();
    expect(screen.getByText('5 gün, 42,5 saat')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni çalışma takvimi/ })).not.toBeInTheDocument();
  });

  it('creates a schedule from the standard week', async () => {
    render(['platform.calendars.read', 'platform.calendars.manage']);

    const drawer = await openNew();
    expect(drawer.getByText('Haftalık toplam: 42,5 saat')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() => expect(createSchedule).toHaveBeenCalledTimes(1));
    expect(createSchedule.mock.calls[0]![0].data).toEqual({
      code: 'VARDIYA',
      name: { 'tr-TR': 'Vardiya' },
      companyId: null,
      holidayCalendarId: 'tr',
      days: standardWeek(),
      isDefault: false,
    });
  });

  it('shows a day error of the API next to that day', async () => {
    createSchedule.mockRejectedValue(
      new ApiError(400, {
        status: 400,
        fieldErrors: [
          { field: 'days[2]', code: 'platform.calendars.errors.invalid_work_day', message: 'Saatler hatalı.' },
        ],
      }),
    );
    render(['platform.calendars.read', 'platform.calendars.manage']);

    const drawer = await openNew();
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    expect(await drawer.findByText('Saatler hatalı.')).toBeInTheDocument();
  });
});
