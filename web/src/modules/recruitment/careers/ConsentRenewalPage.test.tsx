import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { RespondToConsentRenewalCommand } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { ConsentRenewalPage } from './ConsentRenewalPage';

const respond = vi.fn<(tenant: string, token: string, body: RespondToConsentRenewalCommand) => Promise<void>>();

vi.mock('@/shared/api/generated/endpoints/consents/consents', () => ({
  useGetConsentRenewalPage: () => ({
    data: {
      firstName: 'Ayşe',
      text: 'Özgeçmişimin aday havuzunda saklanmasına izin veriyorum.',
      revision: 2,
      retentionEndsOn: '2026-11-29',
    },
    isError: false,
  }),
  respondToConsentRenewal: (tenant: string, token: string, body: RespondToConsentRenewalCommand) =>
    respond(tenant, token, body),
}));

const texts: Record<string, string> = {
  'web.recruitment.consent_renewal.intro': 'Sayın {name}, bilgileriniz {date} tarihine kadar saklanacak.',
  'web.recruitment.consent_renewal.accept': 'Rızamı yeniliyorum',
  'web.recruitment.consent_renewal.renewed': 'Rızanız yenilendi',
};

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <Routes>
        <Route path="/careers/:tenant/consents/:token" element={<ConsentRenewalPage />} />
      </Routes>
    </QueryClientProvider>,
    texts,
    '/careers/acme/consents/tok123',
  );

describe('ConsentRenewalPage', () => {
  beforeEach(() => respond.mockReset().mockResolvedValue(undefined));

  it('shows the text and renews the consent', async () => {
    render();

    expect(
      await screen.findByText('Sayın Ayşe, bilgileriniz 29 Kas 2026 tarihine kadar saklanacak.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Özgeçmişimin aday havuzunda saklanmasına izin veriyorum.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Rızamı yeniliyorum' }));

    await waitFor(() => expect(respond).toHaveBeenCalledWith('acme', 'tok123', { accepted: true }));
    expect(await screen.findByText('Rızanız yenilendi')).toBeInTheDocument();
  });
});
