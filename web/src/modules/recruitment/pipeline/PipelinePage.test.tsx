import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import dayjs from 'dayjs';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  ApplicationDetails,
  ApplicationListItem,
  OpeningPipeline,
  RejectApplicationCommand,
} from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { HiringPipelinePage, PipelinePage } from './PipelinePage';
import { isOverdue } from './pipeline';

const reject = vi.fn<(id: string, body: RejectApplicationCommand) => Promise<void>>().mockResolvedValue(undefined);

const pipeline: OpeningPipeline = {
  openingId: 'o1',
  title: 'Satış Uzmanı',
  companyId: 'c1',
  companyName: 'Acme',
  status: 'Published',
  stages: [
    { id: 's1', name: 'Başvuru', type: 'Applied', slaDays: 3, count: 1 },
    { id: 's2', name: 'Ön eleme', type: 'Screening', slaDays: 5, count: 1 },
    { id: 's3', name: 'İşe alındı', type: 'Hired', slaDays: null, count: 0 },
    { id: 's4', name: 'Reddedildi', type: 'Rejected', slaDays: null, count: 0 },
    { id: 's5', name: 'Geri çekildi', type: 'Withdrawn', slaDays: null, count: 0 },
  ],
  canManage: true,
  rejectionReasons: [{ code: 'SKILLS', name: 'Yetkinlikler uyuşmuyor' }],
  withdrawalReasons: [{ code: 'OTHER_OFFER', name: 'Başka bir teklifi kabul etti' }],
};

const application = (
  id: string,
  name: string,
  stageId: string,
  extra: Partial<ApplicationListItem> = {},
): ApplicationListItem => ({
  id,
  candidateId: `c-${id}`,
  candidateName: name,
  email: `${id}@ornek.test`,
  phone: null,
  openingId: 'o1',
  openingTitle: 'Satış Uzmanı',
  companyId: 'c1',
  stageId,
  stageName: pipeline.stages.find((s) => s.id === stageId)?.name ?? null,
  stageType: pipeline.stages.find((s) => s.id === stageId)?.type ?? null,
  status: 'Active',
  knockedOut: false,
  isInternal: false,
  source: 'CAREER_PAGE',
  appliedAt: '2026-10-20T09:00:00Z',
  stageEnteredAt: '2026-10-20T09:00:00Z',
  slaDueAt: null,
  ...extra,
});

const ayse = application('a1', 'Ayşe Yılmaz', 's1', { knockedOut: true });
const mehmet = application('a2', 'Mehmet Kaya', 's2', { isInternal: true });

const details: ApplicationDetails = {
  application: ayse,
  coverLetter: null,
  answers: [
    { questionId: 'q1', question: 'Askerlik görevinizi tamamladınız mı?', type: 'YesNo', values: ['no'], missed: true },
  ],
  cv: { fileName: 'ozgecmis.pdf', contentType: 'application/pdf', length: 1200 },
  consent: { disclosureRevision: 2, culture: 'tr-TR', poolConsentRevision: null },
  reasonCode: null,
  reasonName: null,
  rejectionEmailDueAt: null,
  rejectionEmailSentAt: null,
  history: [
    {
      fromStageId: null,
      fromStageName: null,
      toStageId: 's1',
      toStageName: 'Başvuru',
      status: 'Active',
      reasonCode: null,
      reasonName: null,
      note: null,
      userId: null,
      userName: null,
      at: '2026-10-20T09:00:00Z',
    },
  ],
  canManage: true,
  version: 7,
};

let teamDetails = false;

vi.mock('@/shared/api/generated/endpoints/recruitment/recruitment', () => ({
  useGetOpeningPipeline: () => ({ data: pipeline }),
  useGetMyHiringOpeningPipeline: () => ({
    data: { ...pipeline, canManage: false, rejectionReasons: [], withdrawalReasons: [] },
  }),
  useListApplications: () => ({ data: { items: [ayse, mehmet], page: 1, pageSize: 200, totalCount: 2 } }),
  useListMyHiringApplications: () => ({ data: { items: [ayse, mehmet], page: 1, pageSize: 200, totalCount: 2 } }),
  useGetApplication: () => ({ data: teamDetails ? undefined : details }),
  useGetMyHiringApplication: () => ({ data: teamDetails ? { ...details, canManage: false } : undefined }),
  getApplication: () => Promise.resolve(details),
  rejectApplication: (id: string, body: RejectApplicationCommand) => reject(id, body),
  moveApplication: vi.fn(),
  withdrawApplication: vi.fn(),
  reopenApplication: vi.fn(),
  listApplications: vi.fn(),
  listMyHiringApplications: vi.fn(),
  downloadApplicationCv: vi.fn(),
  downloadMyHiringApplicationCv: vi.fn(),
  getGetOpeningPipelineQueryKey: (id: string) => [`/api/recruitment/openings/${id}/pipeline`],
  getListApplicationsQueryKey: () => ['/api/recruitment/applications'],
  getListMyHiringApplicationsQueryKey: () => ['/api/recruitment/me/hiring/applications'],
  getListOpeningsQueryKey: () => ['/api/recruitment/openings'],
  getGetApplicationQueryKey: (id: string) => [`/api/recruitment/applications/${id}`],
}));

const texts: Record<string, string> = {
  'web.recruitment.pipeline.knocked_out': 'Elemeye takıldı',
  'web.recruitment.pipeline.internal': 'İç aday',
  'web.recruitment.pipeline.reject.ok': 'Reddet',
  'web.recruitment.pipeline.reject.title': '{name} başvurusunu reddet',
  'web.recruitment.pipeline.reject.send_email': 'Adaya ret e-postası gönder',
  'web.recruitment.pipeline.reject.when': 'Ne zaman',
  'web.recruitment.pipeline.reject.after_days': '{days} gün sonra',
  'web.recruitment.pipeline.fields.reason': 'Neden',
  'web.recruitment.pipeline.history': 'Aşama geçmişi',
  'web.recruitment.pipeline.applied': 'Başvuru yapıldı: {stage}',
  'web.recruitment.pipeline.team_hint': 'Bu ilanın işe alım ekibindesiniz.',
  'web.recruitment.pipeline.download_cv': 'Özgeçmişi indir ({name})',
  'web.recruitment.openings.answers.no': 'Hayır',
};

const render = (route: string) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <Routes>
        <Route path="/recruitment/openings/:id/pipeline" element={<PipelinePage />} />
        <Route path="/recruitment/hiring/:id" element={<HiringPipelinePage />} />
      </Routes>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('PipelinePage', () => {
  beforeEach(() => {
    reject.mockClear();
    teamDetails = false;
  });

  it('shows the applications by stage and rejects one with the e-mail two days later', async () => {
    render('/recruitment/openings/o1/pipeline');

    expect(await screen.findByText('Ön eleme')).toBeInTheDocument();
    expect(screen.getByText('Mehmet Kaya')).toBeInTheDocument();
    expect(screen.getByText('İç aday')).toBeInTheDocument();
    await userEvent.click(screen.getByText('Ayşe Yılmaz'));

    const drawer = await screen.findByRole('dialog');
    expect(within(drawer).getByText('Başvuru yapıldı: Başvuru')).toBeInTheDocument();
    expect(within(drawer).getByText('Hayır')).toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: /Özgeçmişi indir/ })).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('button', { name: 'Reddet' }));

    const title = await screen.findByText('Ayşe Yılmaz başvurusunu reddet');
    const modal = title.closest('.ant-modal') as HTMLElement;
    const selects = within(modal).getAllByRole('combobox');
    await userEvent.click(selects[0]!);
    await userEvent.click(await screen.findByText('Yetkinlikler uyuşmuyor'));
    await userEvent.click(selects[1]!);
    await userEvent.click(await screen.findByText('2 gün sonra'));
    await userEvent.click(within(modal).getByRole('button', { name: 'Reddet' }));

    await waitFor(() =>
      expect(reject).toHaveBeenCalledWith('a1', {
        version: 7,
        reasonCode: 'SKILLS',
        note: null,
        sendEmail: true,
        emailDelayDays: 2,
      }),
    );
  });

  it('lets the hiring team read the pipeline without changing it', async () => {
    teamDetails = true;
    render('/recruitment/hiring/o1');

    expect(await screen.findByText('Bu ilanın işe alım ekibindesiniz.')).toBeInTheDocument();
    await userEvent.click(screen.getByText('Mehmet Kaya'));

    const drawer = await screen.findByRole('dialog');
    expect(within(drawer).getByText('Aşama geçmişi')).toBeInTheDocument();
    expect(within(drawer).queryByRole('button', { name: 'Reddet' })).not.toBeInTheDocument();
  });
});

describe('isOverdue', () => {
  it('flags an active application past its stage SLA', () => {
    const now = dayjs('2026-10-29T09:00:00Z');
    expect(isOverdue(application('x', 'X', 's1', { slaDueAt: '2026-10-28T09:00:00Z' }), now)).toBe(true);
    expect(isOverdue(application('x', 'X', 's1', { slaDueAt: '2026-10-30T09:00:00Z' }), now)).toBe(false);
    expect(isOverdue(application('x', 'X', 's1'), now)).toBe(false);
  });
});
