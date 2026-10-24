import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { LeaveBalanceReportRow, LongTimeNoLeaveRow } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { LeaveReportsPage } from './LeaveReportsPage';

const balances = vi.fn();

const balance: LeaveBalanceReportRow = {
  employeeId: 'e1',
  employmentId: 'm1',
  employeeName: 'Ali Demir',
  registryNumber: '000042',
  companyId: 'c1',
  companyName: 'Acme',
  leaveTypeCode: 'YILLIK',
  leaveTypeName: 'Yıllık ücretli izin',
  earned: 14,
  used: 3.5,
  balance: 10.5,
};

const idle: LongTimeNoLeaveRow = {
  employeeId: 'e2',
  employmentId: 'm2',
  employeeName: 'Zeynep Kaya',
  registryNumber: '000043',
  companyId: 'c1',
  companyName: 'Acme',
  orgUnitName: 'Satış',
  hireDate: '2024-03-01',
  lastLeaveEnd: null,
  daysSince: null,
  balance: 20,
};

vi.mock('@/app/me/meContext', () => ({ useMe: () => ({ companies: [{ id: 'c1', name: 'Acme' }] }) }));

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  getLeaveBalanceReport: (params: unknown) => {
    balances(params);
    return Promise.resolve({ items: [balance], page: 1, pageSize: 20, totalCount: 1 });
  },
  getLongTimeNoLeaveReport: () => Promise.resolve({ items: [idle], page: 1, pageSize: 20, totalCount: 1 }),
  getGetLeaveBalanceReportQueryKey: () => ['/api/hr/leave/reports/balances'],
  getGetLeaveUsageReportQueryKey: () => ['/api/hr/leave/reports/usage'],
  getGetUpcomingLeavesReportQueryKey: () => ['/api/hr/leave/reports/upcoming'],
  getGetLongTimeNoLeaveReportQueryKey: () => ['/api/hr/leave/reports/long-time-no-leave'],
}));

const texts: Record<string, string> = {
  'web.hr.leave_reports.balances.title': 'İzin bakiyeleri',
  'web.hr.leave_reports.long_time_no_leave.title': 'Uzun süredir izin kullanmayanlar',
  'web.hr.leave_reports.never': 'Hiç kullanmadı',
};

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={['hr.leave.reports.read', 'hr.employees.read']}>
        <LeaveReportsPage />
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    '/hr/leave-reports',
  );

describe('LeaveReportsPage', () => {
  it('shows the balances as of today, linking the people to their cards', async () => {
    render();

    expect(await screen.findByRole('link', { name: 'Ali Demir' })).toHaveAttribute('href', '/hr/employees/e1');
    expect(screen.getByText('10,5')).toBeInTheDocument();
    expect(balances).toHaveBeenCalledWith(
      expect.objectContaining({ asOf: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/) as unknown }),
    );
  });

  it('lists the people who never took leave in the other report', async () => {
    render();

    await userEvent.click(screen.getByRole('tab', { name: 'Uzun süredir izin kullanmayanlar' }));

    expect(await screen.findByText('Zeynep Kaya')).toBeInTheDocument();
    expect(screen.getByText('Hiç kullanmadı')).toBeInTheDocument();
  });
});
