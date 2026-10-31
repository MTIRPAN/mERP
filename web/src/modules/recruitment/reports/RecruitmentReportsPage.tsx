import { Card, Col, DatePicker, Empty, Flex, Row, Select, Space, Statistic, Tabs, Tag, Typography, theme } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useMe } from '@/app/me/meContext';
import {
  exportRecruitmentFunnelReport,
  exportRecruitmentHireReport,
  exportRecruitmentInterviewerReport,
  exportRecruitmentOfferReport,
  exportRecruitmentSourceReport,
  exportRequisitionAgingReport,
  getGetRecruitmentFunnelReportQueryKey,
  getGetRecruitmentHireReportQueryKey,
  getGetRecruitmentInterviewerReportQueryKey,
  getGetRecruitmentOfferReportQueryKey,
  getGetRecruitmentSourceReportQueryKey,
  getGetRequisitionAgingReportQueryKey,
  getRecruitmentFunnelReport,
  getRecruitmentHireReport,
  getRecruitmentInterviewerReport,
  getRecruitmentOfferReport,
  getRecruitmentSourceReport,
  getRequisitionAgingReport,
  useGetRecruitmentFunnelReport,
  useGetRecruitmentOfferReport,
  useGetRecruitmentSourceReport,
  useGetRecruitmentSummary,
  useGetRequisitionAgingReport,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type {
  FunnelRow,
  HireRow,
  InterviewerRow,
  OfferReportRow,
  RequisitionAgingRow,
  SourceRow,
} from '@/shared/api/generated/model';
import { useListExport } from '@/shared/api/listExport';
import { MAX_PAGE_SIZE, type ListParams } from '@/shared/api/paging';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatMomentDate } from '@/shared/format/date';
import { recruitmentPermissions } from '../permissions';
import { ReportChart } from './ReportChart';

type ReportKey = 'funnel' | 'hires' | 'sources' | 'interviewers' | 'requisition-aging' | 'offers';

const reports: ReportKey[] = ['funnel', 'hires', 'sources', 'interviewers', 'requisition-aging', 'offers'];

const buckets = ['0-30', '31-60', '61-90', '90+'];

/** The filters every report of the page shares: a period and a company in scope. */
interface Filters {
  from: string;
  to: string;
  companyId?: string;
}

const day = (value: Dayjs) => value.format('YYYY-MM-DD');

const number = (value: number | null | undefined, language: string) =>
  value == null ? '—' : value.toLocaleString(language, { maximumFractionDigits: 1 });

const percent = (value: number | null | undefined, language: string) =>
  value == null ? '—' : `%${value.toLocaleString(language, { maximumFractionDigits: 1 })}`;

const prefix = 'web.recruitment.reports';

const useCommon = () => {
  const { t, i18n } = useTranslation();
  const download = useListExport();
  return { t, language: i18n.language, download };
};

/** The headline figures of the period. */
const Summary = ({ filters }: { filters: Filters }) => {
  const { t, language } = useCommon();
  const { data } = useGetRecruitmentSummary(filters);
  const items: { key: string; value: string }[] = [
    { key: 'applications', value: number(data?.applications, language) },
    { key: 'hires', value: number(data?.hires, language) },
    { key: 'average_days_to_hire', value: number(data?.averageDaysToHire, language) },
    { key: 'average_days_to_fill', value: number(data?.averageDaysToFill, language) },
    { key: 'offer_acceptance_rate', value: percent(data?.offerAcceptanceRate, language) },
    { key: 'open_requisitions', value: number(data?.openRequisitions, language) },
    { key: 'missing_forms', value: number(data?.missingForms, language) },
  ];
  return (
    <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
      {items.map((item) => (
        <Col key={item.key} xs={12} md={8} xl={6} xxl={3}>
          <Card size="small">
            <Statistic title={t(`${prefix}.summary.${item.key}`)} value={item.value} />
          </Card>
        </Col>
      ))}
    </Row>
  );
};

const ChartCard = ({ children, empty }: { children: ReactNode; empty: boolean }) => (
  <Card size="small" style={{ marginBottom: 16 }}>
    {empty ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /> : children}
  </Card>
);

const FunnelReport = ({ filters }: { filters: Filters }) => {
  const { t, language, download } = useCommon();
  const [openingId, setOpeningId] = useState<string>();
  const { data } = useGetRecruitmentFunnelReport({ ...filters, pageSize: MAX_PAGE_SIZE });
  const openings = useMemo(
    () => [...new Map((data?.items ?? []).map((r) => [r.openingId, r.openingTitle])).entries()],
    [data],
  );
  const chosen = openingId && openings.some(([id]) => id === openingId) ? openingId : openings[0]?.[0];
  const stages = (data?.items ?? []).filter(
    (r) => r.openingId === chosen && r.stageType !== 'Rejected' && r.stageType !== 'Withdrawn',
  );
  const columns = useMemo<DataTableColumn<FunnelRow>[]>(
    () => [
      { key: 'openingTitle', dataIndex: 'openingTitle', title: t(`${prefix}.columns.opening`) },
      { key: 'stageName', dataIndex: 'stageName', title: t(`${prefix}.columns.stage`) },
      { key: 'reached', dataIndex: 'reached', title: t(`${prefix}.columns.reached`), align: 'right' },
      {
        key: 'conversionRate',
        title: t(`${prefix}.columns.conversion_rate`),
        align: 'right',
        render: (_, r) => percent(r.conversionRate, language),
      },
      { key: 'current', dataIndex: 'current', title: t(`${prefix}.columns.current`), align: 'right' },
    ],
    [t, language],
  );
  return (
    <>
      <ChartCard empty={stages.length === 0}>
        <Flex vertical gap={8}>
          <Select
            aria-label={t(`${prefix}.columns.opening`)}
            value={chosen}
            onChange={setOpeningId}
            options={openings.map(([value, label]) => ({ value, label }))}
            style={{ maxWidth: 360 }}
          />
          <ReportChart
            horizontal
            categories={stages.map((s) => s.stageName)}
            series={[{ name: t(`${prefix}.columns.reached`), data: stages.map((s) => s.reached) }]}
          />
        </Flex>
      </ChartCard>
      <DataTable<FunnelRow>
        queryKey={getGetRecruitmentFunnelReportQueryKey()}
        fetchPage={(list: ListParams) =>
          getRecruitmentFunnelReport({ ...filters, page: list.page, pageSize: list.pageSize })
        }
        columns={columns}
        rowKey="id"
        filter={{ ...filters }}
        onExport={({ columns: keys }) =>
          download(exportRecruitmentFunnelReport({ ...filters, columns: keys.join(',') }), t(`${prefix}.funnel.title`))
        }
      />
    </>
  );
};

const HiresReport = ({ filters }: { filters: Filters }) => {
  const { t, language, download } = useCommon();
  const canOpen = useHasPermission(recruitmentPermissions.applicationsRead);
  const columns = useMemo<DataTableColumn<HireRow>[]>(
    () => [
      { key: 'candidateName', dataIndex: 'candidateName', title: t(`${prefix}.columns.candidate`) },
      {
        key: 'openingTitle',
        title: t(`${prefix}.columns.opening`),
        render: (_, r) =>
          canOpen ? <Link to={`/recruitment/openings/${r.openingId}`}>{r.openingTitle}</Link> : r.openingTitle,
      },
      { key: 'source', dataIndex: 'source', title: t(`${prefix}.columns.source`) },
      {
        key: 'appliedAt',
        title: t(`${prefix}.columns.applied_at`),
        render: (_, r) => formatMomentDate(r.appliedAt, language),
      },
      {
        key: 'hiredAt',
        title: t(`${prefix}.columns.hired_at`),
        render: (_, r) => formatMomentDate(r.hiredAt, language),
      },
      {
        key: 'daysToHire',
        title: t(`${prefix}.columns.days_to_hire`),
        align: 'right',
        render: (_, r) => number(r.daysToHire, language),
      },
      {
        key: 'daysToFill',
        title: t(`${prefix}.columns.days_to_fill`),
        align: 'right',
        render: (_, r) => number(r.daysToFill, language),
      },
    ],
    [t, language, canOpen],
  );
  return (
    <DataTable<HireRow>
      queryKey={getGetRecruitmentHireReportQueryKey()}
      fetchPage={(list: ListParams) =>
        getRecruitmentHireReport({ ...filters, page: list.page, pageSize: list.pageSize })
      }
      columns={columns}
      rowKey="applicationId"
      filter={{ ...filters }}
      onExport={({ columns: keys }) =>
        download(exportRecruitmentHireReport({ ...filters, columns: keys.join(',') }), t(`${prefix}.hires.title`))
      }
    />
  );
};

const SourcesReport = ({ filters }: { filters: Filters }) => {
  const { t, language, download } = useCommon();
  const { token } = theme.useToken();
  const { data } = useGetRecruitmentSourceReport({ ...filters, pageSize: MAX_PAGE_SIZE });
  const rows = data?.items ?? [];
  const columns = useMemo<DataTableColumn<SourceRow>[]>(
    () => [
      { key: 'sourceName', title: t(`${prefix}.columns.source`), render: (_, r) => r.sourceName ?? r.source },
      { key: 'applications', dataIndex: 'applications', title: t(`${prefix}.columns.applications`), align: 'right' },
      { key: 'interviewed', dataIndex: 'interviewed', title: t(`${prefix}.columns.interviewed`), align: 'right' },
      { key: 'offered', dataIndex: 'offered', title: t(`${prefix}.columns.offered`), align: 'right' },
      { key: 'hired', dataIndex: 'hired', title: t(`${prefix}.columns.hired`), align: 'right' },
      {
        key: 'hireRate',
        title: t(`${prefix}.columns.hire_rate`),
        align: 'right',
        render: (_, r) => percent(r.hireRate, language),
      },
    ],
    [t, language],
  );
  return (
    <>
      <ChartCard empty={rows.length === 0}>
        <ReportChart
          categories={rows.map((r) => r.sourceName ?? r.source)}
          series={[
            { name: t(`${prefix}.columns.applications`), data: rows.map((r) => r.applications) },
            { name: t(`${prefix}.columns.interviewed`), data: rows.map((r) => r.interviewed), color: token.colorInfo },
            { name: t(`${prefix}.columns.offered`), data: rows.map((r) => r.offered), color: token.colorWarning },
            { name: t(`${prefix}.columns.hired`), data: rows.map((r) => r.hired), color: token.colorSuccess },
          ]}
        />
      </ChartCard>
      <DataTable<SourceRow>
        queryKey={getGetRecruitmentSourceReportQueryKey()}
        fetchPage={(list: ListParams) =>
          getRecruitmentSourceReport({ ...filters, page: list.page, pageSize: list.pageSize })
        }
        columns={columns}
        rowKey="source"
        filter={{ ...filters }}
        onExport={({ columns: keys }) =>
          download(exportRecruitmentSourceReport({ ...filters, columns: keys.join(',') }), t(`${prefix}.sources.title`))
        }
      />
    </>
  );
};

const InterviewersReport = ({ filters }: { filters: Filters }) => {
  const { t, language, download } = useCommon();
  const columns = useMemo<DataTableColumn<InterviewerRow>[]>(
    () => [
      {
        key: 'name',
        title: t(`${prefix}.columns.interviewer`),
        render: (_, r) => r.name ?? <Typography.Text type="secondary">{t(`${prefix}.unknown_user`)}</Typography.Text>,
      },
      { key: 'interviews', dataIndex: 'interviews', title: t(`${prefix}.columns.interviews`), align: 'right' },
      { key: 'completed', dataIndex: 'completed', title: t(`${prefix}.columns.completed`), align: 'right' },
      { key: 'formsSent', dataIndex: 'formsSent', title: t(`${prefix}.columns.forms_sent`), align: 'right' },
      {
        key: 'formsMissing',
        title: t(`${prefix}.columns.forms_missing`),
        align: 'right',
        render: (_, r) => (r.formsMissing > 0 ? <Tag color="warning">{r.formsMissing}</Tag> : r.formsMissing),
      },
      {
        key: 'averageFeedbackHours',
        title: t(`${prefix}.columns.average_feedback_hours`),
        align: 'right',
        render: (_, r) => number(r.averageFeedbackHours, language),
      },
    ],
    [t, language],
  );
  return (
    <DataTable<InterviewerRow>
      queryKey={getGetRecruitmentInterviewerReportQueryKey()}
      fetchPage={(list: ListParams) =>
        getRecruitmentInterviewerReport({ ...filters, page: list.page, pageSize: list.pageSize })
      }
      columns={columns}
      rowKey="userId"
      filter={{ ...filters }}
      onExport={({ columns: keys }) =>
        download(
          exportRecruitmentInterviewerReport({ ...filters, columns: keys.join(',') }),
          t(`${prefix}.interviewers.title`),
        )
      }
    />
  );
};

const RequisitionAgingReport = ({ companyId }: { companyId?: string }) => {
  const { t, language, download } = useCommon();
  const canOpen = useHasPermission(recruitmentPermissions.requisitionsRead);
  const { data } = useGetRequisitionAgingReport({ companyId, pageSize: MAX_PAGE_SIZE });
  const rows = data?.items ?? [];
  const columns = useMemo<DataTableColumn<RequisitionAgingRow>[]>(
    () => [
      {
        key: 'number',
        title: t(`${prefix}.columns.number`),
        render: (_, r) => (canOpen ? <Link to={`/recruitment/requisitions/${r.id}`}>{r.number}</Link> : r.number),
      },
      { key: 'positionTitle', dataIndex: 'positionTitle', title: t(`${prefix}.columns.position`) },
      { key: 'companyName', dataIndex: 'companyName', title: t(`${prefix}.columns.company`) },
      {
        key: 'status',
        title: t(`${prefix}.columns.status`),
        render: (_, r) => t(`web.recruitment.requisitions.status.${r.status}`),
      },
      {
        key: 'headcount',
        title: t(`${prefix}.columns.headcount`),
        align: 'right',
        render: (_, r) => `${r.filledCount}/${r.headcount}`,
      },
      {
        key: 'ageDays',
        title: t(`${prefix}.columns.age_days`),
        align: 'right',
        render: (_, r) => number(r.ageDays, language),
      },
      { key: 'ageBucket', dataIndex: 'ageBucket', title: t(`${prefix}.columns.age_bucket`) },
    ],
    [t, language, canOpen],
  );
  return (
    <>
      <ChartCard empty={rows.length === 0}>
        <ReportChart
          categories={buckets.map((b) => t(`${prefix}.buckets.${b}`))}
          series={[
            {
              name: t(`${prefix}.columns.count`),
              data: buckets.map((b) => rows.filter((r) => r.ageBucket === b).length),
            },
          ]}
        />
      </ChartCard>
      <DataTable<RequisitionAgingRow>
        queryKey={getGetRequisitionAgingReportQueryKey()}
        fetchPage={(list: ListParams) =>
          getRequisitionAgingReport({ companyId, page: list.page, pageSize: list.pageSize })
        }
        columns={columns}
        rowKey="id"
        filter={{ companyId }}
        onExport={({ columns: keys }) =>
          download(
            exportRequisitionAgingReport({ companyId, columns: keys.join(',') }),
            t(`${prefix}.requisition_aging.title`),
          )
        }
      />
    </>
  );
};

const OffersReport = ({ filters }: { filters: Filters }) => {
  const { t, language, download } = useCommon();
  const { token } = theme.useToken();
  const { data } = useGetRecruitmentOfferReport({ ...filters, pageSize: MAX_PAGE_SIZE });
  const rows = data?.items ?? [];
  const columns = useMemo<DataTableColumn<OfferReportRow>[]>(
    () => [
      { key: 'openingTitle', dataIndex: 'openingTitle', title: t(`${prefix}.columns.opening`) },
      { key: 'sent', dataIndex: 'sent', title: t(`${prefix}.columns.sent`), align: 'right' },
      { key: 'accepted', dataIndex: 'accepted', title: t(`${prefix}.columns.accepted`), align: 'right' },
      { key: 'declined', dataIndex: 'declined', title: t(`${prefix}.columns.declined`), align: 'right' },
      { key: 'expired', dataIndex: 'expired', title: t(`${prefix}.columns.expired`), align: 'right' },
      { key: 'waiting', dataIndex: 'waiting', title: t(`${prefix}.columns.waiting`), align: 'right' },
      {
        key: 'acceptanceRate',
        title: t(`${prefix}.columns.acceptance_rate`),
        align: 'right',
        render: (_, r) => percent(r.acceptanceRate, language),
      },
    ],
    [t, language],
  );
  return (
    <>
      <ChartCard empty={rows.length === 0}>
        <ReportChart
          horizontal
          stacked
          categories={rows.map((r) => r.openingTitle)}
          series={[
            { name: t(`${prefix}.columns.accepted`), data: rows.map((r) => r.accepted), color: token.colorSuccess },
            { name: t(`${prefix}.columns.declined`), data: rows.map((r) => r.declined), color: token.colorError },
            { name: t(`${prefix}.columns.expired`), data: rows.map((r) => r.expired), color: token.colorWarning },
            { name: t(`${prefix}.columns.waiting`), data: rows.map((r) => r.waiting), color: token.colorTextTertiary },
          ]}
        />
      </ChartCard>
      <DataTable<OfferReportRow>
        queryKey={getGetRecruitmentOfferReportQueryKey()}
        fetchPage={(list: ListParams) =>
          getRecruitmentOfferReport({ ...filters, page: list.page, pageSize: list.pageSize })
        }
        columns={columns}
        rowKey="openingId"
        filter={{ ...filters }}
        onExport={({ columns: keys }) =>
          download(exportRecruitmentOfferReport({ ...filters, columns: keys.join(',') }), t(`${prefix}.offers.title`))
        }
      />
    </>
  );
};

/**
 * Recruitment reports (PLAN R9, D56): the headline figures of a period, then the funnel of each opening, the hires
 * with their durations, the sources, the interviewers, the aging of open requisitions and the offers, each with its
 * chart and Excel file. The open report is kept in the address (<c>?tab=</c>).
 */
export const RecruitmentReportsPage = () => {
  const { t } = useTranslation();
  const me = useMe();
  const [params, setParams] = useSearchParams();
  const requested = params.get('tab') as ReportKey | null;
  const tab = requested && reports.includes(requested) ? requested : 'funnel';
  const [range, setRange] = useState<[Dayjs, Dayjs]>([dayjs().subtract(3, 'month').add(1, 'day'), dayjs()]);
  const [companyId, setCompanyId] = useState<string>();
  const filters: Filters = { from: day(range[0]), to: day(range[1]), companyId };

  const views: Record<ReportKey, () => ReactNode> = {
    funnel: () => <FunnelReport filters={filters} />,
    hires: () => <HiresReport filters={filters} />,
    sources: () => <SourcesReport filters={filters} />,
    interviewers: () => <InterviewersReport filters={filters} />,
    'requisition-aging': () => <RequisitionAgingReport companyId={companyId} />,
    offers: () => <OffersReport filters={filters} />,
  };

  return (
    <>
      <PageHeader title={t(`${prefix}.title`)} subtitle={t(`${prefix}.subtitle`)} />
      <Space wrap style={{ marginBottom: 16 }}>
        <DatePicker.RangePicker
          aria-label={t(`${prefix}.period`)}
          value={range}
          onChange={(value) => value?.[0] && value[1] && setRange([value[0], value[1]])}
          format="L"
          allowClear={false}
        />
        <Select
          allowClear
          aria-label={t(`${prefix}.company`)}
          placeholder={t(`${prefix}.all_companies`)}
          value={companyId}
          onChange={setCompanyId}
          options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
          style={{ minWidth: 200 }}
        />
      </Space>
      <Summary filters={filters} />
      <Tabs
        activeKey={tab}
        destroyOnHidden
        onChange={(key) => setParams({ tab: key }, { replace: true })}
        items={reports.map((key) => ({
          key,
          label: t(`${prefix}.${key.replaceAll('-', '_')}.title`),
          children: views[key](),
        }))}
      />
    </>
  );
};
