import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { CreateNotificationTemplateMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { MeResponse, NotificationTemplateList } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import type { CodeEditorProps } from '@/shared/components/code-editor/CodeEditor';
import { renderWithProviders } from '@/test/render';
import { NotificationTemplatesPage } from './NotificationTemplatesPage';

const create = vi.fn<(variables: CreateNotificationTemplateMutationVariables) => Promise<{ id: string }>>();

const list: NotificationTemplateList = {
  items: [
    {
      id: 'n1',
      notificationType: 'platform.test',
      companyId: 'c1',
      companyName: 'Acme',
      culture: 'tr-TR',
      subject: '{{ title }}',
      body: '<p>{{ body }}</p>',
      version: 1,
    },
  ],
  defaultSubject: '{{ title }}',
  defaultBody: '<html>{{ body }}</html>',
};

vi.mock('@/shared/components/code-editor/CodeEditor', () => ({
  CodeEditor: ({ label, value, onChange }: CodeEditorProps) => (
    <textarea aria-label={label} value={value} onChange={(event) => onChange(event.target.value)} />
  ),
}));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  getListNotificationTemplatesQueryKey: () => ['/api/platform/notification-templates'],
  useListNotificationTemplates: () => ({ data: list, isFetching: false }),
  useListNotificationTypes: () => ({ data: [{ type: 'platform.test', label: 'Test bildirimi' }] }),
  useCreateNotificationTemplate: () => ({ mutateAsync: create }),
  useUpdateNotificationTemplate: () => ({ mutateAsync: vi.fn() }),
  useDeleteNotificationTemplate: () => ({ mutateAsync: vi.fn() }),
}));

const texts: Record<string, string> = {
  'web.notification_templates.new': 'Yeni şablon',
  'web.notification_templates.subject': 'Konu',
  'web.notification_templates.body': 'İçerik (HTML)',
  'web.common.save': 'Kaydet',
};

const me = {
  cultures: [{ code: 'tr-TR', name: 'Türkçe' }],
  companies: [{ id: 'c1', name: 'Acme' }],
} as unknown as MeResponse;

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <NotificationTemplatesPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('NotificationTemplatesPage', () => {
  beforeEach(() => {
    create.mockReset().mockResolvedValue({ id: 'n2' });
  });

  it('lists the templates with the type label', () => {
    render(['platform.notification_templates.read']);

    const row = screen.getByRole('row', { name: /Test bildirimi/ });
    expect(within(row).getByText('Acme')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni şablon/ })).not.toBeInTheDocument();
  });

  it('creates a template from the built-in layout', async () => {
    render(['platform.notification_templates.read', 'platform.notification_templates.manage']);

    await userEvent.click(screen.getByRole('button', { name: /Yeni şablon/ }));
    const drawer = within(await screen.findByRole('dialog'));
    expect(drawer.getByRole('textbox', { name: 'İçerik (HTML)' })).toHaveValue(list.defaultBody);
    await userEvent.clear(drawer.getByLabelText('Konu'));
    await userEvent.type(drawer.getByLabelText('Konu'), 'Acme');
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() => expect(create).toHaveBeenCalledTimes(1));
    expect(create.mock.calls[0]![0].data).toEqual({
      notificationType: null,
      companyId: null,
      culture: 'tr-TR',
      subject: 'Acme',
      body: list.defaultBody,
    });
  });
});
