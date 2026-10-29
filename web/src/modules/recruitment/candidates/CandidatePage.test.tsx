import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { CandidateDetails, MergeCandidatesCommand } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { CandidatePage, HiringCandidatePage } from './CandidatePage';
import { mentionedIn } from './candidates';

const merge = vi.fn<(id: string, body: MergeCandidatesCommand) => Promise<void>>().mockResolvedValue(undefined);

const candidate: CandidateDetails = {
  id: 'c1',
  email: 'ayse@ornek.test',
  firstName: 'Ayşe',
  lastName: 'Yılmaz',
  phone: '0532 123 45 67',
  culture: 'tr-TR',
  source: 'REFERRAL',
  sourceName: 'Çalışan referansı',
  referrerEmployeeId: 'e1',
  referrerName: 'Mehmet Kaya',
  isEmployee: false,
  profile: {
    location: 'İstanbul',
    currentTitle: 'Satış Uzmanı',
    currentCompany: 'Kuzey Lojistik',
    experienceYears: 6.5,
    education: null,
    linkedInUrl: 'https://linkedin.example/ayse',
    portfolioUrl: null,
  },
  tags: [{ code: 'SALES', name: 'Satış' }],
  poolConsentRevision: null,
  poolConsentAt: null,
  applications: [],
  files: [],
  notes: [
    {
      id: 'n1',
      body: 'Mülakata çağıralım',
      authorUserId: 'u1',
      authorName: 'Zeynep Ak',
      mentioned: [{ userId: 'u2', name: 'Can Demir' }],
      at: '2026-10-28T09:00:00Z',
      canRemove: true,
    },
  ],
  duplicates: [
    {
      id: 'c2',
      fullName: 'Ayşe Nur Yılmaz',
      email: 'ayse.nur@ornek.test',
      phone: '532 123 4567',
      samePhone: true,
      sameName: false,
    },
  ],
  timeline: [],
  canManage: true,
  canAddToOpening: true,
  version: 9,
  evaluations: [
    {
      applicationId: 'a1',
      openingTitle: 'Satış Temsilcisi',
      score: 3.75,
      feedbackCount: 2,
      recommendations: [
        { recommendation: 'Yes', count: 1 },
        { recommendation: 'StrongYes', count: 1 },
      ],
      criteria: [{ criterion: 'İletişim', average: 4.5, count: 2 }],
    },
  ],
};

vi.mock('echarts-for-react/lib/core', () => ({
  default: ({ option }: { option: { yAxis: { data: string[] } } }) => (
    <div data-testid="chart">{option.yAxis.data.join(', ')}</div>
  ),
}));

vi.mock('@/app/me/meContext', () => ({ useMe: () => ({ companies: [{ id: 'co1', name: 'Acme' }] }) }));

vi.mock('@/shared/api/generated/endpoints/recruitment/recruitment', () => ({
  useGetCandidate: () => ({ data: candidate }),
  useGetMyHiringCandidate: () => ({ data: { ...candidate, canManage: false, canAddToOpening: false, duplicates: [] } }),
  useSearchCandidateMentionUsers: () => ({ data: [] }),
  useSearchMyHiringMentionUsers: () => ({ data: [] }),
  mergeCandidates: (id: string, body: MergeCandidatesCommand) => merge(id, body),
  uploadCandidateCv: vi.fn(),
  downloadCandidateCv: vi.fn(),
  addCandidateNote: vi.fn(),
  addMyHiringCandidateNote: vi.fn(),
  removeCandidateNote: vi.fn(),
  removeMyHiringCandidateNote: vi.fn(),
  getGetCandidateQueryKey: (id: string) => [`/api/recruitment/candidates/${id}`],
  getGetMyHiringCandidateQueryKey: (id: string) => [`/api/recruitment/me/hiring/candidates/${id}`],
  getListCandidatesQueryKey: () => ['/api/recruitment/candidates'],
}));

const texts: Record<string, string> = {
  'web.recruitment.candidates.referred_by': '{source} · {name}',
  'web.recruitment.candidates.years': '{years} yıl',
  'web.recruitment.candidates.duplicates_title': 'Bu aday başka bir kayıtla aynı kişi olabilir',
  'web.recruitment.candidates.merge': 'Bu adayla birleştir',
  'web.recruitment.candidates.merge_confirm': '{name} kaydı bu adaya taşınsın mı?',
  'web.recruitment.candidates.same_phone': 'Aynı telefon',
  'web.recruitment.candidates.tabs.notes': 'Notlar ({count})',
  'web.recruitment.candidates.tabs.evaluations': 'Değerlendirmeler ({count})',
  'web.recruitment.interviews.recommendations.Yes': 'Evet',
  'web.recruitment.candidates.add_to_opening': 'İlana ekle',
  'web.recruitment.candidates.upload_cv': 'Özgeçmiş yükle',
  'web.common.edit': 'Düzenle',
};

const render = (route: string, permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <Routes>
          <Route path="/recruitment/candidates/:id" element={<CandidatePage />} />
          <Route path="/recruitment/hiring/candidates/:id" element={<HiringCandidatePage />} />
        </Routes>
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('CandidatePage', () => {
  beforeEach(() => merge.mockClear());

  it('shows the profile and merges a possible duplicate into the candidate', async () => {
    render('/recruitment/candidates/c1', ['recruitment.candidates.read', 'recruitment.candidates.manage']);

    expect(await screen.findByText('Çalışan referansı · Mehmet Kaya')).toBeInTheDocument();
    expect(screen.getByText('6,5 yıl')).toBeInTheDocument();
    expect(screen.getByText('Satış')).toBeInTheDocument();
    expect(screen.getByText('Aynı telefon')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Bu adayla birleştir' }));
    await userEvent.click(await screen.findByRole('button', { name: /OK|Tamam/ }));

    await waitFor(() => expect(merge).toHaveBeenCalledWith('c1', { version: 9, duplicateId: 'c2' }));
  });

  it('shows the evaluations of the interviewers with the average of each criterion', async () => {
    render('/recruitment/candidates/c1', ['recruitment.candidates.read']);

    await userEvent.click(await screen.findByRole('tab', { name: 'Değerlendirmeler (1)' }));

    expect(await screen.findByText('Satış Temsilcisi')).toBeInTheDocument();
    expect(screen.getByText('3,75')).toBeInTheDocument();
    expect(screen.getByText(/Evet: 1/)).toBeInTheDocument();
    expect(screen.getByTestId('chart')).toHaveTextContent('İletişim');
  });

  it('lets the hiring team read the card without managing it', async () => {
    render('/recruitment/hiring/candidates/c1', ['recruitment.self.hiring']);

    expect(await screen.findByText('Notlar (1)')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Düzenle' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'İlana ekle' })).not.toBeInTheDocument();
    expect(screen.queryByText('Bu aday başka bir kayıtla aynı kişi olabilir')).not.toBeInTheDocument();
  });
});

describe('mentionedIn', () => {
  it('keeps only the users whose @name is still in the note', () => {
    const picked = new Map([
      ['Can Demir', 'u2'],
      ['Zeynep Ak', 'u3'],
    ]);
    expect(mentionedIn('@Can Demir ile görüşelim', picked)).toEqual(['u2']);
    expect(mentionedIn('kimse anılmadı', picked)).toEqual([]);
  });
});
