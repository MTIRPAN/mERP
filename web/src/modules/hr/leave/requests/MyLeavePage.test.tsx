import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { LeaveBalanceItem, LeaveRequestDetails, LeaveRequestListItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { MyLeavePage } from './MyLeavePage';

const requestCancellation = vi.fn().mockResolvedValue(undefined);

const item: LeaveRequestListItem = {
  id: 'r1',
  number: 'IZN-2026-000012',
  employeeId: 'e1',
  employeeName: 'Ayşe Yılmaz',
  employmentId: 'm1',
  companyId: 'c1',
  companyName: 'Acme',
  leaveTypeCode: 'YILLIK',
  leaveTypeName: 'Yıllık ücretli izin',
  startDate: '2026-11-02',
  endDate: '2026-11-04',
  startPart: 'Full',
  endPart: 'Full',
  startTime: null,
  endTime: null,
  travelDays: 0,
  basis: 'WorkingDays',
  quantity: 3,
  days: 3,
  status: 'Approved',
  origin: 'Self',
  createdAt: '2026-10-20T09:00:00Z',
  submittedAt: '2026-10-20T09:00:00Z',
};

const details: LeaveRequestDetails = {
  request: item,
  leaveTypeId: 't1',
  eventDate: null,
  deductsFrom: 'YILLIK',
  note: 'Aile ziyareti',
  address: null,
  phone: null,
  substituteEmployeeId: null,
  substituteName: null,
  returnComment: null,
  cancellationReason: null,
  cancellationRequestedAt: null,
  originalEndDate: null,
  decidedAt: '2026-10-21T09:00:00Z',
  requiresDocument: false,
  files: [],
  fileExtensions: ['pdf'],
  maxFileBytes: 20971520,
  actions: {
    edit: false,
    submit: false,
    cancel: false,
    requestCancellation: true,
    returnEarly: false,
    manageFiles: false,
  },
};

const balance: LeaveBalanceItem = {
  leaveTypeCode: 'YILLIK',
  leaveTypeName: 'Yıllık ücretli izin',
  balance: 11,
  earned: 14,
  used: 3,
  pending: 0,
  available: 11,
  closedOn: null,
  nextAccrual: null,
  entries: [],
};

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  listMyLeaveRequests: () => Promise.resolve({ items: [item], page: 1, pageSize: 20, totalCount: 1 }),
  useListMyLeaveBalances: () => ({ data: [balance], isLoading: false }),
  useGetMyLeaveRequest: () => ({ data: details }),
  useGetLeaveRequest: () => ({ data: undefined }),
  requestMyLeaveCancellation: (...args: unknown[]) => requestCancellation(...args) as Promise<void>,
  getListMyLeaveRequestsQueryKey: () => ['/api/hr/me/leave-requests'],
  getListMyLeaveBalancesQueryKey: () => ['/api/hr/me/leave-balances'],
  getListLeaveRequestsQueryKey: () => ['/api/hr/leave-requests'],
  getGetMyLeaveRequestQueryKey: (id: string) => [`/api/hr/me/leave-requests/${id}`],
  getGetLeaveRequestQueryKey: (id: string) => [`/api/hr/leave-requests/${id}`],
}));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetApprovalTimeline: () => ({ isLoading: false, data: undefined }),
}));

const texts: Record<string, string> = {
  'web.hr.leave_requests.my_title': 'İzinlerim',
  'web.hr.leave_requests.new': 'Yeni izin talebi',
  'web.hr.leave_requests.request_cancellation': 'İptal talebi',
  'web.hr.leave_requests.send': 'Gönder',
  'web.hr.leave_requests.fields.reason': 'Gerekçe',
  'web.hr.leave_requests.status.Approved': 'Onaylandı',
  'web.hr.leave_requests.length_days': '{quantity} gün',
  'web.hr.leave_balances.available': 'Kullanılabilir',
  'web.platform.fields.required': 'Zorunlu alan',
};

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <MyLeavePage />
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    '/hr/me/leave',
  );

describe('MyLeavePage', () => {
  beforeEach(() => requestCancellation.mockClear());

  it('shows the balances and the requests, and asks for a new one only with the permission', async () => {
    render(['hr.self.leave.read']);

    expect(screen.getByText('Kullanılabilir')).toBeInTheDocument();
    expect(await screen.findByText('IZN-2026-000012')).toBeInTheDocument();
    expect(screen.getByText('3 gün')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni izin talebi/ })).not.toBeInTheDocument();
  });

  it('asks to cancel an approved leave with a reason', async () => {
    render(['hr.self.leave.read', 'hr.self.leave.request']);

    await userEvent.click(await screen.findByText('IZN-2026-000012'));
    await userEvent.click(await screen.findByRole('button', { name: 'İptal talebi' }));
    await screen.findByLabelText(/Gerekçe/);
    // The reason dialog opens over the request's drawer
    const dialog = screen.getAllByRole('dialog').at(-1)!;
    await userEvent.click(within(dialog).getByRole('button', { name: 'Gönder' }));
    expect(await within(dialog).findByText('Zorunlu alan')).toBeInTheDocument();

    await userEvent.type(within(dialog).getByLabelText(/Gerekçe/), 'Seyahat iptal oldu');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Gönder' }));

    await waitFor(() => expect(requestCancellation).toHaveBeenCalledWith('r1', { reason: 'Seyahat iptal oldu' }));
  });
});
