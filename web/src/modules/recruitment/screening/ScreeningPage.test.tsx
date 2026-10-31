import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { OpeningScreeningView } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { ScreeningPage } from './ScreeningPage';

const request = vi.fn<(id: string) => Promise<{ id: string }>>();
const cancel = vi.fn<(id: string, screeningId: string) => Promise<void>>();

const base: OpeningScreeningView = {
  openingId: 'o1',
  openingTitle: 'Satış Uzmanı',
  screening: null,
  due: 1,
  requirements: ['5 yıl B2B satış', 'İyi derecede İngilizce'],
  rows: [
    {
      applicationId: 'a1',
      candidateId: 'c1',
      candidateName: 'Ayşe Yılmaz',
      appliedAt: '2026-10-30T09:00:00.000Z',
      stage: 'Başvuru',
      state: 'Current',
      analyzedAt: '2026-10-31T00:30:00.000Z',
      cells: [{ requirement: '5 yıl B2B satış', status: 'Met', evidence: 'Beş yıl B2B satış' }],
    },
    {
      applicationId: 'a2',
      candidateId: 'c2',
      candidateName: 'Mehmet Kaya',
      appliedAt: '2026-10-31T09:00:00.000Z',
      stage: 'Başvuru',
      state: 'NotAnalyzed',
      analyzedAt: null,
      cells: [],
    },
  ],
  enabled: true,
  hasRequirements: true,
  canRequest: true,
};

let view = base;

vi.mock('@/shared/api/generated/endpoints/recruitment/recruitment', () => ({
  useGetOpeningScreening: () => ({ data: view, refetch: () => Promise.resolve() }),
  requestOpeningScreening: (id: string) => request(id),
  cancelOpeningScreening: (id: string, screeningId: string) => cancel(id, screeningId),
}));

const texts: Record<string, string> = {
  'web.recruitment.screening.title': '{position}: toplu ön tarama',
  'web.recruitment.screening.request': 'Toplu ön tarama iste',
  'web.recruitment.screening.cancel': 'İptal et',
  'web.recruitment.screening.cancel_confirm': 'İptal edilsin mi?',
  'web.recruitment.screening.due': '{count} başvurunun güncel analizi yok.',
  'web.recruitment.screening.requirements.Met': 'Karşılıyor',
  'web.recruitment.screening.states.NotAnalyzed': 'Analiz yok',
  'web.recruitment.screening.statuses.Held': 'Bekletildi',
  'web.recruitment.screening.hold_reasons.ai_budget_exceeded': 'Bütçe yetmedi.',
};

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <Routes>
        <Route path="/recruitment/openings/:id/screening" element={<ScreeningPage />} />
      </Routes>
    </QueryClientProvider>,
    texts,
    '/recruitment/openings/o1/screening',
  );

describe('ScreeningPage', () => {
  beforeEach(() => {
    request.mockReset().mockResolvedValue({ id: 's1' });
    cancel.mockReset().mockResolvedValue(undefined);
    view = base;
  });

  it('shows the evidence by requirement in the order the applications came, and asks for a screening', async () => {
    render();

    expect(await screen.findByText('Satış Uzmanı: toplu ön tarama')).toBeInTheDocument();
    expect(screen.getByText('1 başvurunun güncel analizi yok.')).toBeInTheDocument();
    const rows = screen.getAllByRole('row').slice(1);
    expect(within(rows[0]!).getByText('Ayşe Yılmaz')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('Karşılıyor')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Mehmet Kaya')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Analiz yok')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'İyi derecede İngilizce' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Toplu ön tarama iste' }));
    await waitFor(() => expect(request).toHaveBeenCalledWith('o1'));
  });

  it('shows why a screening was held and lets it be called off', async () => {
    view = {
      ...base,
      canRequest: false,
      screening: {
        id: 's1',
        status: 'Held',
        requestedAt: '2026-10-31T10:00:00.000Z',
        requestedByName: 'İK Uzmanı',
        submittedAt: null,
        running: 0,
        estimatedCostUsd: null,
        holdReason: 'common.errors.ai_budget_exceeded',
        succeeded: 0,
        failed: 0,
        completedAt: null,
        canCancel: true,
      },
    };
    render();

    expect(await screen.findByText('Bekletildi')).toBeInTheDocument();
    expect(screen.getByText('Bütçe yetmedi.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Toplu ön tarama iste' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'İptal et' }));
    await userEvent.click(await screen.findByRole('button', { name: 'OK' }));
    await waitFor(() => expect(cancel).toHaveBeenCalledWith('o1', 's1'));
  });
});
