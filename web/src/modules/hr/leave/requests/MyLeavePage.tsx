import { PlusOutlined } from '@ant-design/icons';
import { Button, Card, Empty, Flex, Select, Skeleton, Typography } from 'antd';
import { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import {
  getListMyLeaveRequestsQueryKey,
  listMyLeaveRequests,
  useListMyLeaveBalances,
} from '@/shared/api/generated/endpoints/hr/hr';
import type { LeaveRequestListItem, LeaveRequestStatus } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDateTime } from '@/shared/format/date';
import { BalanceCard } from '../balances/LeaveBalancesTab';
import { hrPermissions } from '../../permissions';
import { LeaveRequestDetailDrawer } from './LeaveRequestDetailDrawer';
import { LeaveRequestDrawer } from './LeaveRequestDrawer';
import { LeaveRequestStatusTag } from './LeaveRequestStatusTag';
import { leaveRequestStatuses, lengthText, periodText } from './leaveRequests';

/**
 * "İzinlerim" (PLAN L4, D44): the user's balances and leave requests, a new request with its live check, and a
 * request's details with its approval timeline. A notification opens a request with <c>?request=</c>.
 */
export const MyLeavePage = () => {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const [asking, setAsking] = useState(false);
  const [statuses, setStatuses] = useState<LeaveRequestStatus[]>([]);
  const balances = useListMyLeaveBalances(undefined, { query: { retry: false, meta: { silent: true } } });
  const opened = params.get('request');

  const open = useCallback(
    (id: string | undefined) =>
      setParams(
        (current) => {
          const next = new URLSearchParams(current);
          if (id) {
            next.set('request', id);
          } else {
            next.delete('request');
          }

          return next;
        },
        { replace: true },
      ),
    [setParams],
  );

  const columns = useMemo<DataTableColumn<LeaveRequestListItem>[]>(
    () => [
      {
        key: 'number',
        title: t('web.hr.leave_requests.fields.number'),
        sorter: true,
        render: (_, row) => <Typography.Link onClick={() => open(row.id)}>{row.number}</Typography.Link>,
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
        key: 'createdAt',
        title: t('web.hr.leave_requests.fields.created_at'),
        sorter: true,
        hiddenByDefault: true,
        render: (_, row) => formatDateTime(row.createdAt, i18n.language),
      },
    ],
    [t, i18n.language, open],
  );

  const fetchPage = (list: ListParams) =>
    listMyLeaveRequests({
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      status: list.filter?.status ? (list.filter.status.split(',') as LeaveRequestStatus[]) : undefined,
    });

  return (
    <>
      <PageHeader
        title={t('web.hr.leave_requests.my_title')}
        subtitle={t('web.hr.leave_requests.my_subtitle')}
        extra={
          <Can permission={hrPermissions.selfLeaveRequest}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setAsking(true)}>
              {t('web.hr.leave_requests.new')}
            </Button>
          </Can>
        }
      />
      <Flex vertical gap={16}>
        {balances.isLoading ? (
          <Skeleton active />
        ) : (balances.data ?? []).length === 0 ? (
          <Card>
            <Empty description={t('web.hr.leave_balances.none')} />
          </Card>
        ) : (
          (balances.data ?? []).map((balance) => <BalanceCard key={balance.leaveTypeCode} balance={balance} />)
        )}
        <Card title={t('web.hr.leave_requests.my_requests')}>
          <DataTable<LeaveRequestListItem>
            queryKey={getListMyLeaveRequestsQueryKey()}
            fetchPage={fetchPage}
            columns={columns}
            rowKey="id"
            filter={{ status: statuses.length > 0 ? statuses.join(',') : undefined }}
            toolbar={
              <Select
                mode="multiple"
                allowClear
                aria-label={t('web.hr.leave_requests.fields.status')}
                placeholder={t('web.hr.leave_requests.all_statuses')}
                value={statuses}
                onChange={setStatuses}
                options={leaveRequestStatuses.map((s) => ({ value: s, label: t(`web.hr.leave_requests.status.${s}`) }))}
                style={{ minWidth: 240 }}
              />
            }
          />
        </Card>
      </Flex>
      {asking && <LeaveRequestDrawer mode="self" onClose={() => setAsking(false)} onSaved={(id) => open(id)} />}
      {opened && <LeaveRequestDetailDrawer mode="self" id={opened} onClose={() => open(undefined)} />}
    </>
  );
};
