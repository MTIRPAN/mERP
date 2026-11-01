import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { DecideApprovalTaskMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { ApprovalRequestDetails, ApprovalSummaryResponse, MeResponse } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { ApprovalRequestView } from './ApprovalRequestView';

const decide = vi.fn<(variables: DecideApprovalTaskMutationVariables) => Promise<void>>();
const summarize = vi.fn<(id: string) => Promise<ApprovalSummaryResponse>>();
let details: ApprovalRequestDetails;

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetApprovalRequest: () => ({ data: details, isLoading: false }),
  useDecideApprovalTask: () => ({ mutateAsync: decide, isPending: false }),
  useForceApproveApprovalRequest: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useReassignApprovalTask: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useAssignApprovers: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useSearchApprovalUsers: () => ({ data: [], isFetching: false }),
  summarizeApprovalRequest: (id: string) => summarize(id),
  getGetApprovalRequestQueryKey: (id: string) => [`/api/platform/approvals/requests/${id}`],
  getGetApprovalTimelineQueryKey: () => ['/api/platform/approvals/timeline'],
  getListMyApprovalTasksQueryKey: () => ['/api/platform/approvals/my-tasks'],
  getCountMyApprovalTasksQueryKey: () => ['/api/platform/approvals/my-tasks/count'],
  getListApprovalRequestsQueryKey: () => ['/api/platform/approvals/requests'],
}));

const texts: Record<string, string> = {
  'web.approvals.decision.approve': 'Onayla',
  'web.approvals.decision.reject': 'Reddet',
  'web.approvals.decision.return': 'İade et',
  'web.approvals.decision.comment': 'Açıklama',
  'web.approvals.decision.comment_required': 'Ret ve iade için açıklama yazın.',
  'web.approvals.decision.on_behalf': 'Bu kararı {name} adına vekaleten veriyorsunuz.',
  'web.approvals.admin.title': 'Yönetici işlemleri',
  'web.approvals.status.Pending': 'Onay bekliyor',
  'web.approvals.summary.create': 'Özetle',
  'web.approvals.summary.attention': 'Dikkat noktaları',
};

const me = {
  user: { id: 'me' },
  cultures: [],
  companies: [],
  ai: { enabled: false, features: [] },
} as unknown as MeResponse;
const meWithAi = { ...me, ai: { enabled: true, features: ['platform.approval_summary'] } };

const request = (assignee: string, allowReturn = false): ApprovalRequestDetails => ({
  id: 'r1',
  documentType: 'hr.leave-request',
  documentTypeName: 'İzin talebi',
  documentId: '42',
  companyId: null,
  title: 'Yıllık izin',
  summary: '7 gün',
  link: null,
  requesterUserId: 'requester',
  requesterName: 'Ali Veli',
  status: 'Pending',
  round: 1,
  currentStep: 0,
  submittedAt: '2026-10-12T08:00:00Z',
  completedAt: null,
  steps: [{ index: 0, name: 'Yönetici', mode: 'Any', allowReturn, dueHours: null }],
  tasks: [
    {
      id: 't1',
      round: 1,
      step: 0,
      assigneeUserId: assignee,
      assigneeName: 'Mehmet Demir',
      status: 'Pending',
      decidedByUserId: null,
      decidedByName: null,
      onBehalfOfUserId: null,
      onBehalfOfName: null,
      comment: null,
      createdAt: '2026-10-12T08:00:00Z',
      dueAt: null,
      decidedAt: null,
    },
  ],
  history: [],
  myTaskId: 't1',
  canAdminister: false,
});

const render = (profile: MeResponse = me) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={profile}>
        <ApprovalRequestView requestId="r1" />
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('ApprovalRequestView', () => {
  beforeEach(() => {
    decide.mockReset().mockResolvedValue();
    details = request('me');
  });

  it('asks for a comment before rejecting', async () => {
    render();

    await userEvent.click(screen.getByRole('button', { name: /Reddet/ }));
    expect(screen.getByText('Ret ve iade için açıklama yazın.')).toBeInTheDocument();
    expect(decide).not.toHaveBeenCalled();

    await userEvent.type(screen.getByLabelText('Açıklama'), 'Bütçe yok');
    await userEvent.click(screen.getByRole('button', { name: /Reddet/ }));
    await waitFor(() =>
      expect(decide).toHaveBeenCalledWith({ id: 't1', data: { decision: 'Reject', comment: 'Bütçe yok' } }),
    );
  });

  it('approves without a comment and offers return only where the step allows it', async () => {
    render();
    expect(screen.queryByRole('button', { name: /İade et/ })).not.toBeInTheDocument();
    expect(screen.queryByText('Yönetici işlemleri')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Özetle' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /Onayla/ }));
    await waitFor(() =>
      expect(decide).toHaveBeenCalledWith({ id: 't1', data: { decision: 'Approve', comment: null } }),
    );
  });

  it('summarizes the request for its approver on request', async () => {
    summarize.mockReset().mockResolvedValue({
      summary: '7 günlük yıllık izin talebi ilk onay adımında.',
      attentionPoints: ['Talep beş günden uzun.'],
      model: 'claude-sonnet-5-5',
      promptVersion: '2026-11-01',
    });
    render(meWithAi);

    await userEvent.click(screen.getByRole('button', { name: 'Özetle' }));

    expect(await screen.findByText('7 günlük yıllık izin talebi ilk onay adımında.')).toBeInTheDocument();
    expect(screen.getByText('Dikkat noktaları')).toBeInTheDocument();
    expect(screen.getByText('Talep beş günden uzun.')).toBeInTheDocument();
    expect(summarize).toHaveBeenCalledWith('r1');
  });

  it('offers no summary to someone who does not decide the request', () => {
    details = { ...request('someone-else'), myTaskId: null };
    render(meWithAi);

    expect(screen.queryByRole('button', { name: 'Özetle' })).not.toBeInTheDocument();
  });

  it('tells a delegate whose task they decide', () => {
    details = request('mehmet', true);
    render();

    expect(screen.getByText('Bu kararı Mehmet Demir adına vekaleten veriyorsunuz.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /İade et/ })).toBeInTheDocument();
  });
});
