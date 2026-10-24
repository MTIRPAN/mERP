import { DatePicker, Input, InputNumber, Segmented, Select, Space, Switch, Tabs, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useMe } from '@/app/me/meContext';
import {
  exportLeaveBalanceReport,
  exportLeaveUsageReport,
  exportLongTimeNoLeaveReport,
  exportUpcomingLeavesReport,
  getGetLeaveBalanceReportQueryKey,
  getGetLeaveUsageReportQueryKey,
  getGetLongTimeNoLeaveReportQueryKey,
  getGetUpcomingLeavesReportQueryKey,
  getLeaveBalanceReport,
  getLeaveUsageReport,
  getLongTimeNoLeaveReport,
  getUpcomingLeavesReport,
} from '@/shared/api/generated/endpoints/hr/hr';
import type {
  LeaveBalanceReportRow,
  LeaveRequestListItem,
  LeaveUsageReportRow,
  LongTimeNoLeaveRow,
} from '@/shared/api/generated/model';
import { useListExport } from '@/shared/api/listExport';
import type { ListParams } from '@/shared/api/paging';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDate } from '@/shared/format/date';
import { hrPermissions } from '../../permissions';
import { LeaveRequestStatusTag } from '../requests/LeaveRequestStatusTag';

type ReportKey = 'balances' | 'usage' | 'upcoming' | 'long-time-no-leave';

const reports: ReportKey[] = ['balances', 'usage', 'upcoming', 'long-time-no-leave'];

const day = (value: Dayjs) => value.format('YYYY-MM-DD');

const number = (value: number | null | undefined, language: string) =>
  value == null ? '—' : value.toLocaleString(language);

const date = (value: string | null | undefined, language: string) => (value ? formatDate(value, language) : '—');

/** The person's name, linked to the card for those who read employees. */
const Person = ({ id, name, canOpen }: { id: string; name: string; canOpen: boolean }) =>
  canOpen ? <Link to={`/hr/employees/${id}`}>{name}</Link> : <>{name}</>;

/** Company filter shared by the reports: every company in scope, or one. */
const CompanyFilter = ({ value, onChange }: { value?: string; onChange: (value?: string) => void }) => {
  const { t } = useTranslation();
  const me = useMe();
  return (
    <Select
      allowClear
      aria-label={t('web.hr.leave_reports.company')}
      placeholder={t('web.hr.leave_reports.all_companies')}
      value={value}
      onChange={onChange}
      options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
      style={{ minWidth: 200 }}
    />
  );
};

const useCommon = () => {
  const { t, i18n } = useTranslation();
  const canOpenEmployees = useHasPermission(hrPermissions.employeesRead);
  const download = useListExport();
  return { t, i18n, canOpenEmployees, download };
};

const BalancesReport = () => {
  const { t, i18n, canOpenEmployees, download } = useCommon();
  const [asOf, setAsOf] = useState<Dayjs>(dayjs());
  const [companyId, setCompanyId] = useState<string>();
  const [search, setSearch] = useState<string>();
  const columns = useMemo<DataTableColumn<LeaveBalanceReportRow>[]>(
    () => [
      {
        key: 'registryNumber',
        dataIndex: 'registryNumber',
        title: t('web.hr.leave_reports.columns.registry_number'),
        sorter: true,
        width: 110,
      },
      {
        key: 'employeeName',
        title: t('web.hr.leave_reports.columns.employee'),
        sorter: true,
        render: (_, r) => <Person id={r.employeeId} name={r.employeeName} canOpen={canOpenEmployees} />,
      },
      { key: 'companyName', dataIndex: 'companyName', title: t('web.hr.leave_reports.columns.company') },
      { key: 'leaveTypeName', dataIndex: 'leaveTypeName', title: t('web.hr.leave_reports.columns.leave_type') },
      {
        key: 'earned',
        title: t('web.hr.leave_reports.columns.earned'),
        sorter: true,
        align: 'right',
        render: (_, r) => number(r.earned, i18n.language),
      },
      {
        key: 'used',
        title: t('web.hr.leave_reports.columns.used'),
        sorter: true,
        align: 'right',
        render: (_, r) => number(r.used, i18n.language),
      },
      {
        key: 'balance',
        title: t('web.hr.leave_reports.columns.balance'),
        sorter: true,
        align: 'right',
        render: (_, r) => (
          <Typography.Text type={r.balance < 0 ? 'danger' : undefined}>
            {number(r.balance, i18n.language)}
          </Typography.Text>
        ),
      },
    ],
    [t, i18n.language, canOpenEmployees],
  );
  const fetchPage = (list: ListParams) =>
    getLeaveBalanceReport({
      asOf: list.filter!.asOf!,
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      companyId: list.filter?.companyId,
      search: list.filter?.search,
    });
  return (
    <DataTable<LeaveBalanceReportRow>
      queryKey={getGetLeaveBalanceReportQueryKey()}
      fetchPage={fetchPage}
      columns={columns}
      rowKey="employmentId"
      filter={{ asOf: day(asOf), companyId, search }}
      onExport={({ sort, filter, columns: keys }) =>
        download(
          exportLeaveBalanceReport({
            asOf: filter!.asOf!,
            columns: keys.join(','),
            sort,
            companyId: filter?.companyId,
            search: filter?.search,
          }),
          t('web.hr.leave_reports.balances.title'),
        )
      }
      toolbar={
        <Space wrap>
          <DatePicker
            aria-label={t('web.hr.leave_reports.as_of')}
            value={asOf}
            onChange={(value) => value && setAsOf(value)}
            format="L"
            allowClear={false}
          />
          <CompanyFilter value={companyId} onChange={setCompanyId} />
          <Input.Search
            allowClear
            placeholder={t('web.hr.leave_reports.search')}
            onSearch={(v) => setSearch(v.trim() || undefined)}
            style={{ width: 240 }}
          />
        </Space>
      }
    />
  );
};

const UsageReport = () => {
  const { t, i18n, canOpenEmployees, download } = useCommon();
  const [range, setRange] = useState<[Dayjs, Dayjs]>([dayjs().startOf('year'), dayjs()]);
  const [companyId, setCompanyId] = useState<string>();
  const [search, setSearch] = useState<string>();
  const columns = useMemo<DataTableColumn<LeaveUsageReportRow>[]>(
    () => [
      {
        key: 'registryNumber',
        dataIndex: 'registryNumber',
        title: t('web.hr.leave_reports.columns.registry_number'),
        width: 110,
      },
      {
        key: 'employeeName',
        title: t('web.hr.leave_reports.columns.employee'),
        render: (_, r) => <Person id={r.employeeId} name={r.employeeName} canOpen={canOpenEmployees} />,
      },
      { key: 'companyName', dataIndex: 'companyName', title: t('web.hr.leave_reports.columns.company') },
      {
        key: 'leaveTypeCode',
        title: t('web.hr.leave_reports.columns.leave_type'),
        sorter: true,
        render: (_, r) => r.leaveTypeName,
      },
      {
        key: 'requests',
        dataIndex: 'requests',
        title: t('web.hr.leave_reports.columns.requests'),
        sorter: true,
        align: 'right',
      },
      {
        key: 'days',
        title: t('web.hr.leave_reports.columns.days'),
        sorter: true,
        align: 'right',
        render: (_, r) => number(r.days, i18n.language),
      },
    ],
    [t, i18n.language, canOpenEmployees],
  );
  const fetchPage = (list: ListParams) =>
    getLeaveUsageReport({
      from: list.filter!.from!,
      to: list.filter!.to!,
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      companyId: list.filter?.companyId,
      search: list.filter?.search,
    });
  return (
    <DataTable<LeaveUsageReportRow>
      queryKey={getGetLeaveUsageReportQueryKey()}
      fetchPage={fetchPage}
      columns={columns}
      rowKey="employmentId"
      filter={{ from: day(range[0]), to: day(range[1]), companyId, search }}
      onExport={({ sort, filter, columns: keys }) =>
        download(
          exportLeaveUsageReport({
            from: filter!.from!,
            to: filter!.to!,
            columns: keys.join(','),
            sort,
            companyId: filter?.companyId,
            search: filter?.search,
          }),
          t('web.hr.leave_reports.usage.title'),
        )
      }
      toolbar={
        <Space wrap>
          <DatePicker.RangePicker
            aria-label={t('web.hr.leave_reports.period')}
            value={range}
            onChange={(value) => value?.[0] && value[1] && setRange([value[0], value[1]])}
            format="L"
            allowClear={false}
          />
          <CompanyFilter value={companyId} onChange={setCompanyId} />
          <Input.Search
            allowClear
            placeholder={t('web.hr.leave_reports.search')}
            onSearch={(v) => setSearch(v.trim() || undefined)}
            style={{ width: 240 }}
          />
        </Space>
      }
    />
  );
};

const UpcomingReport = () => {
  const { t, i18n, canOpenEmployees, download } = useCommon();
  const canOpenRequests = useHasPermission(hrPermissions.leaveRequestsRead);
  const [days, setDays] = useState(30);
  const [includePending, setIncludePending] = useState(false);
  const [companyId, setCompanyId] = useState<string>();
  const columns = useMemo<DataTableColumn<LeaveRequestListItem>[]>(
    () => [
      {
        key: 'number',
        title: t('web.hr.leave_reports.columns.number'),
        sorter: true,
        render: (_, r) => (canOpenRequests ? <Link to={`/hr/leave-requests/${r.id}`}>{r.number}</Link> : r.number),
      },
      {
        key: 'employeeName',
        title: t('web.hr.leave_reports.columns.employee'),
        render: (_, r) => <Person id={r.employeeId} name={r.employeeName} canOpen={canOpenEmployees} />,
      },
      {
        key: 'companyName',
        dataIndex: 'companyName',
        title: t('web.hr.leave_reports.columns.company'),
        hiddenByDefault: true,
      },
      { key: 'leaveTypeName', dataIndex: 'leaveTypeName', title: t('web.hr.leave_reports.columns.leave_type') },
      {
        key: 'startDate',
        title: t('web.hr.leave_reports.columns.start_date'),
        sorter: true,
        render: (_, r) => date(r.startDate, i18n.language),
      },
      {
        key: 'endDate',
        title: t('web.hr.leave_reports.columns.end_date'),
        render: (_, r) => date(r.endDate, i18n.language),
      },
      {
        key: 'days',
        title: t('web.hr.leave_reports.columns.days'),
        align: 'right',
        render: (_, r) => number(r.days, i18n.language),
      },
      {
        key: 'status',
        title: t('web.hr.leave_reports.columns.status'),
        render: (_, r) => <LeaveRequestStatusTag status={r.status} />,
      },
    ],
    [t, i18n.language, canOpenEmployees, canOpenRequests],
  );
  const fetchPage = (list: ListParams) =>
    getUpcomingLeavesReport({
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      days: Number(list.filter?.days),
      companyId: list.filter?.companyId,
      includePending: list.filter?.includePending === 'true',
    });
  return (
    <DataTable<LeaveRequestListItem>
      queryKey={getGetUpcomingLeavesReportQueryKey()}
      fetchPage={fetchPage}
      columns={columns}
      rowKey="id"
      filter={{ days: String(days), companyId, includePending: String(includePending) }}
      onExport={({ sort, filter, columns: keys }) =>
        download(
          exportUpcomingLeavesReport({
            columns: keys.join(','),
            sort,
            days: Number(filter?.days),
            companyId: filter?.companyId,
            includePending: filter?.includePending === 'true',
          }),
          t('web.hr.leave_reports.upcoming.title'),
        )
      }
      toolbar={
        <Space wrap>
          <Segmented<number>
            value={days}
            onChange={setDays}
            options={[7, 30, 90].map((value) => ({
              value,
              label: t('web.hr.leave_reports.next_days', { days: value }),
            }))}
          />
          <CompanyFilter value={companyId} onChange={setCompanyId} />
          <Space>
            <Switch
              size="small"
              checked={includePending}
              onChange={setIncludePending}
              aria-label={t('web.hr.leave_reports.include_pending')}
            />
            {t('web.hr.leave_reports.include_pending')}
          </Space>
        </Space>
      }
    />
  );
};

const LongTimeNoLeaveReport = () => {
  const { t, i18n, canOpenEmployees, download } = useCommon();
  const [months, setMonths] = useState(12);
  const [companyId, setCompanyId] = useState<string>();
  const [search, setSearch] = useState<string>();
  const columns = useMemo<DataTableColumn<LongTimeNoLeaveRow>[]>(
    () => [
      {
        key: 'registryNumber',
        dataIndex: 'registryNumber',
        title: t('web.hr.leave_reports.columns.registry_number'),
        width: 110,
      },
      {
        key: 'employeeName',
        title: t('web.hr.leave_reports.columns.employee'),
        sorter: true,
        render: (_, r) => <Person id={r.employeeId} name={r.employeeName} canOpen={canOpenEmployees} />,
      },
      { key: 'companyName', dataIndex: 'companyName', title: t('web.hr.leave_reports.columns.company') },
      { key: 'orgUnitName', dataIndex: 'orgUnitName', title: t('web.hr.leave_reports.columns.org_unit') },
      {
        key: 'hireDate',
        title: t('web.hr.leave_reports.columns.hire_date'),
        sorter: true,
        render: (_, r) => date(r.hireDate, i18n.language),
      },
      {
        key: 'lastLeaveEnd',
        title: t('web.hr.leave_reports.columns.last_leave_end'),
        sorter: true,
        render: (_, r) => (r.lastLeaveEnd ? date(r.lastLeaveEnd, i18n.language) : t('web.hr.leave_reports.never')),
      },
      {
        key: 'daysSince',
        title: t('web.hr.leave_reports.columns.days_since'),
        align: 'right',
        render: (_, r) => number(r.daysSince, i18n.language),
      },
      {
        key: 'balance',
        title: t('web.hr.leave_reports.columns.balance'),
        sorter: true,
        align: 'right',
        render: (_, r) => number(r.balance, i18n.language),
      },
    ],
    [t, i18n.language, canOpenEmployees],
  );
  const fetchPage = (list: ListParams) =>
    getLongTimeNoLeaveReport({
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      months: Number(list.filter?.months),
      companyId: list.filter?.companyId,
      search: list.filter?.search,
    });
  return (
    <DataTable<LongTimeNoLeaveRow>
      queryKey={getGetLongTimeNoLeaveReportQueryKey()}
      fetchPage={fetchPage}
      columns={columns}
      rowKey="employmentId"
      filter={{ months: String(months), companyId, search }}
      onExport={({ sort, filter, columns: keys }) =>
        download(
          exportLongTimeNoLeaveReport({
            columns: keys.join(','),
            sort,
            months: Number(filter?.months),
            companyId: filter?.companyId,
            search: filter?.search,
          }),
          t('web.hr.leave_reports.long_time_no_leave.title'),
        )
      }
      toolbar={
        <Space wrap>
          <Space>
            <InputNumber
              aria-label={t('web.hr.leave_reports.months')}
              min={1}
              max={120}
              precision={0}
              value={months}
              onChange={(value) => value && setMonths(value)}
              style={{ width: 90 }}
            />
            {t('web.hr.leave_reports.months')}
          </Space>
          <CompanyFilter value={companyId} onChange={setCompanyId} />
          <Input.Search
            allowClear
            placeholder={t('web.hr.leave_reports.search')}
            onSearch={(v) => setSearch(v.trim() || undefined)}
            style={{ width: 240 }}
          />
        </Space>
      }
    />
  );
};

const views: Record<ReportKey, () => ReactNode> = {
  balances: () => <BalancesReport />,
  usage: () => <UsageReport />,
  upcoming: () => <UpcomingReport />,
  'long-time-no-leave': () => <LongTimeNoLeaveReport />,
};

/**
 * Leave reports (PLAN L6, D46): balances as of a day, leave taken in a period, upcoming leaves and people who have
 * not taken leave for months, each exportable to Excel. The open report is kept in the address (<c>?tab=</c>).
 */
export const LeaveReportsPage = () => {
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const requested = params.get('tab') as ReportKey | null;
  const tab = requested && reports.includes(requested) ? requested : 'balances';

  return (
    <>
      <PageHeader title={t('web.hr.leave_reports.title')} subtitle={t('web.hr.leave_reports.subtitle')} />
      <Tabs
        activeKey={tab}
        destroyOnHidden
        onChange={(key) => setParams({ tab: key }, { replace: true })}
        items={reports.map((key) => ({
          key,
          label: t(`web.hr.leave_reports.${key.replaceAll('-', '_')}.title`),
          children: views[key](),
        }))}
      />
    </>
  );
};
