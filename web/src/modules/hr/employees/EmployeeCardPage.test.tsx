import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { EmployeeDetails } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { EmployeeCardPage } from './EmployeeCardPage';

const employee = vi.fn<() => { data?: EmployeeDetails; isError: boolean; error: unknown }>();

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useGetEmployee: () => employee(),
  useGetEmployeePhoto: () => ({ data: undefined }),
  getGetEmployeePhotoQueryKey: (id: string) => [`/api/hr/employees/${id}/photo`],
  getGetEmployeeQueryKey: (id: string) => [`/api/hr/employees/${id}`],
  getListEmployeesQueryKey: () => ['/api/hr/employees'],
  useSetEmployeePhoto: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useRemoveEmployeePhoto: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetCustomFieldForm: () => ({ data: [] }),
}));

vi.mock('@/modules/platform/audit/AuditHistoryButton', () => ({ AuditHistoryButton: () => null }));

const texts: Record<string, string> = {
  'web.hr.employees.title': 'Personel',
  'web.hr.employees.fields.tckn': 'T.C. kimlik no',
  'web.hr.employees.sensitive.hidden': 'Bu bilgileri görme yetkiniz yok.',
  'web.hr.employees.tabs.general': 'Genel',
  'web.hr.employees.tabs.employment': 'İstihdam ve görev geçmişi',
  'web.hr.employees.assignment.current': 'Bugünkü görev',
  'web.hr.assignment_reason.Hire': 'Giriş',
  'web.hr.assignment_reason.Promotion': 'Terfi',
  'web.hr.employment_status.Active': 'Aktif',
};

const details = (overrides: Partial<EmployeeDetails> = {}): EmployeeDetails => ({
  id: 'e1',
  tckn: '100******46',
  firstName: 'Ayşe',
  lastName: 'Yılmaz',
  fullName: 'Ayşe Yılmaz',
  birthDate: '1990-05-01',
  birthPlace: 'İstanbul',
  gender: 'Female',
  nationality: 'TR',
  maritalStatus: null,
  militaryStatus: null,
  iban: 'TR** **** … 1326',
  sensitiveVisible: false,
  userId: null,
  photoFileId: null,
  emails: [],
  phones: [],
  addresses: [],
  emergencyContacts: [],
  familyMembers: [],
  educations: [],
  certificates: [],
  languages: [],
  customFields: {},
  sensitive: { hidden: true, disabilityDegree: null, bloodType: null, healthNotes: null, criminalRecordDate: null },
  employments: [
    {
      id: 'm1',
      companyId: 'c1',
      companyName: 'Acme',
      registryNumber: '000042',
      hireDate: '2025-12-01',
      seniorityDate: '2025-12-01',
      seniorityReason: null,
      type: 'Indefinite',
      contractEndDate: null,
      weeklyHours: null,
      probationEndDate: null,
      workScheduleId: null,
      leavePolicyId: null,
      isPrimary: true,
      status: 'Active',
      terminationDate: null,
      sgkExitCode: null,
      sgkExitCodeName: null,
      terminationReason: null,
      rehireable: null,
      terminationNote: null,
      assignments: [
        {
          id: 'a2',
          positionId: 'p2',
          positionCode: 'SAT-02',
          orgUnitId: 'u1',
          orgUnitName: 'Satış',
          jobId: 'j2',
          jobName: 'Satış Müdürü',
          branchId: null,
          branchName: null,
          managerPositionId: null,
          managerPositionCode: null,
          level: null,
          costCenterCode: null,
          reason: 'Promotion',
          validFrom: '2026-02-01',
          validTo: null,
          isCurrent: false,
        },
        {
          id: 'a1',
          positionId: 'p1',
          positionCode: 'SAT-01',
          orgUnitId: 'u1',
          orgUnitName: 'Satış',
          jobId: 'j1',
          jobName: 'Satış Temsilcisi',
          branchId: null,
          branchName: null,
          managerPositionId: null,
          managerPositionCode: null,
          level: null,
          costCenterCode: null,
          reason: 'Hire',
          validFrom: '2025-12-01',
          validTo: '2026-01-31',
          isCurrent: true,
        },
      ],
    },
  ],
  version: 3,
  createdAt: '2025-12-01T09:00:00Z',
  modifiedAt: null,
  ...overrides,
});

const render = (permissions: string[] = ['hr.employees.read']) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <Routes>
          <Route path="/hr/employees/:id" element={<EmployeeCardPage />} />
        </Routes>
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    '/hr/employees/e1',
  );

describe('EmployeeCardPage', () => {
  beforeEach(() => {
    employee.mockReset().mockReturnValue({ data: details(), isError: false, error: null });
  });

  it('shows masked values with a lock and hides the special category data', () => {
    render();

    expect(screen.getAllByText('Ayşe Yılmaz').length).toBeGreaterThan(0);
    expect(screen.getByText('100******46')).toBeInTheDocument();
    expect(screen.getAllByRole('img', { name: 'lock' }).length).toBeGreaterThan(0);
    expect(screen.getByText('Bu bilgileri görme yetkiniz yok.')).toBeInTheDocument();
  });

  it('shows the plain values to those who may see them', () => {
    employee.mockReturnValue({
      data: details({
        tckn: '10000000146',
        iban: 'TR330006100519786457841326',
        sensitiveVisible: true,
        sensitive: {
          hidden: false,
          disabilityDegree: 40,
          bloodType: 'A Rh+',
          healthNotes: null,
          criminalRecordDate: null,
        },
      }),
      isError: false,
      error: null,
    });

    render();

    expect(screen.getByText('10000000146')).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: 'lock' })).not.toBeInTheDocument();
    expect(screen.getByText('A Rh+')).toBeInTheDocument();
  });

  it('lists the job history with the current assignment marked', async () => {
    render();

    await userEvent.click(screen.getByRole('tab', { name: 'İstihdam ve görev geçmişi' }));

    expect(await screen.findByText('Satış Müdürü · SAT-02')).toBeInTheDocument();
    expect(screen.getByText('Terfi')).toBeInTheDocument();
    expect(screen.getByText('Bugünkü görev')).toBeInTheDocument();
  });
});
