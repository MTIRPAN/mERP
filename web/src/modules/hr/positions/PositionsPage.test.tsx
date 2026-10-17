import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { ListPositionsParams, MeResponse, PagedResultOfPositionListItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { PositionsPage } from './PositionsPage';

const listPositions = vi.fn<(params: ListPositionsParams) => Promise<PagedResultOfPositionListItem>>();

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  listPositions: (params: ListPositionsParams) => listPositions(params),
  getListPositionsQueryKey: () => ['/api/hr/positions'],
}));

const texts: Record<string, string> = {
  'web.hr.positions.title': 'Pozisyonlar',
  'web.hr.positions.new': 'Yeni pozisyon',
  'web.hr.positions.all': 'Tümü',
  'web.hr.positions.vacant_only': 'Boş kadrolar',
  'web.hr.positions.fields.vacant': 'Boş',
  'web.hr.position_status.Active': 'Aktif',
  'web.hr.position_status.Frozen': 'Dondurulmuş',
};

const page = (vacant: number): PagedResultOfPositionListItem => ({
  items: [
    {
      id: 'p1',
      companyId: 'c1',
      companyName: 'Acme',
      code: 'SAT-01',
      orgUnitId: 'u1',
      orgUnitName: 'Satış',
      jobId: 'j1',
      jobCode: 'TEMS',
      jobName: 'Satış Temsilcisi',
      reportsToPositionId: null,
      reportsToPositionCode: null,
      norm: 4,
      filled: 4 - vacant,
      vacant,
      status: 'Active',
      isManager: false,
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
});

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MeContext.Provider value={{ companies: [] } as unknown as MeResponse}>
        <PermissionProvider permissions={permissions}>
          <PositionsPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('PositionsPage', () => {
  beforeEach(() => {
    listPositions.mockReset().mockResolvedValue(page(3));
  });

  it('lists the positions with their seats', async () => {
    render(['hr.org.read']);

    expect(await screen.findByText('Satış Temsilcisi')).toBeInTheDocument();
    expect(screen.getByText('Aktif')).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni pozisyon/ })).not.toBeInTheDocument();
    expect(listPositions).toHaveBeenCalledWith(expect.objectContaining({ page: 1, vacantOnly: undefined }));
  });

  it('asks for the vacancy report', async () => {
    render(['hr.org.read', 'hr.org.manage']);
    await screen.findByText('Satış Temsilcisi');

    await userEvent.click(screen.getByText('Boş kadrolar'));

    await waitFor(() => expect(listPositions).toHaveBeenLastCalledWith(expect.objectContaining({ vacantOnly: true })));
    expect(screen.getByRole('button', { name: /Yeni pozisyon/ })).toBeInTheDocument();
  });
});
