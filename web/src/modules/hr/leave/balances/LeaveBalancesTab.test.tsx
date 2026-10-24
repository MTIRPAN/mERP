import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { EmploymentItem, LeaveBalanceItem } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { LeaveBalancesTab } from './LeaveBalancesTab';

const adjust = vi.fn().mockResolvedValue(undefined);
const saveFile = vi.fn().mockResolvedValue(undefined);
const record = vi.fn().mockResolvedValue(new Blob(['%PDF']));

const balances: LeaveBalanceItem[] = [
  {
    leaveTypeCode: 'YILLIK',
    leaveTypeName: 'Yıllık ücretli izin',
    balance: 16.5,
    earned: 34,
    used: 17.5,
    pending: 0,
    available: 16.5,
    closedOn: null,
    nextAccrual: { date: '2027-03-16', days: 20 },
    entries: [
      {
        date: '2026-05-04',
        quantity: -3,
        source: 'Adjustment',
        periodKey: null,
        note: 'Eski sistemden fark',
        recordedAt: '2026-05-04T08:00:00Z',
      },
      {
        date: '2026-03-16',
        quantity: 20,
        source: 'Accrual',
        periodKey: '2026-03-16',
        note: null,
        recordedAt: '2026-03-17T01:00:00Z',
      },
    ],
  },
];

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useListLeaveBalances: () => ({ data: balances, isLoading: false }),
  useAdjustLeaveBalance: () => ({ mutateAsync: adjust }),
  getLeaveRecordDocument: (...args: unknown[]) => record(...args) as Promise<Blob>,
  getListLeaveBalancesQueryKey: (employeeId: string, employmentId: string) => [
    `/api/hr/employees/${employeeId}/employments/${employmentId}/leave-balances`,
  ],
}));

vi.mock('@/shared/api/download', () => ({
  saveFile: (...args: unknown[]) => saveFile(...args) as Promise<void>,
}));

const texts: Record<string, string> = {
  'web.hr.leave_balances.record': 'Kayıt belgesi',
  'web.hr.leave_balances.record_file': 'yillik-izin-kayit-belgesi-{registry}.pdf',
  'web.hr.leave_balances.available': 'Kullanılabilir',
  'web.hr.leave_balances.adjust': 'Bakiye düzelt',
  'web.hr.leave_balances.next_accrual': 'Sonraki hak ediş: {date}, {days}',
  'web.hr.leave_balances.days_value': '{days} gün',
  'web.hr.leave_balances.sources.Accrual': 'Hak ediş',
  'web.hr.leave_balances.sources.Adjustment': 'Düzeltme',
  'web.hr.leave_balances.quantity': 'Gün',
  'web.hr.leave_balances.note': 'Açıklama',
  'web.common.save': 'Kaydet',
};

const employments = [
  { id: 'm1', companyName: 'Acme', registryNumber: '000042', isPrimary: true, status: 'Active' },
] as unknown as EmploymentItem[];

const render = (canManage: boolean) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <LeaveBalancesTab employeeId="e1" employments={employments} canManage={canManage} />
    </QueryClientProvider>,
    texts,
  );

describe('LeaveBalancesTab', () => {
  beforeEach(() => adjust.mockClear());

  it('shows the balance, the next entitlement and the movements, without corrections for a reader', () => {
    render(false);

    expect(screen.getByText('Yıllık ücretli izin')).toBeInTheDocument();
    expect(screen.getByText('Kullanılabilir')).toBeInTheDocument();
    expect(screen.getByText(/^Sonraki hak ediş: .*, 20 gün$/)).toBeInTheDocument();
    expect(screen.getByText('Hak ediş')).toBeInTheDocument();
    expect(screen.getByText('Eski sistemden fark')).toBeInTheDocument();
    expect(screen.getByText('2026-03-16')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Bakiye düzelt/ })).not.toBeInTheDocument();
  });

  it('prints the annual leave record of the employment', async () => {
    render(false);

    await userEvent.click(screen.getByRole('button', { name: /Kayıt belgesi/ }));

    await waitFor(() =>
      expect(saveFile).toHaveBeenCalledWith(expect.any(Promise), 'yillik-izin-kayit-belgesi-000042.pdf'),
    );
    expect(record).toHaveBeenCalledWith('e1', 'm1');
  });

  it('corrects a balance with a reason', async () => {
    render(true);

    await userEvent.click(screen.getByRole('button', { name: /Bakiye düzelt/ }));
    await userEvent.type(await screen.findByLabelText(/^Gün/), '-2');
    await userEvent.type(screen.getByLabelText(/^Açıklama/), 'Mükerrer giriş');
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }));

    await waitFor(() =>
      expect(adjust).toHaveBeenCalledWith({
        employeeId: 'e1',
        employmentId: 'm1',
        data: expect.objectContaining({ leaveTypeCode: 'YILLIK', quantity: -2, note: 'Mükerrer giriş' }) as unknown,
      }),
    );
  });
});
