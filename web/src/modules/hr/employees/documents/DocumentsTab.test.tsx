import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { EmployeeDocumentList } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { DocumentsTab } from './DocumentsTab';

const documents = vi.fn<() => { data?: EmployeeDocumentList; isError: boolean }>();

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useListEmployeeDocuments: () => documents(),
  getListEmployeeDocumentsQueryKey: (id: string) => [`/api/hr/employees/${id}/documents`],
  downloadEmployeeDocument: vi.fn(),
  useDeleteEmployeeDocument: () => ({ mutateAsync: vi.fn() }),
  useReplaceEmployeeDocumentFile: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const texts: Record<string, string> = {
  'web.hr.documents.add': 'Belge ekle',
  'web.hr.documents.expired': 'Süresi doldu',
  'web.hr.documents.days_left': '{days} gün kaldı',
  'web.hr.documents.download': 'İndir',
  'web.common.edit': 'Düzenle',
  'web.common.delete': 'Sil',
};

const item = (id: string, daysLeft: number | null) => ({
  id,
  typeId: 't1',
  typeCode: 'HEALTH_REPORT',
  typeName: `Sağlık raporu ${id}`,
  number: null,
  issuedOn: null,
  validUntil: daysLeft == null ? null : '2026-01-25',
  daysLeft,
  note: null,
  fileName: 'rapor.pdf',
  contentType: 'application/pdf',
  length: 1000,
  version: 1,
  createdAt: '2026-01-05T09:00:00Z',
});

const list = (canManage: boolean): EmployeeDocumentList => ({
  items: [item('a', 20), item('b', 5), item('c', -1), item('d', null)],
  canManage,
  extensions: ['pdf', 'png'],
  maxBytes: 20 * 1024 * 1024,
});

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <DocumentsTab employeeId="e1" />
    </QueryClientProvider>,
    texts,
  );

describe('DocumentsTab', () => {
  beforeEach(() => {
    documents.mockReset().mockReturnValue({ data: list(true), isError: false });
  });

  it('shows how long each document is valid, as HR is reminded', () => {
    render();

    expect(screen.getByText('20 gün kaldı')).toBeInTheDocument();
    expect(screen.getByText('5 gün kaldı')).toBeInTheDocument();
    expect(screen.getByText('Süresi doldu')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /İndir/ })).toHaveLength(4);
    expect(screen.getByRole('button', { name: /Belge ekle/ })).toBeInTheDocument();
  });

  it('offers only the download to those who may not manage the documents', () => {
    documents.mockReturnValue({ data: list(false), isError: false });

    render();

    expect(screen.queryByRole('button', { name: /Belge ekle/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Düzenle/ })).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /İndir/ })).toHaveLength(4);
  });
});
