import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AnalyzeCvMutationVariables } from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { CvAnalysisResponse } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { CvAnalysisPage } from './CvAnalysisPage';

const mutateAsync = vi.fn<(variables: AnalyzeCvMutationVariables) => Promise<CvAnalysisResponse>>();

vi.mock('@/shared/api/generated/endpoints/recruitment/recruitment', () => ({
  useAnalyzeCv: () => ({ mutateAsync, isPending: false }),
}));

const texts: Record<string, string> = {
  'web.recruitment.cv_analysis.form.submit': 'Analiz et',
  'web.recruitment.cv_analysis.form.file_required': 'Bir özgeçmiş dosyası seçin.',
  'web.recruitment.cv_analysis.form.job_requirements': 'Gereksinimler',
  'web.recruitment.cv_analysis.empty': 'Sonuçlar burada görünecek',
  'web.recruitment.cv_analysis.notice.title': 'Yapay zeka önerisidir',
  'web.recruitment.cv_analysis.notice.redactions': 'Çıkarılanlar: {items}.',
  'web.recruitment.cv_analysis.redaction.email': 'e-posta ({count})',
  'web.recruitment.cv_analysis.redaction.name': 'ad ({count})',
  'web.recruitment.cv_analysis.years': '{years} yıl deneyim',
  'web.recruitment.cv_analysis.seniority.senior': 'Kıdemli',
  'web.recruitment.cv_analysis.recommendation.match': 'Uygun',
  'web.recruitment.cv_analysis.requirement_status.met': 'Karşılıyor',
  'web.recruitment.cv_analysis.requirement_status.partial': 'Kısmen',
};

const response: CvAnalysisResponse = {
  analysis: {
    summary: 'Ödeme sistemlerinde deneyimli backend geliştirici.',
    currentTitle: 'Senior Software Engineer',
    totalExperienceYears: 8.2,
    seniority: 'Senior',
    experiences: [],
    education: [],
    skills: [],
    languages: [],
    certifications: [],
    match: {
      score: 82,
      recommendation: 'Match',
      summary: 'Zorunlu gereksinimleri karşılıyor.',
      requirements: [
        { requirement: '5+ yıl .NET', status: 'Met', evidence: '8 yıl .NET' },
        { requirement: 'Kubernetes', status: 'Partial', evidence: 'Taşıma projesi' },
      ],
      strengths: ['Ödeme alanı bilgisi'],
      gaps: ['Kubernetes işletimi'],
    },
    interviewQuestions: [],
    clarifications: [],
  },
  redactions: [
    { kind: 'email', count: 1 },
    { kind: 'name', count: 2 },
  ],
  documentTruncated: false,
  model: 'claude-opus-5-5',
  promptVersion: '2026-10-05',
};

describe('CvAnalysisPage', () => {
  beforeEach(() => mutateAsync.mockReset());

  it('requires a file before analyzing', async () => {
    renderWithProviders(<CvAnalysisPage />, texts);

    await userEvent.click(screen.getByRole('button', { name: /Analiz et/ }));

    expect(await screen.findByText('Bir özgeçmiş dosyası seçin.')).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
    expect(screen.getByText('Sonuçlar burada görünecek')).toBeInTheDocument();
  });

  it('sends the file with the requirements and shows the analysis', async () => {
    mutateAsync.mockResolvedValue(response);
    const { container } = renderWithProviders(<CvAnalysisPage />, texts);
    const file = new File(['%PDF-1.7'], 'cv.pdf', { type: 'application/pdf' });

    await userEvent.upload(container.querySelector<HTMLInputElement>('input[type="file"]')!, file);
    await userEvent.type(screen.getByLabelText(/Gereksinimler/), '5+ yıl .NET');
    await userEvent.click(screen.getByRole('button', { name: /Analiz et/ }));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    const data = mutateAsync.mock.calls[0]![0].data;
    expect(data.file).toBe(file);
    expect(data.jobRequirements).toBe('5+ yıl .NET');
    expect(data.jobTitle).toBeUndefined();

    expect(await screen.findByText('Ödeme sistemlerinde deneyimli backend geliştirici.')).toBeInTheDocument();
    expect(screen.getByText('Yapay zeka önerisidir')).toBeInTheDocument();
    expect(screen.getByText('Çıkarılanlar: e-posta (1), ad (2).')).toBeInTheDocument();
    expect(screen.getByText('8,2 yıl deneyim')).toBeInTheDocument();
    expect(screen.getByText('Uygun')).toBeInTheDocument();
    expect(screen.getByText('Karşılıyor')).toBeInTheDocument();
    expect(screen.getByText('Kubernetes işletimi')).toBeInTheDocument();
  });
});
