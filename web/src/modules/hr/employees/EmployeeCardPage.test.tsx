import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { EmployeeDetails, ProfileChangeItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { EmployeeCardPage } from './EmployeeCardPage';

const employee = vi.fn<() => { data?: EmployeeDetails; isError: boolean; error: unknown }>();
const profileChanges = vi.fn<() => { data?: ProfileChangeItem[]; isLoading: boolean }>();

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useGetEmployee: () => employee(),
  useGetEmployeePhoto: () => ({ data: undefined }),
  getGetEmployeePhotoQueryKey: (id: string) => [`/api/hr/employees/${id}/photo`],
  getGetEmployeeQueryKey: (id: string) => [`/api/hr/employees/${id}`],
  getListEmployeesQueryKey: () => ['/api/hr/employees'],
  useSetEmployeePhoto: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useRemoveEmployeePhoto: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUnlinkEmployeeUser: () => ({ mutateAsync: vi.fn() }),
  useListEmployeeProfileChanges: () => profileChanges(),
}));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetCustomFieldForm: () => ({ data: [] }),
  useGetApprovalTimeline: () => ({ isLoading: false, data: undefined }),
}));

vi.mock('@/modules/platform/audit/AuditHistoryButton', () => ({ AuditHistoryButton: () => null }));
vi.mock('./documents/DocumentsTab', () => ({ DocumentsTab: () => <div>belge listesi</div> }));

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
  'web.common.edit': 'Düzenle',
  'web.hr.employees.tabs.documents': 'Belgeler',
  'web.hr.employees.lifecycle.rehire': 'Tekrar işe giriş',
  'web.hr.employees.lifecycle.change_assignment': 'Görev değişikliği',
  'web.hr.employees.lifecycle.group_transfer': 'Grup içi nakil',
  'web.hr.employees.lifecycle.terminate': 'İşten çıkış',
  'web.hr.employees.tabs.requests': 'Değişiklik talepleri',
  'web.hr.employees.user.title': 'Sistem kullanıcısı',
  'web.hr.employees.user.active': 'Kullanıcı aktif',
  'web.hr.employees.user.unlink': 'Bağı kaldır',
  'web.hr.employees.user.create': 'Kullanıcı oluştur',
  'web.hr.employees.user.failed': 'İstek tamamlanamadı',
  'web.hr.profile_changes.kind.Iban': 'IBAN',
  'web.hr.profile_changes.status.Pending': 'Onay bekliyor',
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
  userLinkState: 'None',
  userLinkError: null,
  user: null,
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

const render = (permissions: string[] = ['hr.employees.read'], route = '/hr/employees/e1') =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <Routes>
          <Route path="/hr/employees/:id" element={<EmployeeCardPage />} />
        </Routes>
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('EmployeeCardPage', () => {
  beforeEach(() => {
    employee.mockReset().mockReturnValue({ data: details(), isError: false, error: null });
    profileChanges.mockReset().mockReturnValue({ data: [], isLoading: false });
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

  it('offers the edits and the life cycle actions the user may use', async () => {
    render([
      'hr.employees.read',
      'hr.employees.update',
      'hr.employees.create',
      'hr.employees.terminate',
      'hr.employees.documents.read',
    ]);

    expect(screen.getByRole('button', { name: /Düzenle/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Tekrar işe giriş/ })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'İstihdam ve görev geçmişi' }));
    expect(await screen.findByRole('button', { name: /Görev değişikliği/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Grup içi nakil/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /İşten çıkış/ })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Belgeler' }));
    expect(await screen.findByText('belge listesi')).toBeInTheDocument();
  });

  it('offers nothing to change to a reader', async () => {
    render();

    expect(screen.queryByRole('button', { name: /Düzenle/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Tekrar işe giriş/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Belgeler' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: 'İstihdam ve görev geçmişi' }));
    expect(screen.queryByRole('button', { name: /İşten çıkış/ })).not.toBeInTheDocument();
  });

  it('shows the linked system user, and to those who manage users the way to unlink it', () => {
    employee.mockReturnValue({
      data: details({
        userId: 'u1',
        userLinkState: 'Linked',
        user: { id: 'u1', userName: 'ayse.yilmaz', email: 'ayse@acme.test', isActive: true },
      }),
      isError: false,
      error: null,
    });

    render(['hr.employees.read', 'hr.employees.user']);

    expect(screen.getByText('Sistem kullanıcısı')).toBeInTheDocument();
    expect(screen.getByText('ayse.yilmaz')).toBeInTheDocument();
    expect(screen.getByText('Kullanıcı aktif')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Bağı kaldır/ })).toBeInTheDocument();
  });

  it('shows why a user request failed, and hides the section from readers when nobody is linked', () => {
    employee.mockReturnValue({
      data: details({ userLinkState: 'Failed', userLinkError: 'Bu e-posta adresiyle kayıtlı bir kullanıcı var.' }),
      isError: false,
      error: null,
    });

    const { unmount } = render(['hr.employees.read', 'hr.employees.user']);
    expect(screen.getByText('İstek tamamlanamadı')).toBeInTheDocument();
    expect(screen.getByText('Bu e-posta adresiyle kayıtlı bir kullanıcı var.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Kullanıcı oluştur/ })).toBeInTheDocument();
    unmount();

    render();
    expect(screen.queryByText('Sistem kullanıcısı')).not.toBeInTheDocument();
  });

  it('opens the change requests from a link with the tab', async () => {
    profileChanges.mockReturnValue({
      data: [
        {
          id: 'r1',
          number: 'PDT-2026-00012',
          kind: 'Iban',
          status: 'Pending',
          proposed: null,
          proposedIban: 'TR** **** … 1326',
          note: null,
          returnComment: null,
          createdAt: '2026-10-22T09:00:00Z',
          closedAt: null,
        },
      ],
      isLoading: false,
    });

    render(['hr.employees.read'], '/hr/employees/e1?tab=requests');

    expect(await screen.findByText('PDT-2026-00012')).toBeInTheDocument();
    expect(screen.getByText('Onay bekliyor')).toBeInTheDocument();
  });
});
