import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { UpdateRoleMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { MeResponse, PermissionModuleNode, RoleDetails } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { RoleEditorPage } from './RoleEditorPage';

const updateRole = vi.fn<(variables: UpdateRoleMutationVariables) => Promise<void>>();

const role: RoleDetails = {
  id: 'r1',
  code: 'PAYROLL',
  name: { 'tr-TR': 'Bordro Uzmanı' },
  description: null,
  isSystem: false,
  permissions: ['platform.companies.read'],
  userCount: 2,
  version: 7,
};

const tree: PermissionModuleNode[] = [
  {
    code: 'platform',
    label: 'Platform',
    resources: [
      {
        code: 'platform.companies',
        label: 'Şirketler',
        permissions: [
          { code: 'platform.companies.read', label: 'Şirket görüntüleme' },
          { code: 'platform.companies.manage', label: 'Şirket düzenleme' },
        ],
      },
    ],
  },
];

vi.mock('@/app/i18n/i18n', () => ({ DEFAULT_CULTURE: 'tr-TR' }));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetRole: () => ({ data: role }),
  useGetPermissionTree: () => ({ data: tree }),
  getGetRoleQueryKey: (id: string) => [`/api/platform/roles/${id}`],
  getListRolesQueryKey: () => ['/api/platform/roles'],
  getGetMeQueryKey: () => ['/api/platform/me'],
  useCreateRole: () => ({ mutateAsync: vi.fn() }),
  useUpdateRole: () => ({ mutateAsync: updateRole }),
  useDeleteRole: () => ({ mutateAsync: vi.fn() }),
  useResetRole: () => ({ mutateAsync: vi.fn() }),
}));

const texts: Record<string, string> = {
  'web.common.save': 'Kaydet',
  'web.platform.roles.delete': 'Sil',
  'web.platform.roles.reset': 'Varsayılana dön',
};

const me = { cultures: [{ code: 'tr-TR', name: 'Türkçe' }] } as unknown as MeResponse;

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <Routes>
            <Route path="/access/roles/:id" element={<RoleEditorPage />} />
          </Routes>
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
    '/access/roles/r1',
  );

describe('RoleEditorPage', () => {
  beforeEach(() => {
    updateRole.mockReset().mockResolvedValue(undefined);
  });

  it('saves the changed permission matrix with the version it read', async () => {
    render(['platform.roles.read', 'platform.roles.manage']);

    await userEvent.click(screen.getByText('Şirket düzenleme'));
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() => expect(updateRole).toHaveBeenCalledTimes(1));
    expect(updateRole.mock.calls[0]![0]).toEqual({
      id: 'r1',
      data: {
        version: 7,
        name: { 'tr-TR': 'Bordro Uzmanı' },
        description: null,
        permissions: ['platform.companies.manage', 'platform.companies.read'],
      },
    });
  });

  it('cannot delete a role that users hold', () => {
    render(['platform.roles.read', 'platform.roles.manage']);

    expect(screen.getByRole('button', { name: /Sil/ })).toBeDisabled();
    expect(screen.queryByRole('button', { name: /Varsayılana dön/ })).not.toBeInTheDocument();
  });

  it('is read-only without the manage permission', () => {
    render(['platform.roles.read']);

    expect(screen.queryByRole('button', { name: /Kaydet/ })).not.toBeInTheDocument();
    expect(screen.getByDisplayValue('Bordro Uzmanı')).toBeDisabled();
  });
});
