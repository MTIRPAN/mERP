import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App as AntApp } from 'antd';
import type { ReactNode } from 'react';
import { I18nextProvider } from 'react-i18next';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ImportStatus, type ImportJobDetails } from '@/shared/api/generated/model';
import { createTestI18n, renderWithProviders } from '@/test/render';
import { ImportDrawer } from './ImportDrawer';

const startImport = vi.fn<(variables: { key: string; data: { file: File } }) => Promise<{ id: string }>>();
const commitImport = vi.fn<(variables: { id: string }) => Promise<void>>();
let job: ImportJobDetails | undefined;

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useStartImport: () => ({ mutateAsync: startImport, isPending: false }),
  useCommitImport: () => ({ mutateAsync: commitImport, isPending: false }),
  useGetImportJob: (id: string) => ({ data: id ? job : undefined, refetch: vi.fn() }),
  getImportTemplate: vi.fn(),
  getImportReport: vi.fn(),
}));

const texts: Record<string, string> = {
  'web.imports.title': '{name}: Excel içe aktar',
  'web.imports.drop': 'Dosyayı sürükleyin',
  'web.imports.template': 'Şablonu indir',
  'web.imports.ready': '{rows} satır hazır.',
  'web.imports.commit': 'İçe aktar',
  'web.imports.invalid': '{total} satırın {rows} tanesinde hata var.',
  'web.imports.report': 'Hata raporunu indir',
  'web.imports.completed': '{rows} kayıt içe aktarıldı.',
  'web.imports.errors.row': 'Satır',
  'web.imports.errors.column': 'Sütun',
};

const details = (status: ImportStatus, overrides: Partial<ImportJobDetails> = {}): ImportJobDetails => ({
  id: 'job-1',
  importKey: 'platform.branches',
  importName: 'Şubeler',
  fileName: 'subeler.xlsx',
  status,
  totalRows: 3,
  errorRows: 0,
  committedRows: 0,
  errorCount: 0,
  errors: [],
  columnNames: { companyCode: 'Şirket kodu' },
  createdAt: '2026-10-13T09:00:00Z',
  completedAt: null,
  ...overrides,
});

const upload = async () => {
  const input = document.querySelector<HTMLInputElement>('input[type="file"]')!;
  await userEvent.upload(input, new File(['x'], 'subeler.xlsx'));
};

describe('ImportDrawer', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    job = undefined;
    startImport.mockResolvedValue({ id: 'job-1' });
  });

  it('uploads the file and lists the errors with their row and column header', async () => {
    job = details(ImportStatus.Invalid, {
      errorRows: 1,
      errorCount: 1,
      errors: [{ row: 3, column: 'companyCode', message: 'Şirket bulunamadı.' }],
    });
    renderWithProviders(
      <ImportDrawer importKey="platform.branches" name="Şubeler" open onClose={vi.fn()} onImported={vi.fn()} />,
      texts,
    );

    expect(await screen.findByText('Şubeler: Excel içe aktar')).toBeInTheDocument();
    await upload();

    await waitFor(() =>
      expect(startImport).toHaveBeenCalledWith(expect.objectContaining({ key: 'platform.branches' })),
    );
    expect(await screen.findByText('3 satırın 1 tanesinde hata var.')).toBeInTheDocument();
    expect(screen.getByText('Şirket kodu')).toBeInTheDocument();
    expect(screen.getByText('Şirket bulunamadı.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Hata raporunu indir/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'İçe aktar' })).not.toBeInTheDocument();
  });

  it('commits a file without errors and tells the page once', async () => {
    job = details(ImportStatus.Ready);
    const onImported = vi.fn();
    const i18n = createTestI18n(texts);
    const wrapper = ({ children }: { children: ReactNode }) => (
      <I18nextProvider i18n={i18n}>
        <AntApp>{children}</AntApp>
      </I18nextProvider>
    );
    const { rerender } = render(
      <ImportDrawer importKey="platform.branches" name="Şubeler" open onClose={vi.fn()} onImported={onImported} />,
      { wrapper },
    );
    await upload();

    await userEvent.click(await screen.findByRole('button', { name: 'İçe aktar' }));
    expect(commitImport).toHaveBeenCalledWith({ id: 'job-1' });

    job = details(ImportStatus.Completed, { committedRows: 3 });
    rerender(
      <ImportDrawer importKey="platform.branches" name="Şubeler" open onClose={vi.fn()} onImported={onImported} />,
    );
    expect(await screen.findByText('3 kayıt içe aktarıldı.')).toBeInTheDocument();
    rerender(
      <ImportDrawer importKey="platform.branches" name="Şubeler" open onClose={vi.fn()} onImported={onImported} />,
    );
    expect(onImported).toHaveBeenCalledTimes(1);
  });
});
