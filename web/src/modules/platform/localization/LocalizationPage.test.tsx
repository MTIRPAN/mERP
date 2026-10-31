import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { SetTextMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type {
  CultureItem,
  DraftTranslationsQuery,
  ListTextsParams,
  MeResponse,
  PagedResultOfTextItem,
  SetTextsCommand,
  TranslationDraftResponse,
} from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { LocalizationPage } from './LocalizationPage';

const listTexts = vi.fn<(culture: string, params: ListTextsParams) => Promise<PagedResultOfTextItem>>();
const setText = vi.fn<(variables: SetTextMutationVariables) => Promise<void>>();
const draft = vi.fn<(culture: string, body: DraftTranslationsQuery) => Promise<TranslationDraftResponse>>();
const setTexts = vi.fn<(culture: string, body: SetTextsCommand) => Promise<{ changed: number }>>();

const cultures: CultureItem[] = [
  {
    code: 'tr-TR',
    name: 'Türkçe',
    isActive: true,
    isDefault: true,
    fallbackCode: null,
    translatedCount: 1000,
    totalCount: 1000,
    version: 1,
  },
  {
    code: 'en-US',
    name: 'English',
    isActive: true,
    isDefault: false,
    fallbackCode: null,
    translatedCount: 10,
    totalCount: 1000,
    version: 2,
  },
];

vi.mock('@/app/i18n/i18n', () => ({ reloadTexts: vi.fn() }));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useListCultures: () => ({ data: cultures }),
  getListCulturesQueryKey: () => ['/api/platform/localization/cultures'],
  getGetMeQueryKey: () => ['/api/platform/me'],
  getListTextsQueryKey: (culture: string) => [`/api/platform/localization/texts/${culture}`],
  listTexts: (culture: string, params: ListTextsParams) => listTexts(culture, params),
  exportTexts: vi.fn(),
  useDeleteCulture: () => ({ mutateAsync: vi.fn() }),
  useAddCulture: () => ({ mutateAsync: vi.fn() }),
  useUpdateCulture: () => ({ mutateAsync: vi.fn() }),
  useSetText: () => ({ mutateAsync: setText, isPending: false }),
  useImportTexts: () => ({ mutateAsync: vi.fn(), isPending: false }),
  draftTranslations: (culture: string, body: DraftTranslationsQuery) => draft(culture, body),
  setTexts: (culture: string, body: SetTextsCommand) => setTexts(culture, body),
}));

const texts: Record<string, string> = {
  'web.localization.cultures.default': 'Varsayılan',
  'web.localization.texts.missing': 'Eksik',
  'web.localization.texts.custom': 'Özel',
  'web.localization.texts.only_missing': 'Yalnızca eksikler',
  'web.localization.texts.translation': 'Çeviri',
  'web.localization.texts.edit': 'Çeviriyi düzenle',
  'web.common.edit': 'Düzenle',
  'web.common.save': 'Kaydet',
  'web.localization.drafts.button': 'Yapay zeka ile taslak',
  'web.localization.drafts.include': 'Kaydet',
  'web.localization.drafts.save': 'Seçilenleri kaydet',
  'web.localization.drafts.placeholders_differ': 'Yer tutucular farklı',
  'web.localization.drafts.remaining': '{count} eksik metin daha var.',
};

/** A profile with the translation drafts on. */
const withAi = { ai: { enabled: true, features: ['platform.translation_drafts'] } } as unknown as MeResponse;

const render = (permissions: string[], me?: MeResponse) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <LocalizationPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('LocalizationPage', () => {
  beforeEach(() => {
    listTexts.mockReset().mockResolvedValue({
      items: [
        { key: 'web.common.save', defaultText: 'Kaydet', text: 'Save', source: 'Database' },
        { key: 'web.common.cancel', defaultText: 'Vazgeç', text: null, source: null },
      ],
      page: 1,
      pageSize: 50,
      totalCount: 2,
    });
    setText.mockReset().mockResolvedValue(undefined);
  });

  it('shows the languages with their coverage and the texts of the first other language', async () => {
    render(['platform.localization.read']);

    expect(screen.getByText('Varsayılan')).toBeInTheDocument();
    expect(screen.getByText('10 / 1000')).toBeInTheDocument();
    expect(await screen.findByText('Save')).toBeInTheDocument();
    expect(screen.getByText('Özel')).toBeInTheDocument();
    expect(screen.getByText('Eksik')).toBeInTheDocument();
    expect(listTexts).toHaveBeenCalledWith('en-US', expect.objectContaining({ page: 1 }));
    expect(screen.queryByRole('button', { name: 'Düzenle' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yapay zeka ile taslak/ })).not.toBeInTheDocument();
  });

  it('drafts missing translations with AI and saves only the reviewed rows', async () => {
    draft.mockReset().mockResolvedValue({
      drafts: [
        { key: 'web.common.cancel', source: 'Vazgeç', text: 'Cancel', placeholdersMatch: true },
        { key: 'web.common.greet', source: 'Merhaba {name}', text: 'Hello', placeholdersMatch: false },
        { key: 'web.common.back', source: 'Geri', text: 'Back', placeholdersMatch: true },
      ],
      remaining: 7,
      sourceCulture: 'tr-TR',
      model: 'claude-sonnet-5-5',
      promptVersion: '2026-11-01',
    });
    setTexts.mockReset().mockResolvedValue({ changed: 2 });
    render(['platform.localization.read', 'platform.localization.manage'], withAi);
    await screen.findByText('Save');

    await userEvent.click(screen.getByRole('button', { name: /Yapay zeka ile taslak/ }));
    const dialog = within(await screen.findByRole('dialog'));
    expect(await dialog.findByText('Merhaba {name}')).toBeInTheDocument();
    expect(draft).toHaveBeenCalledWith('en-US', { prefix: null, search: null, count: 100 });
    expect(dialog.getByText('Yer tutucular farklı')).toBeInTheDocument();
    expect(dialog.getByText('7 eksik metin daha var.')).toBeInTheDocument();

    // The reviewer fixes the placeholder and leaves one draft out
    const greet = dialog.getByRole('textbox', { name: 'web.common.greet' });
    await userEvent.clear(greet);
    await userEvent.type(greet, 'Hello {{name}');
    expect(dialog.queryByText('Yer tutucular farklı')).not.toBeInTheDocument();
    await userEvent.click(dialog.getByRole('checkbox', { name: 'Kaydet web.common.back' }));
    await userEvent.click(dialog.getByRole('button', { name: /Seçilenleri kaydet/ }));

    await waitFor(() =>
      expect(setTexts).toHaveBeenCalledWith('en-US', {
        texts: [
          { key: 'web.common.cancel', value: 'Cancel' },
          { key: 'web.common.greet', value: 'Hello {name}' },
        ],
      }),
    );
  });

  it('filters the missing translations', async () => {
    render(['platform.localization.read']);
    await screen.findByText('Save');

    await userEvent.click(screen.getByRole('switch', { name: 'Yalnızca eksikler' }));

    await waitFor(() =>
      expect(listTexts).toHaveBeenLastCalledWith('en-US', expect.objectContaining({ missing: true })),
    );
  });

  it('translates a missing text', async () => {
    render(['platform.localization.read', 'platform.localization.manage']);
    await screen.findByText('Save');

    const rows = screen.getAllByRole('row');
    const missingRow = rows.find((row) => within(row).queryByText('Eksik'))!;
    await userEvent.click(within(missingRow).getByRole('button', { name: 'Düzenle' }));
    const dialog = within(await screen.findByRole('dialog'));
    await userEvent.type(dialog.getByLabelText('Çeviri'), 'Cancel');
    await userEvent.click(dialog.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() =>
      expect(setText).toHaveBeenCalledWith({ culture: 'en-US', data: { key: 'web.common.cancel', value: 'Cancel' } }),
    );
  });
});
