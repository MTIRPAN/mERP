import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AttachmentList, UploadAttachmentBody } from '@/shared/api/generated/model';
import { ApiError } from '@/shared/api/problem';
import { renderWithProviders } from '@/test/render';
import { Attachments } from './Attachments';

const listAttachments = vi.fn<() => Promise<AttachmentList>>();
const uploadAttachment = vi.fn<(body: UploadAttachmentBody) => Promise<{ id: string }>>();
const deleteAttachment = vi.fn<(id: string) => Promise<void>>();

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  getListAttachmentsQueryKey: (params: unknown) => ['/api/platform/attachments', params],
  listAttachments: () => listAttachments(),
  uploadAttachment: (body: UploadAttachmentBody) => uploadAttachment(body),
  deleteAttachment: (id: string) => deleteAttachment(id),
  downloadFile: vi.fn(),
}));

const texts: Record<string, string> = {
  'web.files.drop': 'Dosyayı buraya sürükleyin',
  'web.files.hint': '{extensions} · en fazla {maxMegabytes} MB',
  'web.files.empty': 'Henüz ek yok.',
  'web.files.download': 'İndir',
  'web.files.delete': 'Sil',
  'web.files.delete_confirm': '{name} silinsin mi?',
  'web.files.uploaded': '{name} yüklendi.',
  'web.files.deleted': 'Ek silindi.',
  'web.files.added_by': '{name} ekledi',
  'web.common.cancel': 'Vazgeç',
};

const list = (overrides: Partial<AttachmentList> = {}): AttachmentList => ({
  items: [
    {
      id: 'f1',
      fileName: 'Sözleşme.pdf',
      contentType: 'application/pdf',
      length: 1536,
      createdAt: '2026-10-10T07:00:00Z',
      createdBy: 'Ayşe Yılmaz',
    },
  ],
  canManage: true,
  extensions: ['pdf', 'png'],
  maxBytes: 20 * 1024 * 1024,
  ...overrides,
});

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <Attachments ownerType="platform.company" ownerId="c1" />
    </QueryClientProvider>,
    texts,
  );

describe('Attachments', () => {
  beforeEach(() => {
    listAttachments.mockReset().mockResolvedValue(list());
    uploadAttachment.mockReset().mockResolvedValue({ id: 'f2' });
    deleteAttachment.mockReset().mockResolvedValue(undefined);
  });

  it('lists the files with size, author and the accepted types', async () => {
    render();

    expect(await screen.findByText('Sözleşme.pdf')).toBeInTheDocument();
    expect(screen.getByText(/1,5 kB · .* · Ayşe Yılmaz ekledi/)).toBeInTheDocument();
    expect(screen.getByText('pdf, png · en fazla 20 MB')).toBeInTheDocument();
  });

  it('uploads a dropped file for the record', async () => {
    const { container } = render();
    await screen.findByText('Sözleşme.pdf');
    const file = new File(['%PDF-1.7'], 'rapor.pdf', { type: 'application/pdf' });

    await userEvent.upload(container.querySelector('input[type="file"]')!, file);

    await waitFor(() => expect(uploadAttachment).toHaveBeenCalledTimes(1));
    expect(uploadAttachment.mock.calls[0]![0]).toMatchObject({ ownerType: 'platform.company', ownerId: 'c1' });
    expect(await screen.findByText('rapor.pdf yüklendi.')).toBeInTheDocument();
  });

  it('shows why the server refused a file', async () => {
    uploadAttachment.mockRejectedValue(
      new ApiError(400, {
        code: 'common.errors.validation',
        fieldErrors: [{ field: 'file', code: 'common.files.errors.content_mismatch', message: 'İçerik uyuşmuyor.' }],
      }),
    );
    const { container } = render();
    await screen.findByText('Sözleşme.pdf');

    await userEvent.upload(container.querySelector('input[type="file"]')!, new File(['MZ'], 'fatura.pdf'));

    expect(await screen.findByText('İçerik uyuşmuyor.')).toBeInTheDocument();
  });

  it('deletes after confirmation', async () => {
    render();
    await screen.findByText('Sözleşme.pdf');

    await userEvent.click(screen.getByRole('button', { name: 'Sil' }));
    const confirm = (await screen.findByText('Sözleşme.pdf silinsin mi?')).closest<HTMLElement>('.ant-popover')!;
    await userEvent.click(within(confirm).getByRole('button', { name: 'Sil' }));

    await waitFor(() => expect(deleteAttachment).toHaveBeenCalledWith('f1'));
  });

  it('only lists when the user may not change the files', async () => {
    listAttachments.mockResolvedValue(list({ canManage: false }));
    const { container } = render();

    expect(await screen.findByText('Sözleşme.pdf')).toBeInTheDocument();
    expect(container.querySelector('input[type="file"]')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Sil' })).not.toBeInTheDocument();
  });

  it('says when there are no files', async () => {
    listAttachments.mockResolvedValue(list({ items: [] }));
    render();

    expect(await screen.findByText('Henüz ek yok.')).toBeInTheDocument();
  });
});
