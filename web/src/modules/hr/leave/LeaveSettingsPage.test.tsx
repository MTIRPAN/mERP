import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type {
  AnnualLeaveRules,
  LeavePolicyItem,
  LeavePolicyResolution,
  LeavePolicyRules,
  LeaveTypeItem,
  MeResponse,
} from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { LeaveSettingsPage } from './LeaveSettingsPage';

const replaceRules = vi.fn().mockResolvedValue(undefined);
const createPolicy = vi.fn().mockResolvedValue({ id: 'p9' });
const checkRules = vi.fn();
const setOverride = vi.fn().mockResolvedValue(undefined);

const rules: AnnualLeaveRules = {
  minServiceYearsForEntitlement: 1,
  tiers: [
    { minServiceYears: 1, days: 14 },
    { minServiceYears: 5, days: 20 },
  ],
  ageMinimums: [],
  extraDayRules: [],
  accrualMethod: 'AnniversaryLumpSum',
  tenDayBlockRule: 'Warn',
  allowTravelDays: true,
  maxTravelDays: 4,
  carryOverLimitDays: null,
  useGroupSeniority: false,
  unpaidLeaveShiftsAccrualDate: false,
  advanceLimitDays: 0,
  allowBeforeFirstEntitlement: false,
  allowHalfDay: true,
  allowHourly: false,
  rounding: 'None',
  transferBalanceOnGroupTransfer: true,
  minNoticeDays: null,
  maxDaysPerRequest: null,
  allowPastDatesForHr: true,
};

const types: LeaveTypeItem[] = [
  {
    id: 't1',
    code: 'BABALIK',
    name: 'Babalık izni',
    names: { 'tr-TR': 'Babalık izni' },
    companyId: null,
    category: 'Excuse',
    payer: 'Employer',
    deductsFrom: null,
    unit: 'Day',
    durationBasis: 'CalendarDays',
    requiresDocument: true,
    requiresApproval: true,
    genderRestriction: 'Male',
    allowPastDates: true,
    legalBasis: '4857 Ek md. 2',
    isActive: true,
    versions: [
      { validFrom: '0001-01-01', fixedDays: 5 },
      { validFrom: '2026-05-01', fixedDays: 3 },
    ],
    warnings: [
      {
        code: 'hr.leave.legal.type_fixed_days',
        message: 'Süre 3 gün; kanuni alt sınır 10 gün.',
        validFrom: '2026-05-01',
      },
    ],
    version: 4,
  },
];

const policies: LeavePolicyItem[] = [
  {
    id: 'p1',
    code: 'KANUNI',
    name: 'Kanuni yıllık izin',
    names: { 'tr-TR': 'Kanuni yıllık izin' },
    companyId: null,
    isDefault: true,
    isActive: true,
    versions: [{ validFrom: '0001-01-01', rules }],
    warnings: [],
    version: 1,
  },
  {
    id: 'p2',
    code: 'COMERT',
    name: 'Cömert',
    names: { 'tr-TR': 'Cömert' },
    companyId: 'c1',
    isDefault: false,
    isActive: true,
    versions: [{ validFrom: '0001-01-01', rules }],
    warnings: [],
    version: 1,
  },
];

const ruleSet: LeavePolicyRules = {
  companyId: 'c1',
  version: 7,
  rules: [
    {
      name: 'Stajyerler',
      policyId: 'p1',
      policyName: 'Kanuni yıllık izin',
      criteria: [{ kind: 'EmploymentType', values: ['Intern'] }],
    },
    { name: 'Herkes', policyId: 'p2', policyName: 'Cömert', criteria: [] },
  ],
};

const resolution: LeavePolicyResolution = {
  employmentId: 'm1',
  date: '2030-01-05',
  source: 'Rule',
  policyId: 'p2',
  policyCode: 'COMERT',
  policyName: 'Cömert',
  rulePosition: 2,
  ruleName: 'Herkes',
  versionValidFrom: null,
  completedYears: 4,
  age: 35,
  entitlementDays: 18,
};

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useListLeaveTypes: () => ({ data: types, isLoading: false }),
  useCreateLeaveType: () => ({ mutateAsync: vi.fn() }),
  useUpdateLeaveType: () => ({ mutateAsync: vi.fn() }),
  getListLeaveTypesQueryKey: () => ['/api/hr/leave/types'],
  useListLeavePolicies: () => ({ data: policies, isLoading: false, isFetching: false }),
  useCreateLeavePolicy: () => ({ mutateAsync: createPolicy }),
  useUpdateLeavePolicy: () => ({ mutateAsync: vi.fn() }),
  getListLeavePoliciesQueryKey: () => ['/api/hr/leave/policies'],
  checkLeavePolicyRules: (...args: unknown[]) => checkRules(...args) as Promise<unknown>,
  useGetLeavePolicyRules: () => ({ data: ruleSet, isLoading: false }),
  useReplaceLeavePolicyRules: () => ({ mutateAsync: replaceRules }),
  getGetLeavePolicyRulesQueryKey: (id: string) => [`/api/hr/leave/policy-rules/${id}`],
  useListEmployees: () => ({
    isFetching: false,
    data: {
      items: [
        {
          id: 'e1',
          employmentId: 'm1',
          fullName: 'Ayşe Yılmaz',
          companyId: 'c1',
          companyName: 'Acme',
          registryNumber: '000042',
        },
      ],
    },
  }),
  useResolveLeavePolicy: (params: { employmentId: string }) => ({
    data: params.employmentId ? resolution : undefined,
    refetch: vi.fn(),
  }),
  getResolveLeavePolicyQueryKey: () => ['/api/hr/leave/policy-resolution'],
  useGetEmployee: () => ({ data: { version: 3 } }),
  getGetEmployeeQueryKey: (id: string) => [`/api/hr/employees/${id}`],
  useSetEmploymentLeavePolicy: () => ({ mutateAsync: setOverride, isPending: false }),
  useGetOrgUnitTree: () => ({ data: { units: [] }, isLoading: false }),
}));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useListBranches: () => ({ data: { items: [] }, isFetching: false }),
  useGetCustomFieldForm: () => ({ data: [], isFetching: false }),
}));

vi.mock('@/modules/platform/audit/AuditHistoryButton', () => ({ AuditHistoryButton: () => null }));

const texts: Record<string, string> = {
  'web.hr.leave.title': 'İzin ayarları',
  'web.hr.leave.tabs.types': 'İzin türleri',
  'web.hr.leave.tabs.policies': 'Politikalar',
  'web.hr.leave.tabs.rules': 'Atama kuralları',
  'web.hr.leave.tabs.check': 'Hangi politika?',
  'web.hr.leave.types.new': 'Yeni izin türü',
  'web.hr.leave.policies.new': 'Yeni politika',
  'web.hr.leave.legal.title': 'Kanuni alt sınırın altında kalan ayarlar',
  'web.hr.leave.versions.from_beginning': 'Başlangıçtan beri',
  'web.hr.leave.versions.from_date': '{date} itibarıyla',
  'web.hr.leave.rules_tab.down': 'Aşağı taşı',
  'web.hr.leave.rules_tab.position': '{position}. kural',
  'web.hr.leave.check.employee': 'Personel',
  'web.hr.leave.check.entitlement': 'Yıllık izin hakkı',
  'web.hr.leave.check.rule': '{position}. kural: {name}',
  'web.hr.leave.check.override': 'Elle politika',
  'web.hr.leave.check.override_save': 'Elle ver',
  'web.hr.leave.source.Rule': 'Atama kuralı',
  'web.hr.leave.fields.name': 'Ad',
  'web.platform.fields.code': 'Kod',
  'web.common.save': 'Kaydet',
};

const me = {
  companies: [{ id: 'c1', code: 'ACME', name: 'Acme' }],
  cultures: [{ code: 'tr-TR', name: 'Türkçe' }],
} as unknown as MeResponse;

const render = (
  route: string,
  permissions = ['hr.leave.settings.read', 'hr.leave.settings.manage', 'hr.employees.read'],
) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <LeaveSettingsPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('LeaveSettingsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    checkRules.mockResolvedValue([]);
  });

  it('lists the leave types with their versions and legal warnings, without changes for a reader', () => {
    render('/hr/leave-settings', ['hr.leave.settings.read']);

    expect(screen.getByText('Babalık izni')).toBeInTheDocument();
    expect(screen.getByText('Başlangıçtan beri')).toBeInTheDocument();
    expect(screen.getByLabelText('Kanuni alt sınırın altında kalan ayarlar')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni izin türü/ })).not.toBeInTheDocument();
  });

  it('starts a new policy from the default and warns while the rules are below the law', async () => {
    checkRules.mockResolvedValue([
      { code: 'hr.leave.legal.tier', message: '1 yıllık kıdemde 12 gün veriliyor.', validFrom: null },
    ]);
    render('/hr/leave-settings?tab=policies');

    await userEvent.click(screen.getByRole('button', { name: /Yeni politika/ }));

    await waitFor(() => expect(checkRules).toHaveBeenCalled(), { timeout: 3000 });
    expect(await screen.findByText('1 yıllık kıdemde 12 gün veriliyor.')).toBeInTheDocument();
    expect(checkRules).toHaveBeenCalledWith({ rules: expect.objectContaining({ tiers: rules.tiers }) as unknown });
    await userEvent.type(screen.getByLabelText(/^Kod/), 'yeni');
    await userEvent.type(screen.getByLabelText(/^Ad/), 'Yeni politika');
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }));

    await waitFor(() =>
      expect(createPolicy).toHaveBeenCalledWith({
        data: expect.objectContaining({
          code: 'YENI',
          companyId: null,
          versions: [
            {
              validFrom: '0001-01-01',
              rules: expect.objectContaining({ minServiceYearsForEntitlement: 1 }) as unknown,
            },
          ],
        }) as unknown,
      }),
    );
  });

  it('reorders the assignment rules of a company and saves them with the version', async () => {
    render('/hr/leave-settings?tab=rules');

    const first = await screen.findByText('1. kural');
    await userEvent.click(within(first.closest<HTMLElement>('.ant-card')!).getByRole('button', { name: 'Aşağı taşı' }));
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }));

    await waitFor(() =>
      expect(replaceRules).toHaveBeenCalledWith({
        companyId: 'c1',
        data: {
          version: 7,
          rules: [
            { name: 'Herkes', policyId: 'p2', criteria: [] },
            { name: 'Stajyerler', policyId: 'p1', criteria: [{ kind: 'EmploymentType', values: ['Intern'] }] },
          ],
        },
      }),
    );
  });

  it('tells which policy applies to an employee, and why', async () => {
    render('/hr/leave-settings?tab=check');

    await userEvent.click(screen.getByLabelText('Personel'));
    await userEvent.click(await screen.findByText('Ayşe Yılmaz · Acme · 000042'));

    expect(await screen.findByText('Cömert (COMERT)')).toBeInTheDocument();
    expect(screen.getByText('Atama kuralı')).toBeInTheDocument();
    expect(screen.getByText('2. kural: Herkes')).toBeInTheDocument();
    expect(screen.getByText('18')).toBeInTheDocument();
  });
});
