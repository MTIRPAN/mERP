import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App as AntApp } from 'antd';
import type { ReactNode } from 'react';
import { I18nextProvider } from 'react-i18next';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import {
  ImportStatus,
  type ImportFileInspection,
  type ImportJobDetails,
  type ImportMappingSuggestion,
  type MeResponse,
} from '@/shared/api/generated/model';
import { createTestI18n, renderWithProviders } from '@/test/render';
import { ImportDrawer } from './ImportDrawer';

const startImport = vi.fn<(variables: { key: string; data: { file: File } }) => Promise<{ id: string }>>();
const commitImport = vi.fn<(variables: { id: string }) => Promise<void>>();
const inspect = vi.fn<(key: string, body: { file: File }) => Promise<ImportFileInspection>>();
const suggest = vi.fn<(key: string, body: { headers: unknown[] }) => Promise<ImportMappingSuggestion>>();
let job: ImportJobDetails | undefined;

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useStartImport: () => ({ mutateAsync: startImport, isPending: false }),
  useCommitImport: () => ({ mutateAsync: commitImport, isPending: false }),
  useGetImportJob: (id: string) => ({ data: id ? job : undefined, refetch: vi.fn() }),
  getImportTemplate: vi.fn(),
  getImportReport: vi.fn(),
  inspectImportFile: (key: string, body: { file: File }) => inspect(key, body),
  suggestImportMapping: (key: string, body: { headers: unknown[] }) => suggest(key, body),
}));

/** A file in the template's form: it goes without a mapping. */
const templateFile: ImportFileInspection = { headers: [], columns: [], mapping: {}, complete: true };

/** A customer's own headers: the company code matches, the branch code does not. */
const ownFile: ImportFileInspection = {
  headers: [
    { index: 0, text: 'Şirket kodu', hint: 'text up to 6 characters' },
    { index: 1, text: 'Kod', hint: 'text up to 6 characters' },
    { index: 2, text: 'Not', hint: 'text up to 40 characters' },
  ],
  columns: [
    { key: 'companyCode', header: 'Şirket kodu', type: 'Text', required: true },
    { key: 'code', header: 'Şube kodu', type: 'Text', required: true },
    { key: 'phone', header: 'Telefon', type: 'Text', required: false },
  ],
  mapping: { companyCode: 0 },
  complete: false,
};

const withAi = { ai: { enabled: true, features: ['platform.import_mapping'] } } as unknown as MeResponse;

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
  'web.imports.mode.create': 'Yalnız yeni kayıt ekle',
  'web.imports.mode.create_or_update': 'Kayıtlı olanları güncelle',
  'web.imports.ready_counts': '{created} kayıt eklenecek, {updated} kayıt güncellenecek.',
  'web.imports.commit_valid': 'Hatasız satırları aktar',
  'web.imports.commit_valid_count': 'Hatasız {rows} satırı aktar',
  'web.imports.commit_valid_confirm': '{rows} satır içe aktarılacak, hatalı {skipped} satır atlanacak.',
  'web.imports.mapping.title': 'Sütunları eşleştirin',
  'web.imports.mapping.confirm': 'Eşleştirmeyle yükle',
  'web.imports.mapping.suggested': 'Yapay zeka önerisi',
  'web.imports.mapping.required_missing': 'Zorunlu alanlar için sütun seçin.',
};

const details = (status: ImportStatus, overrides: Partial<ImportJobDetails> = {}): ImportJobDetails => ({
  id: 'job-1',
  importKey: 'platform.branches',
  importName: 'Şubeler',
  fileName: 'subeler.xlsx',
  status,
  mode: 'Create',
  totalRows: 3,
  errorRows: 0,
  updateRows: 0,
  canCommitValidRows: false,
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
    inspect.mockResolvedValue(templateFile);
  });

  it('maps a file with its own headers, with the AI suggestion, before uploading it', async () => {
    inspect.mockResolvedValue(ownFile);
    suggest.mockResolvedValue({
      mapping: { companyCode: 0, code: 1 },
      suggested: ['code'],
      model: 'claude-sonnet-5-5',
      promptVersion: '2026-11-01',
    });
    job = details(ImportStatus.Ready);
    renderWithProviders(
      <MeContext.Provider value={withAi}>
        <ImportDrawer importKey="platform.branches" name="Şubeler" open onClose={vi.fn()} onImported={vi.fn()} />
      </MeContext.Provider>,
      texts,
    );
    await upload();

    expect(await screen.findByText('Sütunları eşleştirin')).toBeInTheDocument();
    expect(startImport).not.toHaveBeenCalled();
    expect(await screen.findByText('Yapay zeka önerisi')).toBeInTheDocument();
    expect(suggest).toHaveBeenCalledWith('platform.branches', { headers: ownFile.headers });
    expect(screen.queryByText('Zorunlu alanlar için sütun seçin.')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Eşleştirmeyle yükle' }));
    await waitFor(() =>
      expect(startImport).toHaveBeenCalledWith({
        key: 'platform.branches',
        data: { file: expect.any(File) as File, mapping: JSON.stringify({ companyCode: 0, code: 1 }) },
      }),
    );
    expect(await screen.findByText('3 satır hazır.')).toBeInTheDocument();
  });

  it('waits for the required columns when there is no AI', async () => {
    inspect.mockResolvedValue(ownFile);
    renderWithProviders(
      <ImportDrawer importKey="platform.branches" name="Şubeler" open onClose={vi.fn()} onImported={vi.fn()} />,
      texts,
    );
    await upload();

    expect(await screen.findByText('Zorunlu alanlar için sütun seçin.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Eşleştirmeyle yükle' })).toBeDisabled();
    expect(suggest).not.toHaveBeenCalled();
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

  it('asks whether known records are updated, sends the mode and shows what will change', async () => {
    job = details(ImportStatus.Ready, { mode: 'CreateOrUpdate', updateRows: 1 });
    renderWithProviders(
      <ImportDrawer
        importKey="hr.employees"
        name="Personel"
        open
        onClose={vi.fn()}
        onImported={vi.fn()}
        supportsUpdate
      />,
      texts,
    );

    expect(screen.getByRole('radio', { name: 'Yalnız yeni kayıt ekle' })).toBeChecked();
    await userEvent.click(screen.getByRole('radio', { name: /Kayıtlı olanları güncelle/ }));
    await upload();

    await waitFor(() =>
      expect(startImport).toHaveBeenCalledWith({
        key: 'hr.employees',
        data: { file: expect.any(File) as File, mode: 'CreateOrUpdate' },
      }),
    );
    expect(await screen.findByText('2 kayıt eklenecek, 1 kayıt güncellenecek.')).toBeInTheDocument();
  });

  it('offers no mode to an import without updates', async () => {
    renderWithProviders(
      <ImportDrawer importKey="platform.branches" name="Şubeler" open onClose={vi.fn()} onImported={vi.fn()} />,
      texts,
    );
    await screen.findByText('Şubeler: Excel içe aktar');

    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
    await upload();
    await waitFor(() =>
      expect(startImport).toHaveBeenCalledWith({ key: 'platform.branches', data: { file: expect.any(File) as File } }),
    );
  });

  it('commits the rows without errors of a file with errors after a confirmation', async () => {
    job = details(ImportStatus.Invalid, {
      errorRows: 1,
      errorCount: 1,
      canCommitValidRows: true,
      errors: [{ row: 3, column: 'tckn', message: 'T.C. kimlik numarası geçersiz.' }],
    });
    renderWithProviders(
      <ImportDrawer importKey="hr.employees" name="Personel" open onClose={vi.fn()} onImported={vi.fn()} />,
      texts,
    );
    await upload();

    await userEvent.click(await screen.findByRole('button', { name: 'Hatasız 2 satırı aktar' }));
    expect(await screen.findByText('2 satır içe aktarılacak, hatalı 1 satır atlanacak.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Hatasız satırları aktar' }));

    await waitFor(() => expect(commitImport).toHaveBeenCalledWith({ id: 'job-1', params: { skipInvalidRows: true } }));
  });

  it('offers no partial commit when the file as a whole is wrong', async () => {
    job = details(ImportStatus.Invalid, {
      errorRows: 1,
      errorCount: 1,
      errors: [{ row: 1, column: 'tckn', message: 'Sütun dosyada yok.' }],
    });
    renderWithProviders(
      <ImportDrawer importKey="hr.employees" name="Personel" open onClose={vi.fn()} onImported={vi.fn()} />,
      texts,
    );
    await upload();

    expect(await screen.findByText('Sütun dosyada yok.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Hatasız/ })).not.toBeInTheDocument();
  });
});
