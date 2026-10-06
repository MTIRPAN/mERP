import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type * as ReactRouter from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { CreateUserMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { MeResponse, PagedResultOfUserListItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { UsersPage } from './UsersPage';

const listUsers = vi.fn<() => Promise<PagedResultOfUserListItem>>();
const createUser = vi.fn<(variables: CreateUserMutationVariables) => Promise<{ id: string }>>();
const navigate = vi.fn();

vi.mock('react-router', async (original) => ({
  ...(await original<typeof ReactRouter>()),
  useNavigate: () => navigate,
}));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  listUsers: () => listUsers(),
  getListUsersQueryKey: () => ['/api/platform/users'],
  useListRoles: () => ({ data: { items: [{ id: 'r1', name: 'İK Yöneticisi' }] }, isFetching: false }),
  useCreateUser: () => ({ mutateAsync: createUser }),
}));

const texts: Record<string, string> = {
  'web.platform.users.new': 'Yeni kullanıcı',
  'web.platform.users.user_name': 'Kullanıcı adı',
  'web.platform.users.first_name': 'Ad',
  'web.platform.users.last_name': 'Soyad',
  'web.platform.users.no_role': 'Rol yok',
  'web.platform.fields.email': 'E-posta',
  'web.common.save': 'Kaydet',
  'web.status.active': 'Aktif',
  'web.status.inactive': 'Pasif',
};

const me = { cultures: [{ code: 'tr-TR', name: 'Türkçe' }], companies: [] } as unknown as MeResponse;

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <UsersPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('UsersPage', () => {
  beforeEach(() => {
    listUsers.mockReset().mockResolvedValue({
      items: [
        {
          id: 'u1',
          userName: 'ayse',
          displayName: 'Ayşe Yılmaz',
          email: 'ayse@acme.test',
          isActive: true,
          employeeId: null,
          roles: ['İK Yöneticisi', 'Çalışan'],
        },
        {
          id: 'u2',
          userName: 'mehmet',
          displayName: 'Mehmet Kaya',
          email: null,
          isActive: false,
          employeeId: null,
          roles: [],
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
    });
    createUser.mockReset().mockResolvedValue({ id: 'u3' });
    navigate.mockReset();
  });

  it('lists the users with the roles in force', async () => {
    render(['platform.users.read']);

    expect(await screen.findByRole('link', { name: 'ayse' })).toHaveAttribute('href', '/access/users/u1');
    expect(screen.getByText('İK Yöneticisi')).toBeInTheDocument();
    expect(screen.getByText('Rol yok')).toBeInTheDocument();
    expect(screen.getByText('Pasif')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni kullanıcı/ })).not.toBeInTheDocument();
  });

  it('creates a user and opens it', async () => {
    render(['platform.users.read', 'platform.users.manage']);

    await userEvent.click(await screen.findByRole('button', { name: /Yeni kullanıcı/ }));
    const drawer = within(await screen.findByRole('dialog'));
    await userEvent.type(drawer.getByLabelText(/^Kullanıcı adı/), 'zeynep');
    await userEvent.type(drawer.getByLabelText('E-posta'), 'zeynep@acme.test');
    await userEvent.type(drawer.getByLabelText('Ad'), 'Zeynep');
    await userEvent.type(drawer.getByLabelText('Soyad'), 'Demir');
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/access/users/u3'));
    expect(createUser.mock.calls[0]![0].data).toEqual({
      userName: 'zeynep',
      email: 'zeynep@acme.test',
      firstName: 'Zeynep',
      lastName: 'Demir',
      culture: null,
    });
  });
});
