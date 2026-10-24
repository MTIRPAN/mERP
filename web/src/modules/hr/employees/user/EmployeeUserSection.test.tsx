import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { EmployeeDetails } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { EmployeeUserSection } from './EmployeeUserSection';

const create = vi.fn().mockResolvedValue(undefined);
const link = vi.fn().mockResolvedValue(undefined);

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useCreateEmployeeUser: () => ({ mutateAsync: create }),
  useLinkEmployeeUser: () => ({ mutateAsync: link }),
  useUnlinkEmployeeUser: () => ({ mutateAsync: vi.fn() }),
  useSearchEmployeeUserCandidates: () => ({
    isFetching: false,
    data: [
      {
        id: 'u7',
        displayName: 'Ayşe Yılmaz',
        userName: 'ayse',
        email: 'ayse@acme.test',
        isActive: true,
        employeeId: null,
      },
    ],
  }),
  getGetEmployeeQueryKey: (id: string) => [`/api/hr/employees/${id}`],
  getListEmployeesQueryKey: () => ['/api/hr/employees'],
  getListPositionsQueryKey: () => ['/api/hr/positions'],
}));

const texts: Record<string, string> = {
  'web.hr.employees.user.create': 'Kullanıcı oluştur',
  'web.hr.employees.user.create_submit': 'Oluştur',
  'web.hr.employees.user.link': 'Mevcut kullanıcıya bağla',
  'web.hr.employees.user.link_submit': 'Bağla',
  'web.hr.employees.user.user': 'Kullanıcı',
  'web.hr.employees.user.pending': 'İstek işleniyor…',
  'web.hr.employees.contacts.email': 'E-posta',
};

const employee = (overrides: Partial<EmployeeDetails> = {}) =>
  ({
    id: 'e1',
    userId: null,
    userLinkState: 'None',
    userLinkError: null,
    user: null,
    emails: [{ kind: 'Work', address: 'ayse@acme.test', isPrimary: true }],
    ...overrides,
  }) as unknown as EmployeeDetails;

const render = (data: EmployeeDetails) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={['hr.employees.read', 'hr.employees.user']}>
        <EmployeeUserSection employee={data} />
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
  );

describe('EmployeeUserSection', () => {
  beforeEach(() => {
    create.mockClear();
    link.mockClear();
  });

  it("opens a user with the card's primary e-mail", async () => {
    render(employee());

    await userEvent.click(screen.getByRole('button', { name: /Kullanıcı oluştur/ }));
    expect(await screen.findByDisplayValue('ayse@acme.test')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Oluştur' }));

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith({ id: 'e1', data: { email: 'ayse@acme.test', userName: null } }),
    );
  });

  it('links a user that belongs to nobody yet', async () => {
    render(employee());

    await userEvent.click(screen.getByRole('button', { name: /Mevcut kullanıcıya bağla/ }));
    await userEvent.click(await screen.findByRole('combobox'));
    await userEvent.click(await screen.findByText('Ayşe Yılmaz · ayse@acme.test'));
    await userEvent.click(screen.getByRole('button', { name: 'Bağla' }));

    await waitFor(() => expect(link).toHaveBeenCalledWith({ id: 'e1', data: { userId: 'u7' } }));
  });

  it('waits for Platform without offering another request', () => {
    render(employee({ userLinkState: 'Pending' }));

    expect(screen.getByText('İstek işleniyor…')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Kullanıcı oluştur/ })).not.toBeInTheDocument();
  });
});
