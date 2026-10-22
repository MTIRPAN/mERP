import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PagedResultOfAuditItem, SearchAuditParams } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { AuditHistoryButton } from './AuditHistoryButton';

const useSearchAudit = vi.fn<(params: SearchAuditParams) => { data?: PagedResultOfAuditItem }>();

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useSearchAudit: (params: SearchAuditParams) => useSearchAudit(params),
}));

const texts: Record<string, string> = {
  'web.audit.history': 'Geçmiş',
  'web.audit.action.Update': 'Değişiklik',
  'web.audit.action.Insert': 'Oluşturma',
  'web.audit.history_truncated': 'Son {count} değişiklik gösteriliyor.',
};

const entry = {
  module: 'platform',
  userId: 'u1',
  userName: 'Ayşe Yılmaz',
  entityType: 'Company',
  entityLabel: 'Şirket',
  entityId: 'c1',
  source: null,
};

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <AuditHistoryButton module="platform" entityType="Company" entityId="c1" title="Acme" />
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
  );

describe('AuditHistoryButton', () => {
  beforeEach(() => {
    useSearchAudit.mockReset().mockReturnValue({
      data: {
        items: [
          {
            ...entry,
            id: '3',
            occurredAt: '2026-10-07T10:00:00Z',
            action: 'Update',
            field: 'ShortName',
            fieldLabel: 'Kısa ad',
            oldValue: 'Acme',
            newValue: 'Acme Yeni',
            correlationId: 't2',
          },
          {
            ...entry,
            id: '2',
            occurredAt: '2026-10-07T10:00:00Z',
            action: 'Update',
            field: 'Address.City',
            fieldLabel: 'İl',
            oldValue: 'İstanbul',
            newValue: 'Ankara',
            correlationId: 't2',
          },
          {
            ...entry,
            id: '1',
            occurredAt: '2026-10-06T10:00:00Z',
            action: 'Insert',
            field: 'ShortName',
            fieldLabel: 'Kısa ad',
            oldValue: null,
            newValue: 'Acme',
            correlationId: 't1',
          },
        ],
        page: 1,
        pageSize: 200,
        totalCount: 3,
      },
    });
  });

  it('is hidden without the audit permission', () => {
    render(['platform.companies.read']);

    expect(screen.queryByRole('button', { name: /Geçmiş/ })).not.toBeInTheDocument();
  });

  it('shows the changes of the record per operation with old and new values', async () => {
    render(['platform.audit.read']);

    await userEvent.click(screen.getByRole('button', { name: /Geçmiş/ }));

    expect(await screen.findByText('Acme Yeni')).toBeInTheDocument();
    expect(screen.getByText('İstanbul')).toBeInTheDocument();
    expect(screen.getByText('Ankara')).toBeInTheDocument();
    expect(screen.getAllByText('Değişiklik')).toHaveLength(1);
    expect(screen.getByText('Oluşturma')).toBeInTheDocument();
    expect(screen.queryByText(/Son 3 değişiklik/)).not.toBeInTheDocument();
    expect(useSearchAudit).toHaveBeenLastCalledWith({
      module: 'platform',
      entityType: 'Company',
      entityId: 'c1',
      pageSize: 200,
    });
  });
});
