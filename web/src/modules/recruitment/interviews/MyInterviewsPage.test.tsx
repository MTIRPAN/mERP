import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type {
  FeedbackSummaryResponse,
  InterviewDetails,
  InterviewListItem,
  InterviewQuestionsResponse,
  MeResponse,
  SubmitFeedbackCommand,
  UpdateInterviewCommand,
} from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { InterviewModal } from './InterviewModal';
import { MyInterviewsPage } from './MyInterviewsPage';

const submit = vi.fn<(id: string, body: SubmitFeedbackCommand) => Promise<{ id: string }>>();
const update = vi.fn<(id: string, body: UpdateInterviewCommand) => Promise<void>>();
const suggest = vi.fn<(id: string) => Promise<InterviewQuestionsResponse>>();
const summarize = vi.fn<(id: string) => Promise<FeedbackSummaryResponse>>();

const interview: InterviewListItem = {
  id: 'i1',
  applicationId: 'a1',
  candidateId: 'c1',
  candidateName: 'Ayşe Yılmaz',
  openingId: 'o1',
  openingTitle: 'Yazılım Geliştirici',
  companyId: 'co1',
  type: 'Technical',
  startsAt: '2026-11-05T07:00:00.000Z',
  endsAt: '2026-11-05T08:00:00.000Z',
  location: null,
  onlineLink: 'https://meet.example/abc',
  status: 'Scheduled',
  interviewers: [
    { userId: 'u1', name: 'Zeynep Ak' },
    { userId: 'u2', name: 'Can Demir' },
  ],
  feedbackCount: 1,
  myFeedbackSent: false,
};

const details: InterviewDetails = {
  interview,
  notes: 'Projeleri sorun',
  cancelReason: null,
  organizer: { userId: 'u9', name: 'İK Uzmanı' },
  evaluation: {
    sections: [
      {
        title: 'Teknik',
        criteria: [
          {
            id: 'k1',
            name: 'Problem çözme',
            scoreDescriptions: ['Yetersiz', 'Zayıf', 'Yeterli', 'İyi', 'Çok iyi'],
            weight: 3,
          },
        ],
      },
    ],
    questions: [{ id: 'q1', text: 'En güçlü yanı ne?' }],
  },
  myFeedback: null,
  feedback: [],
  feedbackHidden: true,
  applicationScore: null,
  cvFileName: null,
  canManage: false,
  canSubmitFeedback: true,
  version: 3,
};

/** The interview after every form is in: the reader sees them all (D52). */
const reviewed: InterviewDetails = {
  ...details,
  interview: { ...interview, feedbackCount: 2, myFeedbackSent: true },
  feedback: [
    {
      id: 'f1',
      interviewId: 'i1',
      interviewerUserId: 'u1',
      interviewerName: 'Zeynep Ak',
      recommendation: 'Yes',
      weightedScore: 4,
      scores: [{ criterionId: 'k1', criterion: 'Problem çözme', score: 4, comment: null }],
      answers: [],
      summary: null,
      submittedAt: '2026-11-05T09:00:00.000Z',
    },
  ],
  feedbackHidden: false,
  canSubmitFeedback: false,
  feedbackPending: 1,
};

let shown = details;

vi.mock('@/shared/api/generated/endpoints/recruitment/recruitment', () => ({
  useListInterviews: () => ({ data: { items: [interview], page: 1, pageSize: 20, totalCount: 1 }, isLoading: false }),
  useGetInterview: () => ({ data: shown, refetch: () => Promise.resolve() }),
  useSearchInterviewUsers: () => ({ data: [], isFetching: false }),
  useListInterviewTemplates: () => ({ data: [], isLoading: false }),
  useListInterviewConflicts: () => ({
    data: [
      {
        userId: 'u1',
        name: 'Zeynep Ak',
        interviewId: 'i2',
        startsAt: '2026-11-05T07:30:00.000Z',
        endsAt: '2026-11-05T08:30:00.000Z',
      },
    ],
  }),
  submitInterviewFeedback: (id: string, body: SubmitFeedbackCommand) => submit(id, body),
  updateInterview: (id: string, body: UpdateInterviewCommand) => update(id, body),
  scheduleInterview: vi.fn(),
  cancelInterview: vi.fn(),
  closeInterview: vi.fn(),
  downloadInterviewCv: vi.fn(),
  suggestInterviewQuestions: (id: string) => suggest(id),
  summarizeInterviewFeedback: (id: string) => summarize(id),
  getListInterviewsQueryKey: () => ['/api/recruitment/interviews'],
  getGetInterviewQueryKey: (id: string) => [`/api/recruitment/interviews/${id}`],
  getGetApplicationQueryKey: (id: string) => [`/api/recruitment/applications/${id}`],
}));

const texts: Record<string, string> = {
  'web.recruitment.interviews.feedback.hidden': 'Kör değerlendirme: kendi formunuzu gönderin.',
  'web.recruitment.interviews.feedback.criterion': '{name} (ağırlık {weight})',
  'web.recruitment.interviews.feedback.send': 'Formu gönder',
  'web.recruitment.interviews.feedback.recommendation': 'Genel tavsiye',
  'web.recruitment.interviews.recommendations.Yes': 'Evet',
  'web.recruitment.interviews.conflicts': 'Mülakatçıların bu saatte başka mülakatı var',
  'web.recruitment.interviews.send_update': 'Kaydet ve güncelleme gönder',
  'web.recruitment.interviews.preview.subject': 'Mülakat daveti: {position}',
  'web.recruitment.interviews.ai.questions.suggest': 'Soru öner',
  'web.recruitment.interviews.ai.questions.look_for': 'Güçlü bir yanıtta',
  'web.recruitment.interviews.ai.summary.create': 'Özet hazırla',
  'web.recruitment.interviews.ai.summary.pending': '{count} form bekleniyor',
  'web.recruitment.interviews.ai.summary.strengths': 'Güçlü yönler',
};

/** A profile with the interview AI features on. */
const withAi = {
  ai: { enabled: true, features: ['recruitment.interview_questions', 'recruitment.feedback_summary'] },
} as unknown as MeResponse;

const render = (ui: ReactElement, me?: MeResponse) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={['recruitment.interviews.read', 'recruitment.feedback.submit']}>
          {ui}
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('MyInterviewsPage', () => {
  beforeEach(() => {
    submit.mockReset().mockResolvedValue({ id: 'f1' });
    update.mockReset().mockResolvedValue(undefined);
    shown = details;
  });

  it('suggests questions, and keeps the summary until every form is in', async () => {
    suggest.mockReset().mockResolvedValue({
      questions: [
        {
          criterionId: 'k1',
          criterion: 'Problem çözme',
          question: 'Zor bir hatayı nasıl çözdünüz?',
          lookFor: 'Adım adım ayırır.',
          followUps: [],
        },
      ],
      model: 'claude-sonnet-5-5',
      promptVersion: '2026-10-31',
    });
    shown = reviewed;
    render(<MyInterviewsPage />, withAi);

    await userEvent.click(await screen.findByText('Ayşe Yılmaz'));
    const drawer = await screen.findByRole('dialog');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Soru öner' }));
    expect(await within(drawer).findByText('Zor bir hatayı nasıl çözdünüz?')).toBeInTheDocument();
    expect(within(drawer).getByText('Güçlü bir yanıtta: Adım adım ayırır.')).toBeInTheDocument();
    expect(suggest).toHaveBeenCalledWith('i1');

    // One form is missing: the summary waits (the blind rule)
    expect(within(drawer).getByText('1 form bekleniyor')).toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: 'Özet hazırla' })).toBeDisabled();
  });

  it('summarizes the forms once every form is in', async () => {
    summarize.mockReset().mockResolvedValue({
      overview: 'Mülakatçılar analitik beceride hemfikir.',
      strengths: [{ text: 'Analitik düşünce güçlü.', interviewers: ['Zeynep Ak'] }],
      concerns: [],
      disagreements: [],
      openQuestions: [],
      formCount: 2,
      model: 'claude-sonnet-5-5',
      promptVersion: '2026-10-31',
    });
    shown = { ...reviewed, feedbackPending: 0 };
    render(<MyInterviewsPage />, withAi);

    await userEvent.click(await screen.findByText('Ayşe Yılmaz'));
    const drawer = await screen.findByRole('dialog');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Özet hazırla' }));
    expect(await within(drawer).findByText('Mülakatçılar analitik beceride hemfikir.')).toBeInTheDocument();
    expect(within(drawer).getByText('Güçlü yönler')).toBeInTheDocument();
    expect(summarize).toHaveBeenCalledWith('i1');
  });

  it('shows no AI help when the tenant has it off', async () => {
    shown = reviewed;
    render(<MyInterviewsPage />);

    await userEvent.click(await screen.findByText('Ayşe Yılmaz'));
    const drawer = await screen.findByRole('dialog');
    expect(within(drawer).queryByRole('button', { name: 'Soru öner' })).not.toBeInTheDocument();
    expect(within(drawer).queryByRole('button', { name: 'Özet hazırla' })).not.toBeInTheDocument();
  });

  it('lists my interviews and sends my form with the blind rule in place', async () => {
    render(<MyInterviewsPage />);

    await userEvent.click(await screen.findByText('Ayşe Yılmaz'));

    const drawer = await screen.findByRole('dialog');
    expect(within(drawer).getByText('Kör değerlendirme: kendi formunuzu gönderin.')).toBeInTheDocument();
    expect(within(drawer).getByText('Problem çözme (ağırlık 3)')).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('radio', { name: '4' }).closest('label')!);
    expect(within(drawer).getByText('İyi')).toBeInTheDocument();
    await userEvent.type(within(drawer).getByRole('textbox', { name: 'En güçlü yanı ne?' }), 'Analitik');
    await userEvent.click(within(drawer).getByRole('radio', { name: 'Evet' }).closest('label')!);
    await userEvent.click(within(drawer).getByRole('button', { name: 'Formu gönder' }));

    await waitFor(() =>
      expect(submit).toHaveBeenCalledWith('i1', {
        scores: [{ criterionId: 'k1', score: 4, comment: null }],
        answers: [{ questionId: 'q1', text: 'Analitik' }],
        recommendation: 'Yes',
        summary: null,
      }),
    );
  });
});

describe('InterviewModal', () => {
  beforeEach(() => update.mockReset().mockResolvedValue(undefined));

  it('warns about the interviewers’ other interviews and sends the update', async () => {
    render(
      <InterviewModal
        applicationId="a1"
        candidateName="Ayşe Yılmaz"
        openingTitle="Yazılım Geliştirici"
        interview={details}
        onClose={() => undefined}
      />,
    );

    expect(await screen.findByText('Mülakatçıların bu saatte başka mülakatı var')).toBeInTheDocument();
    expect(screen.getByText('Mülakat daveti: Yazılım Geliştirici')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet ve güncelleme gönder' }));

    await waitFor(() =>
      expect(update).toHaveBeenCalledWith('i1', {
        version: 3,
        type: 'Technical',
        startsAt: '2026-11-05T07:00:00.000Z',
        endsAt: '2026-11-05T08:00:00.000Z',
        location: null,
        onlineLink: 'https://meet.example/abc',
        interviewerUserIds: ['u1', 'u2'],
        notes: 'Projeleri sorun',
      }),
    );
  });
});
