import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes, useLocation } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { DocumentTemplateItem, MeResponse } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { DocumentTemplatesPage } from './DocumentTemplatesPage';

const template: DocumentTemplateItem = {
  id: 't1',
  documentType: 'platform.letter',
  documentTypeName: 'Antetli genel yazı',
  companyId: null,
  companyName: null,
  culture: 'tr-TR',
  revision: 2,
  isActive: true,
  version: 1,
};

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useListDocumentTypes: () => ({ data: [{ key: 'platform.letter', name: 'Antetli genel yazı' }] }),
  useListDocumentTemplates: () => ({ data: [template], isFetching: false }),
}));

const texts: Record<string, string> = {
  'web.document_templates.new': 'Yeni şablon',
  'web.document_templates.open_editor': 'Editörü aç',
  'web.document_templates.all_companies': 'Tüm şirketler',
  'web.document_templates.revision_value': 'Rev. {revision}',
  'web.status.active': 'Aktif',
};

const me = {
  cultures: [{ code: 'tr-TR', name: 'Türkçe' }],
  companies: [{ id: 'c1', name: 'Acme' }],
} as unknown as MeResponse;

const Location = () => {
  const location = useLocation();
  return <p data-testid="location">{location.pathname + location.search}</p>;
};

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <Routes>
            <Route path="/" element={<DocumentTemplatesPage />} />
            <Route path="*" element={<Location />} />
          </Routes>
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('DocumentTemplatesPage', () => {
  it('lists the templates with their company, culture and revision', () => {
    render(['platform.document_templates.read']);

    const row = screen.getByRole('row', { name: /Antetli genel yazı/ });
    expect(within(row).getByText('Tüm şirketler')).toBeInTheDocument();
    expect(within(row).getByText('Türkçe')).toBeInTheDocument();
    expect(within(row).getByText('Rev. 2')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni şablon/ })).not.toBeInTheDocument();
  });

  it('opens the editor of a new template with the chosen type and culture', async () => {
    render(['platform.document_templates.read', 'platform.document_templates.manage']);

    await userEvent.click(screen.getByRole('button', { name: /Yeni şablon/ }));
    await userEvent.click(await screen.findByRole('button', { name: /Editörü aç/ }));

    expect(await screen.findByTestId('location')).toHaveTextContent(
      '/system/document-templates/new?type=platform.letter&culture=tr-TR',
    );
  });
});
