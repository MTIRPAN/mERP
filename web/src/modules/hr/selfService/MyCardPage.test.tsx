import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { EmployeeDetails, ProfileChangeItem } from '@/shared/api/generated/model';
import { ApiError } from '@/shared/api/problem';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { MyCardPage } from './MyCardPage';

const card = vi.fn<() => { data?: EmployeeDetails; isError: boolean; error: unknown }>();
const changes = vi.fn<() => { data?: ProfileChangeItem[]; isLoading: boolean }>();
const withdraw = vi.fn().mockResolvedValue(undefined);

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useGetMyEmployee: () => card(),
  useListMyProfileChanges: () => changes(),
  useWithdrawProfileChange: () => ({ mutateAsync: withdraw }),
  useSubmitIbanChange: () => ({ mutateAsync: vi.fn() }),
  getListMyProfileChangesQueryKey: () => ['/api/hr/me/profile-changes'],
}));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetCustomFieldForm: () => ({ data: [] }),
  useGetApprovalTimeline: () => ({ isLoading: false, data: undefined }),
}));

const texts: Record<string, string> = {
  'web.hr.me.title': 'Kartım',
  'web.hr.me.not_linked': 'Hesabınız bir personel kaydına bağlı değil.',
  'web.hr.me.request_change': 'Değişiklik iste',
  'web.hr.me.tabs.requests': 'Taleplerim',
  'web.hr.me.withdraw': 'Geri çek',
  'web.hr.me.withdraw_confirm': '{number} numaralı talep geri çekilsin mi?',
  'web.hr.me.correct': 'Düzelt ve gönder',
  'web.hr.employees.tabs.general': 'Genel',
  'web.hr.employees.tabs.contacts': 'İletişim',
  'web.hr.employees.sensitive.title': 'Özel nitelikli kişisel veriler',
  'web.hr.employees.fields.iban': 'IBAN',
  'web.hr.profile_changes.request.iban': 'IBAN değişiklik talebi',
  'web.hr.profile_changes.kind.Iban': 'IBAN',
  'web.hr.profile_changes.kind.Contacts': 'İletişim bilgileri',
  'web.hr.profile_changes.status.Pending': 'Onay bekliyor',
  'web.hr.profile_changes.status.Returned': 'Düzeltme için iade edildi',
};

const details = (): EmployeeDetails =>
  ({
    id: 'e1',
    tckn: '10000000146',
    fullName: 'Ayşe Yılmaz',
    iban: 'TR330006100519786457841326',
    sensitiveVisible: true,
    customFields: {},
    emails: [],
    phones: [],
    addresses: [],
    emergencyContacts: [],
    familyMembers: [],
    educations: [],
    certificates: [],
    languages: [],
    employments: [],
    sensitive: { hidden: true, disabilityDegree: null, bloodType: null, healthNotes: null, criminalRecordDate: null },
    version: 1,
  }) as unknown as EmployeeDetails;

const request = (overrides: Partial<ProfileChangeItem>): ProfileChangeItem => ({
  id: 'r1',
  number: 'PDT-2026-00012',
  kind: 'Iban',
  status: 'Pending',
  proposed: null,
  proposedIban: 'TR120006200000000006295011',
  note: null,
  returnComment: null,
  createdAt: '2026-10-22T09:00:00Z',
  closedAt: null,
  ...overrides,
});

const render = (route = '/hr/me', permissions = ['hr.self.profile.read', 'hr.self.profile.request']) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <MyCardPage />
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('MyCardPage', () => {
  beforeEach(() => {
    card.mockReset().mockReturnValue({ data: details(), isError: false, error: null });
    changes.mockReset().mockReturnValue({ data: [], isLoading: false });
    withdraw.mockClear();
  });

  it("shows the person's own TCKN and IBAN without the special category data", () => {
    render();

    expect(screen.getByText('10000000146')).toBeInTheDocument();
    expect(screen.getByText('TR330006100519786457841326')).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: 'lock' })).not.toBeInTheDocument();
    expect(screen.queryByText('Özel nitelikli kişisel veriler')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Değişiklik iste/ })).toBeEnabled();
  });

  it('says so when the user has no person', () => {
    card.mockReturnValue({ data: undefined, isError: true, error: new ApiError(404, { status: 404 }) });

    render();

    expect(screen.getByText('Hesabınız bir personel kaydına bağlı değil.')).toBeInTheDocument();
  });

  it('offers no change while a request of the section is open, and withdraws it from the requests', async () => {
    changes.mockReturnValue({ data: [request({})], isLoading: false });

    render('/hr/me?tab=requests');

    expect(await screen.findByText('PDT-2026-00012')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Geri çek' }));
    const confirm = await screen.findByRole('tooltip');
    await userEvent.click(within(confirm).getByRole('button', { name: 'Geri çek' }));
    await waitFor(() => expect(withdraw).toHaveBeenCalledWith({ id: 'r1' }));

    await userEvent.click(screen.getByRole('tab', { name: 'Genel' }));
    expect(screen.getByRole('button', { name: /Değişiklik iste/ })).toBeDisabled();
  });

  it('opens a returned request for correction with what it proposed', async () => {
    changes.mockReturnValue({
      data: [request({ status: 'Returned', returnComment: 'Hesap cüzdanını ekleyin' })],
      isLoading: false,
    });

    render('/hr/me?tab=requests');

    await userEvent.click(await screen.findByRole('button', { name: 'Düzelt ve gönder' }));

    expect(await screen.findByText('IBAN değişiklik talebi')).toBeInTheDocument();
    expect(screen.getByDisplayValue('TR120006200000000006295011')).toBeInTheDocument();
  });

  it('shows the card without requests to a user who may only read it', () => {
    render('/hr/me', ['hr.self.profile.read']);

    expect(screen.queryByRole('tab', { name: 'Taleplerim' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Değişiklik iste/ })).not.toBeInTheDocument();
  });
});
