import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { FunnelRow, InterviewerRow, RecruitmentSummary } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { RecruitmentReportsPage } from './RecruitmentReportsPage';

const summaryParams = vi.fn();

const summary: RecruitmentSummary = {
  applications: 4,
  hires: 2,
  averageDaysToHire: 20.5,
  averageDaysToFill: 32,
  offerAcceptanceRate: 66.7,
  openRequisitions: 2,
  missingForms: 1,
};

const stage = (
  order: number,
  stageName: string,
  stageType: FunnelRow['stageType'],
  reached: number,
  conversionRate: number | null,
): FunnelRow => ({
  id: `o1-${order}`,
  openingId: 'o1',
  openingTitle: 'Satış Uzmanı',
  stageOrder: order,
  stageName,
  stageType,
  reached,
  conversionRate,
  current: 0,
});

const funnel = [
  stage(1, 'Başvuru', 'Applied', 4, null),
  stage(2, 'Ön eleme', 'Screening', 3, 75),
  stage(3, 'İşe alındı', 'Hired', 1, 33.3),
  stage(4, 'Reddedildi', 'Rejected', 1, null),
];

const interviewer: InterviewerRow = {
  userId: 'u1',
  name: null,
  interviews: 2,
  completed: 2,
  formsSent: 1,
  formsMissing: 1,
  averageFeedbackHours: 14,
};

const page = <T,>(items: T[]) => Promise.resolve({ items, page: 1, pageSize: 20, totalCount: items.length });

vi.mock('echarts-for-react/lib/core', () => ({
  default: ({ option }: { option: { xAxis: { data?: string[] }; yAxis: { data?: string[] } } }) => (
    <div data-testid="chart">{(option.yAxis.data ?? option.xAxis.data ?? []).join(', ')}</div>
  ),
}));

vi.mock('@/app/me/meContext', () => ({ useMe: () => ({ companies: [{ id: 'c1', name: 'Acme' }] }) }));

vi.mock('@/shared/api/generated/endpoints/recruitment/recruitment', () => ({
  useGetRecruitmentSummary: (params: unknown) => {
    summaryParams(params);
    return { data: summary };
  },
  useGetRecruitmentFunnelReport: () => ({ data: { items: funnel } }),
  useGetRecruitmentSourceReport: () => ({ data: { items: [] } }),
  useGetRequisitionAgingReport: () => ({ data: { items: [] } }),
  useGetRecruitmentOfferReport: () => ({ data: { items: [] } }),
  getRecruitmentFunnelReport: () => page(funnel),
  getRecruitmentInterviewerReport: () => page([interviewer]),
  getGetRecruitmentFunnelReportQueryKey: () => ['/api/recruitment/reports/funnel'],
  getGetRecruitmentHireReportQueryKey: () => ['/api/recruitment/reports/hires'],
  getGetRecruitmentSourceReportQueryKey: () => ['/api/recruitment/reports/sources'],
  getGetRecruitmentInterviewerReportQueryKey: () => ['/api/recruitment/reports/interviewers'],
  getGetRequisitionAgingReportQueryKey: () => ['/api/recruitment/reports/requisition-aging'],
  getGetRecruitmentOfferReportQueryKey: () => ['/api/recruitment/reports/offers'],
}));

const texts: Record<string, string> = {
  'web.recruitment.reports.summary.average_days_to_hire': 'Ort. işe alım süresi (gün)',
  'web.recruitment.reports.summary.offer_acceptance_rate': 'Teklif kabul oranı',
  'web.recruitment.reports.funnel.title': 'İlan hunisi',
  'web.recruitment.reports.interviewers.title': 'Mülakatçı yükü',
  'web.recruitment.reports.unknown_user': 'Bilinmeyen kullanıcı',
};

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={['recruitment.reports.read']}>
        <RecruitmentReportsPage />
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    '/recruitment/reports',
  );

describe('RecruitmentReportsPage', () => {
  it('shows the figures of the last three months and the funnel of the working stages', async () => {
    render();

    const hire = screen.getByText('Ort. işe alım süresi (gün)').closest('.ant-statistic') as HTMLElement;
    expect(within(hire).getByText('20,5')).toBeInTheDocument();
    expect(screen.getByText('%66,7')).toBeInTheDocument();
    expect(summaryParams).toHaveBeenCalledWith(
      expect.objectContaining({ from: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/) as unknown }),
    );
    // The final stages other than Hired stay out of the chart; the first stage is on top
    expect(screen.getByTestId('chart')).toHaveTextContent('İşe alındı, Ön eleme, Başvuru');
    expect(await screen.findByText('%75')).toBeInTheDocument();
  });

  it('lists the interviewers with their missing forms', async () => {
    render();

    await userEvent.click(screen.getByRole('tab', { name: 'Mülakatçı yükü' }));

    expect(await screen.findByText('Bilinmeyen kullanıcı')).toBeInTheDocument();
    expect(screen.getByText('14')).toBeInTheDocument();
  });
});
