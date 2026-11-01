import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type {
  LeaveRequestDetails,
  LeaveRequestDraft,
  LeaveRequestPreview,
  LeaveTypeOption,
  MeResponse,
} from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { LeaveRequestDrawer } from './LeaveRequestDrawer';

const update = vi.fn().mockResolvedValue(undefined);
const preview = vi.fn<() => Promise<LeaveRequestPreview>>();
const create = vi.fn().mockResolvedValue({ id: 'r2' });
const draft = vi.fn<(query: { text: string }) => Promise<LeaveRequestDraft>>();

const annual: LeaveTypeOption = {
  id: 't1',
  code: 'YILLIK',
  name: 'Yıllık ücretli izin',
  category: 'Annual',
  unit: 'HalfDay',
  durationBasis: 'WorkingDays',
  deductsFrom: 'YILLIK',
  requiresDocument: false,
  requiresApproval: true,
  hasFixedLength: false,
};

const returned = {
  request: {
    id: 'r1',
    number: 'IZN-2026-000012',
    startDate: '2026-11-02',
    endDate: '2026-11-04',
    startPart: 'Full',
    endPart: 'Full',
    startTime: null,
    endTime: null,
    travelDays: 0,
    status: 'Returned',
  },
  leaveTypeId: 't1',
  eventDate: null,
  note: 'Aile ziyareti',
  address: null,
  phone: null,
  substituteEmployeeId: null,
  substituteName: null,
  files: [],
} as unknown as LeaveRequestDetails;

const checked = (overrides: Partial<LeaveRequestPreview> = {}): LeaveRequestPreview => ({
  problem: null,
  warnings: [{ code: 'hr.leave.errors.ten_day_block', message: '10 günlük bölüm için bakiye kalmıyor.' }],
  startDate: '2026-11-02',
  endDate: '2026-11-04',
  basis: 'WorkingDays',
  quantity: 3,
  days: 3,
  durationDays: [],
  deductsFrom: 'YILLIK',
  available: 11,
  availableAfter: 8,
  requiresApproval: true,
  requiresDocument: false,
  approval: {
    hasDefinition: true,
    autoApproved: false,
    steps: [
      {
        index: 0,
        name: 'Yönetici onayı',
        approvers: [{ userId: 'u2', name: 'Mehmet Kaya' }],
        skipReason: null,
        needsAttention: false,
      },
    ],
  },
  ...overrides,
});

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useListMyLeaveTypes: () => ({ data: [annual], isLoading: false }),
  useListLeaveRequestTypes: () => ({ data: undefined, isFetching: false }),
  useSearchMyColleagues: () => ({ data: [], isFetching: false }),
  useListEmployees: () => ({ data: undefined, isFetching: false }),
  previewMyLeaveRequest: () => preview(),
  updateMyLeaveRequest: (...args: unknown[]) => update(...args) as Promise<void>,
  createMyLeaveRequest: (...args: unknown[]) => create(...args) as Promise<{ id: string }>,
  draftMyLeaveRequest: (query: { text: string }) => draft(query),
  getListMyLeaveRequestsQueryKey: () => ['/api/hr/me/leave-requests'],
  getListMyLeaveBalancesQueryKey: () => ['/api/hr/me/leave-balances'],
  getListLeaveRequestsQueryKey: () => ['/api/hr/leave-requests'],
  getGetMyLeaveRequestQueryKey: (id: string) => [`/api/hr/me/leave-requests/${id}`],
  getGetLeaveRequestQueryKey: (id: string) => [`/api/hr/leave-requests/${id}`],
}));

const texts: Record<string, string> = {
  'web.hr.leave_requests.send': 'Gönder',
  'web.hr.leave_requests.save_draft': 'Taslak kaydet',
  'web.hr.leave_requests.preview.approvers': 'Onaylayacaklar',
  'web.hr.leave_requests.preview.available_after': 'Talep sonrası',
  'web.hr.leave_requests.days_value': '{days} gün',
  'web.hr.leave_requests.ai.title': 'Yazarak doldur',
  'web.hr.leave_requests.ai.fill': 'Formu doldur',
  'web.hr.leave_requests.ai.notice': 'Form yapay zeka önerisiyle dolduruldu.',
};

const meWithAi = { ai: { enabled: true, features: ['hr.leave_request_draft'] } } as unknown as MeResponse;

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <LeaveRequestDrawer mode="self" request={returned} onClose={vi.fn()} />
    </QueryClientProvider>,
    texts,
  );

const renderNew = (profile?: MeResponse) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={profile}>
        <LeaveRequestDrawer mode="self" onClose={vi.fn()} />
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('LeaveRequestDrawer', () => {
  beforeEach(() => {
    update.mockClear();
    create.mockClear();
    draft.mockReset();
    preview.mockReset();
  });

  it('shows the live check and sends the corrected request', async () => {
    preview.mockResolvedValue(checked());
    render();

    expect(await screen.findByText('10 günlük bölüm için bakiye kalmıyor.')).toBeInTheDocument();
    expect(screen.getByText('Mehmet Kaya')).toBeInTheDocument();
    expect(screen.getByText('8 gün')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Gönder' }));

    await waitFor(() =>
      expect(update).toHaveBeenCalledWith(
        'r1',
        expect.objectContaining({ leaveTypeId: 't1', startDate: '2026-11-02', endDate: '2026-11-04', submit: true }),
      ),
    );
  });

  it('does not send a request the check stops', async () => {
    preview.mockResolvedValue(
      checked({
        problem: {
          code: 'hr.leave.errors.insufficient_balance',
          message: 'Kullanılabilir izin bakiyesi yetersiz.',
          field: 'days',
        },
      }),
    );
    render();

    expect(await screen.findByText('Kullanılabilir izin bakiyesi yetersiz.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Gönder' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Taslak kaydet' })).toBeEnabled();
  });

  it('fills a new request from what the employee writes and sends it', async () => {
    preview.mockResolvedValue(checked({ warnings: [] }));
    draft.mockResolvedValue({
      leaveTypeId: 't1',
      startDate: '2026-11-06',
      endDate: '2026-11-09',
      startPart: 'Full',
      endPart: 'Full',
      startTime: null,
      endTime: null,
      eventDate: null,
      note: 'Aile ziyareti',
      questions: ['Yol izni ekleyecek misiniz?'],
      model: 'model',
      promptVersion: 'v1',
    });
    renderNew(meWithAi);

    await userEvent.type(
      screen.getByRole('textbox', { name: 'Yazarak doldur' }),
      'Gelecek cuma ve pazartesi yıllık izin',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Formu doldur' }));

    expect(await screen.findByText('Yol izni ekleyecek misiniz?')).toBeInTheDocument();
    expect(screen.getByText('Form yapay zeka önerisiyle dolduruldu.')).toBeInTheDocument();
    expect(draft).toHaveBeenCalledWith(expect.objectContaining({ text: 'Gelecek cuma ve pazartesi yıllık izin' }));

    await userEvent.click(screen.getByRole('button', { name: 'Gönder' }));

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(
        expect.objectContaining({
          leaveTypeId: 't1',
          startDate: '2026-11-06',
          endDate: '2026-11-09',
          note: 'Aile ziyareti',
          submit: true,
        }),
      ),
    );
  });

  it('offers filling by text only when the feature is on', () => {
    preview.mockResolvedValue(checked());
    renderNew();

    expect(screen.queryByRole('button', { name: 'Formu doldur' })).not.toBeInTheDocument();
  });
});
