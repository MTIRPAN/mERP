import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { UpdateSettingsMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { MeResponse, SettingModuleGroup } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { SettingsPage } from './SettingsPage';

const updateSettings = vi.fn<(variables: UpdateSettingsMutationVariables) => Promise<void>>();

const groups: SettingModuleGroup[] = [
  {
    module: 'platform',
    name: 'Platform',
    settings: [
      {
        key: 'platform.notifications.email_enabled',
        label: 'Bildirimleri e-postayla da gönder',
        description: null,
        type: 'Boolean',
        options: [],
        min: null,
        max: null,
        wholeNumber: false,
        maxLength: null,
        companyOverridable: true,
        value: null,
        inheritedValue: true,
        defaultValue: true,
      },
      {
        key: 'platform.test.threshold',
        label: 'Eşik',
        description: null,
        type: 'Number',
        options: [],
        min: 1,
        max: 50,
        wholeNumber: true,
        maxLength: null,
        companyOverridable: false,
        value: 7,
        inheritedValue: 3,
        defaultValue: 3,
      },
    ],
  },
];

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetSettings: () => ({ data: groups }),
  useUpdateSettings: () => ({ mutateAsync: updateSettings, isPending: false }),
  getGetSettingsQueryKey: () => ['/api/platform/settings'],
}));

const texts: Record<string, string> = {
  'web.settings.save': 'Kaydet',
  'web.settings.default': 'Varsayılan',
  'web.settings.reset': 'Varsayılana dön',
  'web.settings.inherited': 'Şu an: {value}',
  'web.settings.yes': 'Açık',
  'web.settings.no': 'Kapalı',
};

const me = { companies: [], cultures: [] } as unknown as MeResponse;

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <SettingsPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('SettingsPage', () => {
  beforeEach(() => {
    updateSettings.mockReset().mockResolvedValue();
  });

  it('shows where a value comes from and saves only what changed', async () => {
    render(['platform.settings.read', 'platform.settings.manage']);

    expect(screen.getByText('Varsayılan')).toBeInTheDocument();
    expect(screen.getByText('Şu an: Açık')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Kaydet' })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: 'Varsayılana dön' }));
    await userEvent.click(screen.getByRole('switch', { name: 'Bildirimleri e-postayla da gönder' }));
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }));

    await waitFor(() =>
      expect(updateSettings).toHaveBeenCalledWith({
        data: {
          companyId: null,
          values: { 'platform.notifications.email_enabled': false, 'platform.test.threshold': null },
        },
      }),
    );
  });

  it('is read only without the manage permission', () => {
    render(['platform.settings.read']);

    expect(screen.queryByRole('button', { name: 'Kaydet' })).not.toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Bildirimleri e-postayla da gönder' })).toBeDisabled();
  });
});
