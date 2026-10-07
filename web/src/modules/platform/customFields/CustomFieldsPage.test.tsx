import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { CreateCustomFieldMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { CustomFieldDefinitionItem, MeResponse } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { CustomFieldsPage } from './CustomFieldsPage';

const createField = vi.fn<(variables: CreateCustomFieldMutationVariables) => Promise<{ id: string }>>();

const definitions: CustomFieldDefinitionItem[] = [
  {
    id: 'd1',
    entityType: 'platform.company',
    code: 'shoe_size',
    type: 'Number',
    companyId: null,
    companyName: null,
    label: { 'tr-TR': 'Ayakkabı numarası' },
    required: true,
    options: [],
    maxLength: null,
    min: 30,
    max: 50,
    wholeNumber: true,
    group: null,
    order: 10,
    isActive: true,
    version: 2,
  },
];

vi.mock('@/app/i18n/i18n', () => ({ DEFAULT_CULTURE: 'tr-TR' }));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useListCustomFieldEntityTypes: () => ({ data: [{ code: 'platform.company', label: 'Şirket' }] }),
  useListCustomFieldDefinitions: () => ({ data: definitions, isFetching: false }),
  getListCustomFieldDefinitionsQueryKey: () => ['/api/platform/custom-fields/definitions'],
  getGetCustomFieldFormQueryKey: () => ['/api/platform/custom-fields/form'],
  useCreateCustomField: () => ({ mutateAsync: createField }),
  useUpdateCustomField: () => ({ mutateAsync: vi.fn() }),
}));

const texts: Record<string, string> = {
  'web.custom_fields.new': 'Yeni alan',
  'web.custom_fields.code': 'Kod',
  'web.custom_fields.label': 'Ad',
  'web.custom_fields.all_companies': 'Tüm şirketler',
  'web.custom_fields.types.Number': 'Sayı',
  'web.common.save': 'Kaydet',
  'web.common.edit': 'Düzenle',
};

const me = { cultures: [{ code: 'tr-TR', name: 'Türkçe' }], companies: [] } as unknown as MeResponse;

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <CustomFieldsPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('CustomFieldsPage', () => {
  beforeEach(() => {
    createField.mockReset().mockResolvedValue({ id: 'd2' });
  });

  it('lists the definitions of the entity type', () => {
    render(['platform.custom_fields.read']);

    expect(screen.getByText('Ayakkabı numarası')).toBeInTheDocument();
    expect(screen.getByText('shoe_size')).toBeInTheDocument();
    expect(screen.getByText('Sayı')).toBeInTheDocument();
    expect(screen.getByText('Tüm şirketler')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni alan/ })).not.toBeInTheDocument();
  });

  it('defines a text field', async () => {
    render(['platform.custom_fields.read', 'platform.custom_fields.manage']);

    await userEvent.click(screen.getByRole('button', { name: /Yeni alan/ }));
    const drawer = within(await screen.findByRole('dialog'));
    await userEvent.type(drawer.getByLabelText(/^Kod/), 'favorite_color');
    await userEvent.type(drawer.getByLabelText('Ad'), 'Sevdiği renk');
    await userEvent.click(drawer.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() => expect(createField).toHaveBeenCalledTimes(1));
    expect(createField.mock.calls[0]![0].data).toMatchObject({
      entityType: 'platform.company',
      code: 'favorite_color',
      type: 'Text',
      companyId: null,
      label: { 'tr-TR': 'Sevdiği renk' },
      required: false,
      options: [],
      group: null,
    });
  });
});
