import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type {
  MeResponse,
  OrgUnitDetails,
  OrgUnitTree,
  PagedResultOfPositionListItem,
} from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { unit } from '@/test/orgUnits';
import { renderWithProviders } from '@/test/render';
import { OrgStructurePage } from './OrgStructurePage';

const tree: OrgUnitTree = {
  companyId: 'c1',
  units: [
    unit('gm', null, { name: 'Genel Müdürlük', code: 'GM', type: 'Directorate' }),
    unit('satis', 'gm', {
      name: 'Satış Direktörlüğü',
      code: 'SATIS',
      type: 'Directorate',
      managerPositionId: 'p1',
      managerPositionCode: 'SAT-01',
      managerJobName: 'Satış Direktörü',
      positionCount: 2,
      norm: 5,
      vacant: 5,
    }),
    unit('ist', 'satis', { name: 'İstanbul Ekibi', code: 'IST', type: 'Team' }),
  ],
};

const details: OrgUnitDetails = {
  id: 'satis',
  companyId: 'c1',
  companyName: 'Acme',
  parentId: 'gm',
  parentName: 'Genel Müdürlük',
  code: 'SATIS',
  name: 'Satış Direktörlüğü',
  type: 'Directorate',
  managerPositionId: 'p1',
  managerPositionCode: 'SAT-01',
  managerJobName: 'Satış Direktörü',
  costCenterCode: 'MM-100',
  branchId: null,
  branchName: null,
  validFrom: null,
  validTo: null,
  isActive: true,
  version: 3,
  createdAt: '2026-10-17T09:00:00Z',
  modifiedAt: null,
};

const positions: PagedResultOfPositionListItem = {
  items: [
    {
      id: 'p2',
      companyId: 'c1',
      companyName: 'Acme',
      code: 'SAT-02',
      orgUnitId: 'satis',
      orgUnitName: 'Satış Direktörlüğü',
      jobId: 'j1',
      jobCode: 'TEMS',
      jobName: 'Satış Temsilcisi',
      reportsToPositionId: 'p1',
      reportsToPositionCode: 'SAT-01',
      norm: 4,
      filled: 0,
      vacant: 4,
      status: 'Active',
      isManager: false,
    },
  ],
  page: 1,
  pageSize: 200,
  totalCount: 1,
};

const getTree = vi.fn((params: { companyId?: string }) => ({
  data: params.companyId ? tree : undefined,
  isLoading: false,
}));

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useGetOrgUnitTree: (params: { companyId?: string }) => getTree(params),
  useGetOrgUnit: () => ({ data: details }),
  useListPositions: () => ({ data: positions, isFetching: false }),
}));

const texts: Record<string, string> = {
  'web.hr.org.title': 'Organizasyon',
  'web.hr.org.view_tree': 'Ağaç',
  'web.hr.org.view_chart': 'Şema',
  'web.hr.org.new_unit': 'Yeni birim',
  'web.hr.org.new_sub_unit': 'Alt birim ekle',
  'web.hr.org.move': 'Taşı',
  'web.hr.org.search': 'Birim ara',
  'web.hr.org.select_unit': 'Bir birim seçin.',
  'web.hr.org.stats.vacant': 'Boş kadro',
  'web.hr.org.vacant_count': '{count} boş',
  'web.hr.org.fields.manager': 'Yönetici pozisyonu',
  'web.hr.org.fields.cost_center': 'Masraf merkezi',
  'web.hr.org.fields.no_manager': 'Yönetici atanmamış',
  'web.hr.org.positions': 'Pozisyonlar',
  'web.hr.positions.new': 'Yeni pozisyon',
  'web.hr.org.staffing': 'Norm {norm} · Dolu {filled} · Boş {vacant}',
  'web.common.edit': 'Düzenle',
};

const me = { companies: [{ id: 'c1', name: 'Acme' }] } as unknown as MeResponse;

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <OrgStructurePage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('OrgStructurePage', () => {
  it('shows the tree of the first company with its vacancies', () => {
    render(['hr.org.read']);

    expect(getTree).toHaveBeenCalledWith({ companyId: 'c1' });
    const tree = screen.getByRole('tree');
    expect(within(tree).getByText('Genel Müdürlük')).toBeInTheDocument();
    expect(within(tree).getByText('İstanbul Ekibi')).toBeInTheDocument();
    expect(within(tree).getByText('5 boş')).toBeInTheDocument();
    expect(screen.getByText('Bir birim seçin.')).toBeInTheDocument();
    // Read only: nothing to change
    expect(screen.queryByRole('button', { name: /Yeni birim/ })).not.toBeInTheDocument();
  });

  it('shows the selected unit with its manager and positions', async () => {
    render(['hr.org.read', 'hr.org.manage']);

    await userEvent.click(within(screen.getByRole('tree')).getByText('Satış Direktörlüğü'));

    expect(await screen.findByText('SAT-01 · Satış Direktörü')).toBeInTheDocument();
    expect(screen.getByText('MM-100')).toBeInTheDocument();
    expect(screen.getByText('Satış Temsilcisi')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Alt birim ekle/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Taşı/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Yeni pozisyon/ })).toBeInTheDocument();
  });

  it('filters the tree by name or code', async () => {
    render(['hr.org.read']);

    await userEvent.type(screen.getByPlaceholderText('Birim ara'), 'ist');

    const tree = screen.getByRole('tree');
    expect(within(tree).getByText('İstanbul Ekibi')).toBeInTheDocument();
    // The units leading to the match stay, the others go
    expect(within(tree).getByText('Genel Müdürlük')).toBeInTheDocument();
    expect(within(tree).queryByText('5 boş')).toBeInTheDocument();
  });

  it('draws the chart and selects a unit from its card', async () => {
    render(['hr.org.read']);

    await userEvent.click(screen.getByText('Şema'));
    const card = screen.getByRole('button', { name: 'Satış Direktörlüğü' });
    expect(within(card).getByText('Satış Direktörü')).toBeInTheDocument();
    expect(
      within(screen.getByRole('button', { name: 'İstanbul Ekibi' })).getByText('Yönetici atanmamış'),
    ).toBeInTheDocument();

    await userEvent.click(card);

    expect(card).toHaveAttribute('aria-pressed', 'true');
    expect(await screen.findByText('MM-100')).toBeInTheDocument();
  });
});
