import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { SetTextMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { CultureItem, ListTextsParams, PagedResultOfTextItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { LocalizationPage } from './LocalizationPage';

const listTexts = vi.fn<(culture: string, params: ListTextsParams) => Promise<PagedResultOfTextItem>>();
const setText = vi.fn<(variables: SetTextMutationVariables) => Promise<void>>();

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
};

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <PermissionProvider permissions={permissions}>
        <LocalizationPage />
      </PermissionProvider>
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
