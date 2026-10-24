import { FileAddOutlined, PlusOutlined, TeamOutlined } from '@ant-design/icons';
import { Button, DatePicker, Input, Select, Space, Typography } from 'antd';
import type { Dayjs } from 'dayjs';
import { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { getListLeaveRequestsQueryKey, listLeaveRequests } from '@/shared/api/generated/endpoints/hr/hr';
import type { LeaveRequestListItem, LeaveRequestStatus } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDateTime } from '@/shared/format/date';
import { hrPermissions } from '../../permissions';
import { BulkLeaveDrawer } from './BulkLeaveDrawer';
import { LeaveRequestDetailDrawer } from './LeaveRequestDetailDrawer';
import { LeaveRequestDrawer } from './LeaveRequestDrawer';
import { LeaveRequestStatusTag } from './LeaveRequestStatusTag';
import { leaveRequestStatuses, lengthText, periodText } from './leaveRequests';

type Form = 'none' | 'onBehalf' | 'record' | 'bulk';

/** Statuses HR chooses from; drafts stay with their requesters. */
const hrStatuses = leaveRequestStatuses.filter((s) => s !== 'Draft');

/**
 * HR's leave requests (PLAN L4, D44): every request of the employees in scope, managers' those of their team, with
 * filters; a request on a person's behalf, a recorded past leave and a bulk leave. <c>/hr/leave-requests/:id</c>
 * (the approval inbox's link) opens a request.
 */
export const LeaveRequestsPage = () => {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams();
  const activeCompanyId = useActiveCompanyId();
  const canReadEmployees = useHasPermission(hrPermissions.employeesRead);
  const [statuses, setStatuses] = useState<LeaveRequestStatus[]>([]);
  const [search, setSearch] = useState<string>();
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(null);
  const [form, setForm] = useState<Form>('none');

  const open = useCallback(
    (requestId: string | undefined) =>
      void navigate(requestId ? `/hr/leave-requests/${requestId}` : '/hr/leave-requests'),
    [navigate],
  );

  const columns = useMemo<DataTableColumn<LeaveRequestListItem>[]>(
    () => [
      {
        key: 'number',
        title: t('web.hr.leave_requests.fields.number'),
        sorter: true,
        render: (_, row) => <Typography.Link onClick={() => open(row.id)}>{row.number}</Typography.Link>,
      },
      {
        key: 'employee',
        title: t('web.hr.leave_requests.fields.employee'),
        render: (_, row) =>
          canReadEmployees ? <Link to={`/hr/employees/${row.employeeId}`}>{row.employeeName}</Link> : row.employeeName,
      },
      {
        key: 'companyName',
        dataIndex: 'companyName',
        title: t('web.hr.leave_requests.fields.company'),
        hiddenByDefault: true,
      },
      { key: 'leaveTypeName', dataIndex: 'leaveTypeName', title: t('web.hr.leave_requests.fields.type') },
      {
        key: 'startDate',
        title: t('web.hr.leave_requests.fields.period'),
        sorter: true,
        render: (_, row) => periodText(row, t, i18n.language),
      },
      {
        key: 'length',
        title: t('web.hr.leave_requests.fields.length'),
        render: (_, row) => lengthText(row.basis, row.quantity, t, i18n.language),
      },
      {
        key: 'status',
        title: t('web.hr.leave_requests.fields.status'),
        render: (_, row) => <LeaveRequestStatusTag status={row.status} />,
      },
      {
        key: 'origin',
        title: t('web.hr.leave_requests.fields.origin'),
        hiddenByDefault: true,
        render: (_, row) => t(`web.hr.leave_requests.origins.${row.origin}`),
      },
      {
        key: 'submittedAt',
        title: t('web.hr.leave_requests.fields.submitted_at'),
        sorter: true,
        render: (_, row) => (row.submittedAt ? formatDateTime(row.submittedAt, i18n.language) : '—'),
      },
    ],
    [t, i18n.language, open, canReadEmployees],
  );

  const fetchPage = (list: ListParams) =>
    listLeaveRequests({
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      status: list.filter?.status ? (list.filter.status.split(',') as LeaveRequestStatus[]) : undefined,
      search: list.filter?.search,
      from: list.filter?.from,
      to: list.filter?.to,
    });

  return (
    <>
      <PageHeader
        title={t('web.hr.leave_requests.title')}
        subtitle={t('web.hr.leave_requests.subtitle')}
        extra={
          <Space wrap>
            <Can permission={hrPermissions.leaveBulk}>
              <Button icon={<TeamOutlined />} onClick={() => setForm('bulk')}>
                {t('web.hr.leave_requests.bulk.title')}
              </Button>
            </Can>
            <Can permission={hrPermissions.leaveRequestsRecord}>
              <Button icon={<FileAddOutlined />} onClick={() => setForm('record')}>
                {t('web.hr.leave_requests.new_title.record')}
              </Button>
            </Can>
            <Can permission={hrPermissions.leaveRequestsManage}>
              <Button type="primary" icon={<PlusOutlined />} onClick={() => setForm('onBehalf')}>
                {t('web.hr.leave_requests.new_title.onBehalf')}
              </Button>
            </Can>
          </Space>
        }
      />
      <DataTable<LeaveRequestListItem>
        // The active company is sent as X-Company-Id; it is part of the key so the list refreshes when it changes
        queryKey={[...getListLeaveRequestsQueryKey(), activeCompanyId]}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{
          status: statuses.length > 0 ? statuses.join(',') : undefined,
          search,
          from: range?.[0]?.format('YYYY-MM-DD'),
          to: range?.[1]?.format('YYYY-MM-DD'),
        }}
        toolbar={
          <Space wrap>
            <Input.Search
              allowClear
              placeholder={t('web.hr.leave_requests.search')}
              onSearch={(value) => setSearch(value.trim() || undefined)}
              style={{ width: 260 }}
            />
            <Select
              mode="multiple"
              allowClear
              aria-label={t('web.hr.leave_requests.fields.status')}
              placeholder={t('web.hr.leave_requests.all_statuses')}
              value={statuses}
              onChange={setStatuses}
              options={hrStatuses.map((s) => ({ value: s, label: t(`web.hr.leave_requests.status.${s}`) }))}
              style={{ minWidth: 220 }}
            />
            <DatePicker.RangePicker
              format="L"
              allowEmpty={[true, true]}
              value={range}
              onChange={(value) => setRange(value)}
            />
          </Space>
        }
      />
      {(form === 'onBehalf' || form === 'record') && (
        <LeaveRequestDrawer mode={form} onClose={() => setForm('none')} onSaved={(saved) => open(saved)} />
      )}
      {form === 'bulk' && <BulkLeaveDrawer onClose={() => setForm('none')} />}
      {id && <LeaveRequestDetailDrawer mode="hr" id={id} onClose={() => open(undefined)} />}
    </>
  );
};
