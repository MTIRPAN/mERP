import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { CreateCompanyMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { MeResponse, PagedResultOfCompanyListItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { CompaniesPage } from './CompaniesPage';

const listCompanies = vi.fn<() => Promise<PagedResultOfCompanyListItem>>();
const createCompany = vi.fn<(variables: CreateCompanyMutationVariables) => Promise<unknown>>();

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  listCompanies: () => listCompanies(),
  getListCompaniesQueryKey: () => ['/api/platform/companies'],
  getGetCompanyQueryKey: (id: string) => [`/api/platform/companies/${id}`],
  getGetMeQueryKey: () => ['/api/platform/me'],
  useGetCompany: () => ({ data: undefined }),
  useCreateCompany: () => ({ mutateAsync: createCompany }),
  useUpdateCompany: () => ({ mutateAsync: vi.fn() }),
  useListProvinces: () => ({ data: [{ code: 34, name: 'İstanbul' }], isFetching: false }),
  useListDistricts: () => ({ data: [], isFetching: false }),
  useGetCustomFieldForm: () => ({ data: [] }),
}));

const texts: Record<string, string> = {
  'web.platform.companies.title': 'Şirketler',
  'web.platform.companies.new': 'Yeni şirket',
  'web.platform.fields.code': 'Kod',
  'web.platform.companies.short_name': 'Kısa ad',
  'web.platform.companies.title_field': 'Ticaret unvanı',
  'web.platform.companies.tax_number': 'Vergi kimlik no',
  'web.platform.fields.required': 'Bu alan zorunludur.',
  'web.common.save': 'Kaydet',
  'web.status.active': 'Aktif',
  'web.status.inactive': 'Pasif',
};

const me = { cultures: [{ code: 'tr-TR', name: 'Türkçe' }], companies: [] } as unknown as MeResponse;

const render = (permissions: string[], ui: ReactElement = <CompaniesPage />) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>{ui}</PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('CompaniesPage', () => {
  beforeEach(() => {
    listCompanies.mockReset().mockResolvedValue({
      items: [
        {
          id: '1',
          code: 'ACME',
          shortName: 'Acme',
          title: 'Acme Holding A.Ş.',
          taxNumber: '1234567890',
          city: 'İstanbul',
          isActive: true,
          customFields: {},
        },
        {
          id: '2',
          code: 'GLB',
          shortName: 'Globex',
          title: 'Globex A.Ş.',
          taxNumber: '9876543217',
          city: null,
          isActive: false,
          customFields: {},
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
    });
    createCompany.mockReset().mockResolvedValue({ id: '3' });
  });

  it('lists the companies with their status', async () => {
    render(['platform.companies.read']);

    expect(await screen.findByText('Acme Holding A.Ş.')).toBeInTheDocument();
    expect(screen.getByText('Globex')).toBeInTheDocument();
    expect(screen.getByText('Pasif')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni şirket/ })).not.toBeInTheDocument();
  });

  it('creates a company with the manage permission', async () => {
    render(['platform.companies.read', 'platform.companies.manage']);

    await userEvent.click(await screen.findByRole('button', { name: /Yeni şirket/ }));
    const drawer = within(await screen.findByRole('dialog'));
    await userEvent.type(drawer.getByLabelText(/^Kod/), 'NEW');
    await userEvent.type(drawer.getByLabelText('Kısa ad'), 'Yeni');
    await userEvent.type(drawer.getByLabelText('Ticaret unvanı'), 'Yeni Sanayi A.Ş.');
    await userEvent.type(drawer.getByLabelText('Vergi kimlik no'), '1234567890');
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() => expect(createCompany).toHaveBeenCalledTimes(1));
    const data = createCompany.mock.calls[0]![0].data;
    expect(data).toMatchObject({
      code: 'NEW',
      shortName: 'Yeni',
      title: 'Yeni Sanayi A.Ş.',
      taxNumber: '1234567890',
      address: null,
      culture: 'tr-TR',
      timeZoneId: 'Europe/Istanbul',
    });
  });
});
