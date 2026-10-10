import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type {
  AddHolidayMutationVariables,
  CopyFixedHolidaysMutationVariables,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { HolidayYear, ListHolidaysParams, MeResponse } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { HolidaysPage } from './HolidaysPage';

const addHoliday = vi.fn<(variables: AddHolidayMutationVariables) => Promise<unknown>>();
const copyFixed = vi.fn<(variables: CopyFixedHolidaysMutationVariables) => Promise<{ count: number }>>();
let movable = true;

const holidays = (year: number): HolidayYear => ({
  calendarId: 'tr',
  year,
  hasMovableHolidays: movable,
  items: [
    {
      id: 'h1',
      date: `${year}-10-28`,
      name: { 'tr-TR': 'Cumhuriyet Bayramı arifesi' },
      displayName: 'Cumhuriyet Bayramı arifesi',
      duration: 'Half',
      startsAt: '13:00:00',
      isMovable: false,
    },
    {
      id: 'h2',
      date: `${year}-10-29`,
      name: { 'tr-TR': 'Cumhuriyet Bayramı' },
      displayName: 'Cumhuriyet Bayramı',
      duration: 'Full',
      startsAt: null,
      isMovable: false,
    },
  ],
});

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  getListHolidaysQueryKey: (id: string) => [`/api/platform/holiday-calendars/${id}/holidays`],
  useListHolidayCalendars: () => ({ data: [{ id: 'tr', code: 'TR', name: 'Türkiye' }] }),
  useListHolidays: (_id: string, params: ListHolidaysParams) => ({
    data: holidays(params.year),
    isLoading: false,
    isFetching: false,
  }),
  useAddHoliday: () => ({ mutateAsync: addHoliday }),
  useUpdateHoliday: () => ({ mutateAsync: vi.fn() }),
  useRemoveHoliday: () => ({ mutateAsync: vi.fn() }),
  useCopyFixedHolidays: () => ({ mutateAsync: copyFixed, isPending: false }),
}));

const texts: Record<string, string> = {
  'web.calendars.holidays_title': 'Resmi tatiller',
  'web.calendars.holiday_new': 'Tatil ekle',
  'web.calendars.holiday_edit': 'Tatili düzenle',
  'web.calendars.name': 'Ad',
  'web.calendars.date': 'Tarih',
  'web.calendars.full_day': 'Tam gün',
  'web.calendars.half_day_at': '{time} itibarıyla yarım gün',
  'web.calendars.half_day_from': '{name} ({time} itibarıyla)',
  'web.calendars.missing_movable': '{year} yılı için dini bayramlar girilmemiş.',
  'web.calendars.copy_fixed': '{year} sabit tatillerini kopyala',
  'web.calendars.copy_confirm': '{from} → {to}',
  'web.calendars.copied': '{count} tatil kopyalandı.',
  'web.platform.fields.required': 'Bu alan zorunludur.',
  'web.common.save': 'Kaydet',
};

const me = { cultures: [{ code: 'tr-TR', name: 'Türkçe' }], companies: [] } as unknown as MeResponse;
const year = new Date().getFullYear();

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <HolidaysPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('HolidaysPage', () => {
  beforeEach(() => {
    movable = true;
    addHoliday.mockReset().mockResolvedValue({ id: 'h3' });
    copyFixed.mockReset().mockResolvedValue({ count: 3 });
  });

  it('marks full and half day holidays in the year view and lists them', () => {
    const { container } = render(['platform.calendars.read']);

    expect(container.querySelector(`[data-date="${year}-10-29"]`)).toHaveAttribute('data-holiday', 'Full');
    expect(container.querySelector(`[data-date="${year}-10-28"]`)).toHaveAttribute('data-holiday', 'Half');
    expect(container.querySelector(`[data-date="${year}-10-27"]`)).not.toHaveAttribute('data-holiday');
    expect(screen.getByText('13:00 itibarıyla yarım gün')).toBeInTheDocument();
    expect(screen.getByText('Cumhuriyet Bayramı')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Tatil ekle/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/dini bayramlar girilmemiş/)).not.toBeInTheDocument();
  });

  it('warns when the religious holidays of the year are missing', () => {
    movable = false;
    render(['platform.calendars.read']);

    expect(screen.getByText(`${year} yılı için dini bayramlar girilmemiş.`)).toBeInTheDocument();
  });

  it('adds a holiday on the clicked day', async () => {
    const { container } = render(['platform.calendars.read', 'platform.calendars.manage']);

    await userEvent.click(container.querySelector(`[data-date="${year}-04-23"]`)!);
    const drawer = within(await screen.findByRole('dialog'));
    expect(drawer.getByText('Tatil ekle')).toBeInTheDocument();
    await userEvent.type(drawer.getByLabelText('Ad'), 'Ulusal Egemenlik ve Çocuk Bayramı');
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() => expect(addHoliday).toHaveBeenCalledTimes(1));
    expect(addHoliday.mock.calls[0]![0]).toEqual({
      id: 'tr',
      data: {
        date: `${year}-04-23`,
        name: { 'tr-TR': 'Ulusal Egemenlik ve Çocuk Bayramı' },
        duration: 'Full',
        startsAt: null,
        isMovable: false,
      },
    });
  });

  it('opens a holiday from the year view for editing', async () => {
    const { container } = render(['platform.calendars.read', 'platform.calendars.manage']);

    await userEvent.click(container.querySelector(`[data-date="${year}-10-29"]`)!);

    const drawer = within(await screen.findByRole('dialog'));
    expect(drawer.getByText('Tatili düzenle')).toBeInTheDocument();
    expect(drawer.getByLabelText('Ad')).toHaveValue('Cumhuriyet Bayramı');
  });

  it('copies the fixed holidays of the previous year', async () => {
    render(['platform.calendars.read', 'platform.calendars.manage']);

    await userEvent.click(screen.getByRole('button', { name: new RegExp(`${year - 1} sabit tatillerini kopyala`) }));
    await userEvent.click(await screen.findByRole('button', { name: /OK|Tamam/ }));

    await waitFor(() => expect(copyFixed).toHaveBeenCalledWith({ id: 'tr', data: { year } }));
    expect(await screen.findByText('3 tatil kopyalandı.')).toBeInTheDocument();
  });
});
