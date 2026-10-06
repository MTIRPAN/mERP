import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within, type BoundFunctions, type queries } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { AssignRoleMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { MeResponse, UserDetails } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { UserDetailPage } from './UserDetailPage';

const assignRole = vi.fn<(variables: AssignRoleMutationVariables) => Promise<void>>();

const user: UserDetails = {
  id: 'u1',
  userName: 'ayse',
  displayName: 'Ayşe Yılmaz',
  email: 'ayse@acme.test',
  firstName: 'Ayşe',
  lastName: 'Yılmaz',
  isActive: true,
  employeeId: null,
  culture: null,
  createdAt: '2026-10-01T09:00:00Z',
  assignments: [
    {
      id: 'a1',
      roleId: 'r-emp',
      roleName: 'Çalışan',
      scopeType: 'Self',
      scopeTypeLabel: 'Yalnızca kendisi',
      scopeId: null,
      scopeName: null,
      includeSubUnits: false,
      validFrom: '2026-01-01',
      validTo: '2026-03-31',
      isCurrent: false,
    },
  ],
};

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetUser: () => ({ data: user }),
  getGetUserQueryKey: (id: string) => [`/api/platform/users/${id}`],
  getListUsersQueryKey: () => ['/api/platform/users'],
  getGetMeQueryKey: () => ['/api/platform/me'],
  useSetUserActive: () => ({ mutateAsync: vi.fn() }),
  useSendPasswordReset: () => ({ mutateAsync: vi.fn() }),
  useRemoveAssignment: () => ({ mutateAsync: vi.fn() }),
  useChangeAssignment: () => ({ mutateAsync: vi.fn() }),
  useAssignRole: () => ({ mutateAsync: assignRole }),
  useListRoles: () => ({ data: { items: [{ id: 'r-hr', name: 'İK Yöneticisi' }] } }),
  useListScopeTypes: () => ({
    data: [
      { code: 'Tenant', label: 'Tüm şirketler', requiresId: false, supportsSubUnits: false },
      { code: 'Company', label: 'Şirket', requiresId: true, supportsSubUnits: false },
      { code: 'OrgUnit', label: 'Birim', requiresId: true, supportsSubUnits: true },
    ],
  }),
  useListBranches: () => ({ data: undefined, isFetching: false }),
}));

const texts: Record<string, string> = {
  'web.platform.assignments.new': 'Rol ata',
  'web.platform.assignments.role': 'Rol',
  'web.platform.assignments.scope': 'Veri kapsamı',
  'web.platform.assignments.company': 'Şirket',
  'web.platform.assignments.state.expired': 'Süresi doldu',
  'web.platform.assignments.period_range': '{from} – {to}',
  'web.common.save': 'Kaydet',
};

const me = {
  user: { id: 'admin' },
  cultures: [{ code: 'tr-TR', name: 'Türkçe' }],
  companies: [
    { id: 'c1', name: 'Acme' },
    { id: 'c2', name: 'Globex' },
  ],
} as unknown as MeResponse;

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <Routes>
            <Route path="/access/users/:id" element={<UserDetailPage />} />
          </Routes>
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
    '/access/users/u1',
  );

/** Opens an antd select by its label and picks an option. */
const pick = async (container: BoundFunctions<typeof queries>, label: string, option: string) => {
  await userEvent.click(container.getByLabelText(label));
  await userEvent.click(await screen.findByTitle(option));
};

describe('UserDetailPage', () => {
  beforeEach(() => {
    assignRole.mockReset().mockResolvedValue(undefined);
  });

  it('shows the assignments with their period and state', () => {
    render(['platform.users.read']);

    expect(screen.getByText('Çalışan')).toBeInTheDocument();
    expect(screen.getByText('Süresi doldu')).toBeInTheDocument();
    expect(screen.getByText('1 Oca 2026 – 31 Mar 2026')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Rol ata/ })).not.toBeInTheDocument();
  });

  it('assigns a role in a company scope', async () => {
    render(['platform.users.read', 'platform.users.manage']);

    await userEvent.click(screen.getByRole('button', { name: /Rol ata/ }));
    const drawer = within(await screen.findByRole('dialog'));
    await pick(drawer, 'Rol', 'İK Yöneticisi');
    await pick(drawer, 'Veri kapsamı', 'Şirket');
    await pick(drawer, 'Şirket', 'Globex');
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() => expect(assignRole).toHaveBeenCalledTimes(1));
    expect(assignRole.mock.calls[0]![0]).toEqual({
      id: 'u1',
      data: {
        roleId: 'r-hr',
        scopeType: 'Company',
        scopeId: 'c2',
        includeSubUnits: false,
        validFrom: null,
        validTo: null,
      },
    });
  });

  it('cannot pick a scope type the web has no picker for', async () => {
    render(['platform.users.read', 'platform.users.manage']);

    await userEvent.click(screen.getByRole('button', { name: /Rol ata/ }));
    const drawer = within(await screen.findByRole('dialog'));
    await userEvent.click(drawer.getByLabelText('Veri kapsamı'));

    expect(await screen.findByTitle('Birim')).toHaveClass('ant-select-item-option-disabled');
  });
});
