import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type {
  CreateDocumentTemplateMutationVariables,
  PreviewDocumentMutationVariables,
  UpdateDocumentTemplateMutationVariables,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { DocumentTemplateDetails, DocumentTypeItem, MeResponse } from '@/shared/api/generated/model';
import { ApiError } from '@/shared/api/problem';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import type { CodeEditorProps } from '@/shared/components/code-editor/CodeEditor';
import { renderWithProviders } from '@/test/render';
import { DocumentTemplateEditorPage } from './DocumentTemplateEditorPage';

const preview = vi.fn<(variables: PreviewDocumentMutationVariables) => Promise<{ html: string }>>();
const create = vi.fn<(variables: CreateDocumentTemplateMutationVariables) => Promise<{ id: string }>>();
const update = vi.fn<(variables: UpdateDocumentTemplateMutationVariables) => Promise<void>>();

const letter: DocumentTypeItem = {
  key: 'platform.letter',
  name: 'Antetli genel yazı',
  defaultBody: "{% render 'letterhead' %}<p>{{ letter.subject }}</p>",
  defaultCss: 'p { margin: 0; }',
  sampleModel: { letter: { subject: 'Konu' } },
  fields: [{ path: 'letter.subject', description: 'Yazının konusu' }],
  partials: [{ path: 'letterhead', description: 'Antet' }],
};

const saved: DocumentTemplateDetails = {
  id: 't1',
  documentType: 'platform.letter',
  documentTypeName: 'Antetli genel yazı',
  companyId: 'c1',
  companyName: 'Acme',
  culture: 'tr-TR',
  body: '<h1>{{ letter.subject }}</h1>',
  css: null,
  revision: 3,
  isActive: true,
  version: 7,
};

// Monaco does not run in jsdom: a text area shows the value and the marked line
vi.mock('@/shared/components/code-editor/CodeEditor', () => ({
  CodeEditor: ({ label, value, onChange, readOnly, error }: CodeEditorProps) => (
    <textarea
      aria-label={label}
      value={value}
      readOnly={readOnly}
      data-error-line={error?.line}
      onChange={(event) => onChange(event.target.value)}
    />
  ),
}));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  getGetDocumentTemplateQueryKey: (id: string) => [`/api/platform/document-templates/${id}`],
  getListDocumentTemplatesQueryKey: () => ['/api/platform/document-templates'],
  useListDocumentTypes: () => ({ data: [letter] }),
  useGetDocumentTemplate: (id: string) => ({ data: id ? saved : undefined }),
  usePreviewDocument: () => ({ mutateAsync: preview }),
  usePreviewDocumentPdf: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useCreateDocumentTemplate: () => ({ mutateAsync: create, isPending: false }),
  useUpdateDocumentTemplate: () => ({ mutateAsync: update, isPending: false }),
  useDeleteDocumentTemplate: () => ({ mutateAsync: vi.fn() }),
}));

const texts: Record<string, string> = {
  'web.document_templates.body': 'Gövde (HTML)',
  'web.document_templates.css': 'Stil (CSS)',
  'web.document_templates.all_companies': 'Tüm şirketler',
  'web.document_templates.revision_value': 'Rev. {revision}',
  'web.common.save': 'Kaydet',
};

const me = {
  cultures: [{ code: 'tr-TR', name: 'Türkçe' }],
  companies: [{ id: 'c1', name: 'Acme' }],
} as unknown as MeResponse;

const render = (
  route: string,
  permissions = ['platform.document_templates.read', 'platform.document_templates.manage'],
) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <Routes>
            <Route path="/system/document-templates/new" element={<DocumentTemplateEditorPage />} />
            <Route path="/system/document-templates/:id" element={<DocumentTemplateEditorPage />} />
            <Route path="/system/document-templates" element={<p>list</p>} />
          </Routes>
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('DocumentTemplateEditorPage', () => {
  beforeEach(() => {
    preview.mockReset().mockResolvedValue({ html: '<p>Konu</p>' });
    create.mockReset().mockResolvedValue({ id: 't2' });
    update.mockReset().mockResolvedValue();
  });

  it('starts a new template from the built-in one, previews it and creates it for the chosen company', async () => {
    render('/system/document-templates/new?type=platform.letter&company=c1&culture=tr-TR');

    const body = await screen.findByRole('textbox', { name: 'Gövde (HTML)' });
    expect(body).toHaveValue(letter.defaultBody);
    await waitFor(() =>
      expect(preview).toHaveBeenCalledWith({
        data: {
          documentType: 'platform.letter',
          companyId: 'c1',
          culture: 'tr-TR',
          body: letter.defaultBody,
          css: letter.defaultCss,
        },
      }),
    );
    expect(await screen.findByTitle('web.document_templates.preview')).toHaveAttribute('srcdoc', '<p>Konu</p>');

    fireEvent.change(body, { target: { value: '<p>Yeni</p>' } });
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() => expect(create).toHaveBeenCalledTimes(1));
    expect(create.mock.calls[0]![0].data).toEqual({
      documentType: 'platform.letter',
      companyId: 'c1',
      culture: 'tr-TR',
      body: '<p>Yeni</p>',
      css: letter.defaultCss,
    });
  });

  it('marks the line of a template error from the preview', async () => {
    preview.mockRejectedValue(
      new ApiError(400, {
        status: 400,
        fieldErrors: [
          {
            field: 'body',
            code: 'common.templates.errors.invalid',
            message: 'Şablon 3. satırda hatalı: etiket kapanmadı',
          },
        ],
      }),
    );
    render('/system/document-templates/new?type=platform.letter&culture=tr-TR');

    expect(await screen.findByText('Şablon 3. satırda hatalı: etiket kapanmadı')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Gövde (HTML)' })).toHaveAttribute('data-error-line', '3');
  });

  it('saves a template with the version it read', async () => {
    render('/system/document-templates/t1');

    const body = await screen.findByRole('textbox', { name: 'Gövde (HTML)' });
    expect(body).toHaveValue(saved.body);
    fireEvent.change(body, { target: { value: '<h2>{{ letter.subject }}</h2>' } });
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() =>
      expect(update).toHaveBeenCalledWith({
        id: 't1',
        data: { version: 7, body: '<h2>{{ letter.subject }}</h2>', css: null, isActive: true },
      }),
    );
  });

  it('is read only without the manage permission', async () => {
    render('/system/document-templates/t1', ['platform.document_templates.read']);

    expect(await screen.findByRole('textbox', { name: 'Gövde (HTML)' })).toHaveAttribute('readonly');
    expect(screen.queryByRole('button', { name: /Kaydet/ })).not.toBeInTheDocument();
  });
});
