import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { UpdateMyPreferencesMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { MeResponse } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { ProfilePage } from './ProfilePage';

const updatePreferences = vi.fn<(variables: UpdateMyPreferencesMutationVariables) => Promise<void>>();
const changeCulture = vi.fn<(culture: string) => Promise<void>>();

vi.mock('@/app/i18n/i18n', () => ({ changeCulture: (culture: string) => changeCulture(culture) }));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useUpdateMyPreferences: () => ({ mutateAsync: updatePreferences }),
}));

const texts: Record<string, string> = {
  'web.profile.language': 'Dil',
  'web.profile.language_device': 'Bu cihazın diline göre',
  'web.common.save': 'Kaydet',
};

const me = {
  user: { id: 'u1', subject: 's1', userName: 'ayse', displayName: 'Ayşe Yılmaz', email: 'ayse@acme.test' },
  tenant: { code: 'acme', name: 'Acme Holding' },
  preferences: { culture: 'tr-TR', preferredCulture: null },
  cultures: [
    { code: 'tr-TR', name: 'Türkçe' },
    { code: 'en-US', name: 'English' },
  ],
} as unknown as MeResponse;

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={me}>
        <ProfilePage />
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('ProfilePage', () => {
  beforeEach(() => {
    updatePreferences.mockReset().mockResolvedValue(undefined);
    changeCulture.mockReset().mockResolvedValue(undefined);
  });

  it('shows the account', () => {
    render();

    expect(screen.getByText('Ayşe Yılmaz')).toBeInTheDocument();
    expect(screen.getByText('Acme Holding')).toBeInTheDocument();
    expect(screen.getByTitle('Bu cihazın diline göre')).toBeInTheDocument();
  });

  it('saves the language and switches the screens to it', async () => {
    render();

    await userEvent.click(screen.getByLabelText('Dil'));
    await userEvent.click(await screen.findByTitle('English'));
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() => expect(changeCulture).toHaveBeenCalledWith('en-US'));
    expect(updatePreferences).toHaveBeenCalledWith({ data: { culture: 'en-US' } });
  });

  it('clears the preference to follow the device', async () => {
    render();

    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() => expect(updatePreferences).toHaveBeenCalledWith({ data: { culture: null } }));
    expect(changeCulture).not.toHaveBeenCalled();
  });
});
