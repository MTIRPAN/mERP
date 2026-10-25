import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { RequisitionDetails, RequisitionListItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { RequisitionsPage } from './RequisitionsPage';

const cancel = vi.fn().mockResolvedValue(undefined);

const item: RequisitionListItem = {
  id: 'r1',
  number: 'PT-2026-00012',
  companyId: 'c1',
  companyName: 'Acme',
  orgUnitName: 'Satış Direktörlüğü',
  positionId: 'p1',
  positionTitle: 'P-001 · Satış Uzmanı',
  headcount: 2,
  filledCount: 0,
  reason: 'NewHeadcount',
  status: 'Approved',
  desiredStartDate: '2027-01-04',
  hiringManagerName: 'Mehmet Kaya',
  createdAt: '2026-10-20T09:00:00Z',
  submittedAt: '2026-10-20T09:05:00Z',
};

const details = (salaryVisible: boolean): RequisitionDetails => ({
  requisition: item,
  orgUnitId: 'u1',
  newPositionTitle: null,
  newPositionLevel: null,
  replacedEmployeeId: null,
  replacedEmployeeName: null,
  employmentType: 'Indefinite',
  salaryMin: salaryVisible ? 40000 : null,
  salaryMax: salaryVisible ? 55000 : null,
  currency: salaryVisible ? 'TRY' : null,
  salaryVisible,
  justification: 'Yeni bayi kanalı',
  hiringManagerUserId: 'u-manager',
  requesterUserId: 'u-requester',
  requesterName: 'Ayşe Yılmaz',
  returnComment: null,
  cancellationReason: null,
  decidedAt: '2026-10-22T10:00:00Z',
  canEdit: false,
  canCancel: salaryVisible,
  canCreateOpening: salaryVisible,
  version: 3,
});

let current = details(true);

vi.mock('@/app/company/companyStore', () => ({ useActiveCompanyId: () => undefined }));

vi.mock('@/app/me/meContext', () => ({ useMe: () => ({ companies: [{ id: 'c1', name: 'Acme' }] }) }));

vi.mock('@/shared/api/generated/endpoints/recruitment/recruitment', () => ({
  listRequisitions: () => Promise.resolve({ items: [item], page: 1, pageSize: 20, totalCount: 1 }),
  useGetRequisition: () => ({ data: current }),
  cancelRequisition: (...args: unknown[]) => cancel(...args) as Promise<void>,
  submitRequisition: vi.fn(),
  getListRequisitionsQueryKey: () => ['/api/recruitment/requisitions'],
  getListRequisitionPositionsQueryKey: () => ['/api/recruitment/requisitions/positions'],
  getGetRequisitionQueryKey: (id: string) => [`/api/recruitment/requisitions/${id}`],
}));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetApprovalTimeline: () => ({ isLoading: false, data: undefined }),
}));

const texts: Record<string, string> = {
  'web.recruitment.requisitions.title': 'Personel talepleri',
  'web.recruitment.requisitions.new_title': 'Yeni personel talebi',
  'web.recruitment.requisitions.cancel': 'Talebi iptal et',
  'web.recruitment.requisitions.ready_for_opening': 'Bu talep için ilan açılabilir.',
  'web.recruitment.requisitions.fields.salary': 'Brüt maaş aralığı',
  'web.recruitment.requisitions.fields.cancellation_reason': 'İptal nedeni',
  'web.recruitment.requisitions.filled_of': '{filled} / {headcount}',
  'web.recruitment.requisitions.status.Approved': 'Onaylandı',
};

const render = (permissions: string[], route = '/recruitment/requisitions') =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <Routes>
          <Route path="/recruitment/requisitions" element={<RequisitionsPage />} />
          <Route path="/recruitment/requisitions/:id" element={<RequisitionsPage />} />
        </Routes>
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('RequisitionsPage', () => {
  beforeEach(() => {
    cancel.mockClear();
    current = details(true);
  });

  it('lists the requisitions and offers a new one only to those who manage them', async () => {
    render(['recruitment.requisitions.read']);

    expect(await screen.findByText('PT-2026-00012')).toBeInTheDocument();
    expect(screen.getByText('P-001 · Satış Uzmanı')).toBeInTheDocument();
    expect(screen.getByText('0 / 2')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni personel talebi/ })).not.toBeInTheDocument();
  });

  it('shows an approved requisition with its salary, and cancels it with a reason', async () => {
    render(['recruitment.requisitions.read', 'recruitment.requisitions.manage'], '/recruitment/requisitions/r1');

    expect(await screen.findByText('Bu talep için ilan açılabilir.')).toBeInTheDocument();
    expect(screen.getByText('Brüt maaş aralığı')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Talebi iptal et' }));
    await userEvent.type(await screen.findByLabelText(/İptal nedeni/), 'Bütçe donduruldu');
    const dialog = screen.getAllByRole('dialog').at(-1)!;
    await userEvent.click(within(dialog).getByRole('button', { name: 'Talebi iptal et' }));

    await waitFor(() => expect(cancel).toHaveBeenCalledWith('r1', { reason: 'Bütçe donduruldu' }));
  });

  it('hides the salary from a reader', async () => {
    current = details(false);
    render(['recruitment.requisitions.read'], '/recruitment/requisitions/r1');

    expect(await screen.findAllByText('Onaylandı')).not.toHaveLength(0);
    expect(screen.queryByText('Brüt maaş aralığı')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Talebi iptal et' })).not.toBeInTheDocument();
  });
});
