import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PagedResultOfAuditItem, SearchAuditParams } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { AuditLogPage } from './AuditLogPage';

const searchAudit = vi.fn<(params: SearchAuditParams) => Promise<PagedResultOfAuditItem>>();

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  searchAudit: (params: SearchAuditParams) => searchAudit(params),
  getSearchAuditQueryKey: () => ['/api/platform/audit'],
  useListAuditEntityTypes: () => ({
    data: [{ module: 'platform', entityType: 'Company', label: 'Şirket' }],
    isFetching: false,
  }),
  useListUsers: () => ({ data: { items: [] }, isFetching: false }),
}));

const texts: Record<string, string> = {
  'web.audit.entity': 'Kayıt türü',
  'web.audit.user': 'Kullanıcı',
  'web.audit.action.Update': 'Değişiklik',
  'web.audit.masked': 'Gizli (değer saklanmaz)',
};

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <PermissionProvider permissions={permissions}>
        <AuditLogPage />
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
  );

describe('AuditLogPage', () => {
  beforeEach(() => {
    searchAudit.mockReset().mockResolvedValue({
      items: [
        {
          id: '1',
          module: 'platform',
          occurredAt: '2026-10-07T10:00:00Z',
          userId: 'u1',
          userName: 'Ayşe Yılmaz',
          entityType: 'Company',
          entityLabel: 'Şirket',
          entityId: 'c1',
          action: 'Update',
          field: 'TaxNumber',
          fieldLabel: 'Vergi kimlik no',
          oldValue: '***',
          newValue: '***',
          correlationId: 't1',
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
  });

  it('lists the entries with labels and masked values', async () => {
    render(['platform.audit.read']);

    expect(await screen.findByText('Vergi kimlik no')).toBeInTheDocument();
    expect(screen.getByText('Ayşe Yılmaz')).toBeInTheDocument();
    expect(screen.getByText('Değişiklik')).toBeInTheDocument();
    expect(screen.getAllByText('Gizli (değer saklanmaz)')).toHaveLength(2);
    // The user filter needs platform.users.read
    expect(screen.queryByLabelText('Kullanıcı')).not.toBeInTheDocument();
  });

  it('filters by entity type', async () => {
    render(['platform.audit.read', 'platform.users.read']);
    await screen.findByText('Vergi kimlik no');

    await userEvent.click(screen.getByLabelText('Kayıt türü'));
    await userEvent.click(await screen.findByTitle('Şirket'));

    await waitFor(() =>
      expect(searchAudit).toHaveBeenLastCalledWith(
        expect.objectContaining({ module: 'platform', entityType: 'Company', page: 1 }),
      ),
    );
    expect(screen.getByLabelText('Kullanıcı')).toBeInTheDocument();
  });
});
