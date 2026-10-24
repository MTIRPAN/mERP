import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { LeaveRequestDetails, LeaveRequestListItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { LeaveRequestsPage } from './LeaveRequestsPage';

const cancel = vi.fn().mockResolvedValue(undefined);

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
  origin: 'OnBehalf',
  createdAt: '2026-10-20T09:00:00Z',
  submittedAt: '2026-10-20T09:00:00Z',
};

const details = {
  request: item,
  leaveTypeId: 't1',
  files: [],
  fileExtensions: ['pdf'],
  requiresDocument: false,
  actions: {
    edit: false,
    submit: false,
    cancel: true,
    requestCancellation: false,
    returnEarly: false,
    manageFiles: true,
  },
} as unknown as LeaveRequestDetails;

vi.mock('@/app/company/companyStore', () => ({ useActiveCompanyId: () => undefined }));

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  listLeaveRequests: () => Promise.resolve({ items: [item], page: 1, pageSize: 20, totalCount: 1 }),
  useGetLeaveRequest: () => ({ data: details }),
  useGetMyLeaveRequest: () => ({ data: undefined }),
  cancelLeaveRequest: (...args: unknown[]) => cancel(...args) as Promise<void>,
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
  'web.hr.leave_requests.title': 'İzin talepleri',
  'web.hr.leave_requests.new_title.onBehalf': 'Personel adına talep',
  'web.hr.leave_requests.new_title.record': 'Geçmiş izin kaydet',
  'web.hr.leave_requests.bulk.title': 'Toplu izin',
  'web.hr.leave_requests.cancel': 'İptal et',
  'web.hr.leave_requests.cancel_title': '{number} numaralı talebi iptal et',
  'web.hr.leave_requests.fields.reason': 'Gerekçe',
  'web.platform.fields.required': 'Zorunlu alan',
};

const render = (permissions: string[], route = '/hr/leave-requests') =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <Routes>
          <Route path="/hr/leave-requests" element={<LeaveRequestsPage />} />
          <Route path="/hr/leave-requests/:id" element={<LeaveRequestsPage />} />
        </Routes>
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('LeaveRequestsPage', () => {
  beforeEach(() => cancel.mockClear());

  it('lists the requests in scope and offers HR only what its permissions allow', async () => {
    render(['hr.leave.requests.read', 'hr.leave.requests.manage']);

    expect(await screen.findByText('IZN-2026-000012')).toBeInTheDocument();
    expect(screen.getByText('Ayşe Yılmaz')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Personel adına talep/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Toplu izin/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Geçmiş izin kaydet/ })).not.toBeInTheDocument();
  });

  it('cancels an approved leave from its address, with the reason it requires', async () => {
    render(['hr.leave.requests.read', 'hr.leave.requests.manage'], '/hr/leave-requests/r1');

    await userEvent.click(await screen.findByRole('button', { name: 'İptal et' }));
    await screen.findByLabelText(/Gerekçe/);
    const dialog = screen.getAllByRole('dialog').at(-1)!;
    await userEvent.type(screen.getByLabelText(/Gerekçe/), 'Proje takvimi değişti');
    await userEvent.click(within(dialog).getByRole('button', { name: 'İptal et' }));

    await waitFor(() => expect(cancel).toHaveBeenCalledWith('r1', { reason: 'Proje takvimi değişti' }));
  });
});
