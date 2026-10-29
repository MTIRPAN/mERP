import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ApplicationCvAnalysisItem } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { ApplicationCvAnalysisCard } from './ApplicationCvAnalysisCard';

const analyze = vi.fn<(id: string) => Promise<void>>().mockResolvedValue(undefined);

vi.mock('@/shared/api/generated/endpoints/recruitment/recruitment', () => ({
  analyzeApplicationCv: (id: string) => analyze(id),
  getGetApplicationQueryKey: (id: string) => [`/api/recruitment/applications/${id}`],
}));

const failed: ApplicationCvAnalysisItem = {
  succeeded: false,
  response: null,
  errorCode: 'common.errors.ai_budget_exceeded',
  analyzedAt: '2026-10-29T09:00:00Z',
  requestedByName: null,
};

const texts: Record<string, string> = {
  'web.recruitment.pipeline.cv_analysis.title': 'Özgeçmiş analizi (yapay zeka)',
  'web.recruitment.pipeline.cv_analysis.again': 'Yeniden analiz et',
  'web.recruitment.pipeline.cv_analysis.failed': 'Analiz yapılamadı',
  'web.recruitment.pipeline.cv_analysis.background': 'otomatik analiz',
  'web.recruitment.pipeline.cv_analysis.when': '{at} · {by}',
  'web.recruitment.pipeline.cv_analysis.none_background': 'Henüz analiz yok.',
  'common.errors.ai_budget_exceeded': 'Bu ayın yapay zeka bütçesi doldu.',
};

const render = (analysis: ApplicationCvAnalysisItem | null, canAnalyze: boolean) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <ApplicationCvAnalysisCard applicationId="a1" analysis={analysis} canAnalyze={canAnalyze} />
    </QueryClientProvider>,
    texts,
  );

describe('ApplicationCvAnalysisCard', () => {
  it('explains a failed background run and runs it again', async () => {
    render(failed, true);

    expect(screen.getByText('Analiz yapılamadı')).toBeInTheDocument();
    expect(screen.getByText('Bu ayın yapay zeka bütçesi doldu.')).toBeInTheDocument();
    expect(screen.getByText(/otomatik analiz/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Yeniden analiz et/ }));

    await waitFor(() => expect(analyze).toHaveBeenCalledWith('a1'));
  });

  it('offers no run to those who may not', () => {
    render(null, false);

    expect(screen.getByText('Henüz analiz yok.')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
