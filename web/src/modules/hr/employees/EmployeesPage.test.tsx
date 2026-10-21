import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { ListEmployeesParams, MeResponse, PagedResultOfEmployeeListItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { EmployeesPage } from './EmployeesPage';

const listEmployees = vi.fn<(params: ListEmployeesParams) => Promise<PagedResultOfEmployeeListItem>>();

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  listEmployees: (params: ListEmployeesParams) => listEmployees(params),
  exportEmployees: vi.fn(),
  getListEmployeesQueryKey: () => ['/api/hr/employees'],
  getGetEmployeePhotoQueryKey: (id: string) => [`/api/hr/employees/${id}/photo`],
  useGetEmployeePhoto: () => ({ data: undefined }),
}));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetCustomFieldForm: () => ({ data: [] }),
}));

const texts: Record<string, string> = {
  'web.hr.employees.title': 'Personel',
  'web.hr.employees.hire': 'İşe giriş',
  'web.hr.employees.filters.active': 'Çalışanlar',
  'web.hr.employees.filters.upcoming': 'Başlayacaklar',
  'web.hr.employees.filters.terminated': 'Ayrılanlar',
  'web.hr.employees.filters.all': 'Tümü',
  'web.hr.employment_status.Active': 'Aktif',
};

const page: PagedResultOfEmployeeListItem = {
  items: [
    {
      id: 'e1',
      employmentId: 'm1',
      firstName: 'Ayşe',
      lastName: 'Yılmaz',
      fullName: 'Ayşe Yılmaz',
      tckn: '100******46',
      companyId: 'c1',
      companyName: 'Acme',
      registryNumber: '000042',
      hireDate: '2025-12-01',
      terminationDate: null,
      status: 'Active',
      isPrimary: true,
      positionId: 'p1',
      positionCode: 'SAT-01',
      jobId: 'j1',
      jobName: 'Satış Temsilcisi',
      orgUnitId: 'u1',
      orgUnitName: 'Satış',
      branchId: null,
      branchName: null,
      photoFileId: null,
      customFields: {},
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
};

const render = (permissions: string[], route = '/hr/employees') =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MeContext.Provider value={{ companies: [] } as unknown as MeResponse}>
        <PermissionProvider permissions={permissions}>
          <EmployeesPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('EmployeesPage', () => {
  beforeEach(() => {
    listEmployees.mockReset().mockResolvedValue(page);
  });

  it('lists who works today and links to the card', async () => {
    render(['hr.employees.read']);

    expect(await screen.findByRole('link', { name: 'Ayşe Yılmaz' })).toHaveAttribute('href', '/hr/employees/e1');
    expect(screen.getByText('000042')).toBeInTheDocument();
    expect(screen.getByText('Aktif')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /İşe giriş/ })).not.toBeInTheDocument();
    expect(listEmployees).toHaveBeenCalledWith(expect.objectContaining({ page: 1, status: undefined }));
  });

  it('asks for the leavers, and offers the hire to those who may hire', async () => {
    render(['hr.employees.read', 'hr.employees.create']);
    await screen.findByText('000042');

    await userEvent.click(screen.getByText('Ayrılanlar'));

    await waitFor(() =>
      expect(listEmployees).toHaveBeenLastCalledWith(expect.objectContaining({ status: ['Terminated'] })),
    );
    expect(screen.getByRole('button', { name: /İşe giriş/ })).toBeInTheDocument();
  });

  it('searches every status for the TCKN of a refused hire', async () => {
    render(['hr.employees.read'], '/hr/employees?search=10000000146');

    await waitFor(() =>
      expect(listEmployees).toHaveBeenCalledWith(
        expect.objectContaining({ search: '10000000146', status: ['Upcoming', 'Active', 'Terminated'] }),
      ),
    );
  });
});
