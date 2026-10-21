import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { MeResponse } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { DocumentTypesPage } from './DocumentTypesPage';

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useListEmployeeDocumentTypes: () => ({
    isLoading: false,
    data: [
      {
        id: 't1',
        code: 'HEALTH_REPORT',
        name: 'Sağlık raporu',
        names: { 'tr-TR': 'Sağlık raporu' },
        isActive: true,
        version: 1,
      },
      { id: 't2', code: 'SRC', name: 'SRC belgesi', names: { 'tr-TR': 'SRC belgesi' }, isActive: false, version: 1 },
    ],
  }),
  getListEmployeeDocumentTypesQueryKey: () => ['/api/hr/document-types'],
  useCreateEmployeeDocumentType: () => ({ mutateAsync: vi.fn() }),
  useUpdateEmployeeDocumentType: () => ({ mutateAsync: vi.fn() }),
}));

vi.mock('@/modules/platform/audit/AuditHistoryButton', () => ({ AuditHistoryButton: () => null }));

const texts: Record<string, string> = {
  'web.hr.document_types.title': 'Belge türleri',
  'web.hr.document_types.new': 'Yeni belge türü',
  'web.common.edit': 'Düzenle',
};

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={{ cultures: [{ code: 'tr-TR', name: 'Türkçe' }] } as unknown as MeResponse}>
        <PermissionProvider permissions={permissions}>
          <DocumentTypesPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('DocumentTypesPage', () => {
  it('lists the active and retired types and offers the changes to their managers', () => {
    render(['hr.employees.documents.read', 'hr.employees.documents.manage']);

    expect(screen.getByText('HEALTH_REPORT')).toBeInTheDocument();
    expect(screen.getByText('SRC belgesi')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Yeni belge türü/ })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /Düzenle/ })).toHaveLength(2);
  });

  it('shows the list only to readers', () => {
    render(['hr.employees.documents.read']);

    expect(screen.getByText('Sağlık raporu')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni belge türü/ })).not.toBeInTheDocument();
  });
});
