import { PlusOutlined } from '@ant-design/icons';
import { Button, Input, Select, Space, Typography } from 'antd';
import { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
import { useActiveCompanyId } from '@/app/company/companyStore';
import {
  getListRequisitionsQueryKey,
  listRequisitions,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { RequisitionListItem, RequisitionStatus } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDate, formatDateTime } from '@/shared/format/date';
import { recruitmentPermissions } from '../permissions';
import { RequisitionDetailDrawer, RequisitionStatusTag } from './RequisitionDetailDrawer';
import { RequisitionDrawer } from './RequisitionDrawer';
import { requisitionStatuses } from './requisitions';

/**
 * Requisitions (PLAN R2, D48): requests to hire people for the companies in scope, with filters and a new
 * requisition. <c>/recruitment/requisitions/:id</c> (the approval inbox's link) opens one.
 */
export const RequisitionsPage = () => {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams();
  const activeCompanyId = useActiveCompanyId();
  const [statuses, setStatuses] = useState<RequisitionStatus[]>([]);
  const [search, setSearch] = useState<string>();
  const [creating, setCreating] = useState(false);

  const open = useCallback(
    (requisitionId: string | undefined) =>
      void navigate(requisitionId ? `/recruitment/requisitions/${requisitionId}` : '/recruitment/requisitions'),
    [navigate],
  );

  const columns = useMemo<DataTableColumn<RequisitionListItem>[]>(
    () => [
      {
        key: 'number',
        title: t('web.recruitment.requisitions.fields.number'),
        sorter: true,
        render: (_, row) => <Typography.Link onClick={() => open(row.id)}>{row.number}</Typography.Link>,
      },
      { key: 'positionTitle', dataIndex: 'positionTitle', title: t('web.recruitment.requisitions.fields.position') },
      { key: 'orgUnitName', dataIndex: 'orgUnitName', title: t('web.recruitment.requisitions.fields.unit') },
      {
        key: 'companyName',
        dataIndex: 'companyName',
        title: t('web.recruitment.requisitions.fields.company'),
        hiddenByDefault: true,
      },
      {
        key: 'headcount',
        title: t('web.recruitment.requisitions.fields.headcount'),
        render: (_, row) =>
          t('web.recruitment.requisitions.filled_of', { filled: row.filledCount, headcount: row.headcount }),
      },
      {
        key: 'reason',
        title: t('web.recruitment.requisitions.fields.reason'),
        render: (_, row) => t(`web.recruitment.requisitions.reasons.${row.reason}`),
      },
      {
        key: 'status',
        title: t('web.recruitment.requisitions.fields.status'),
        render: (_, row) => <RequisitionStatusTag status={row.status} />,
      },
      {
        key: 'desiredStartDate',
        title: t('web.recruitment.requisitions.fields.desired_start_date'),
        sorter: true,
        render: (_, row) => (row.desiredStartDate ? formatDate(row.desiredStartDate, i18n.language) : '—'),
      },
      {
        key: 'hiringManagerName',
        dataIndex: 'hiringManagerName',
        title: t('web.recruitment.requisitions.fields.hiring_manager'),
        hiddenByDefault: true,
      },
      {
        key: 'createdAt',
        title: t('web.recruitment.requisitions.fields.created_at'),
        sorter: true,
        render: (_, row) => formatDateTime(row.createdAt, i18n.language),
      },
    ],
    [t, i18n.language, open],
  );

  const fetchPage = (list: ListParams) =>
    listRequisitions({
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      status: list.filter?.status ? (list.filter.status.split(',') as RequisitionStatus[]) : undefined,
      search: list.filter?.search,
    });

  return (
    <>
      <PageHeader
        title={t('web.recruitment.requisitions.title')}
        subtitle={t('web.recruitment.requisitions.subtitle')}
        extra={
          <Can permission={recruitmentPermissions.requisitionsManage}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
              {t('web.recruitment.requisitions.new_title')}
            </Button>
          </Can>
        }
      />
      <DataTable<RequisitionListItem>
        // The active company is sent as X-Company-Id; it is part of the key so the list refreshes when it changes
        queryKey={[...getListRequisitionsQueryKey(), activeCompanyId]}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{ status: statuses.length > 0 ? statuses.join(',') : undefined, search }}
        toolbar={
          <Space wrap>
            <Input.Search
              allowClear
              placeholder={t('web.recruitment.requisitions.search')}
              onSearch={(value) => setSearch(value.trim() || undefined)}
              style={{ width: 260 }}
            />
            <Select
              mode="multiple"
              allowClear
              aria-label={t('web.recruitment.requisitions.fields.status')}
              placeholder={t('web.recruitment.requisitions.all_statuses')}
              value={statuses}
              onChange={setStatuses}
              options={requisitionStatuses.map((s) => ({
                value: s,
                label: t(`web.recruitment.requisitions.status.${s}`),
              }))}
              style={{ minWidth: 220 }}
            />
          </Space>
        }
      />
      {creating && <RequisitionDrawer onClose={() => setCreating(false)} onSaved={(saved) => open(saved)} />}
      {id && <RequisitionDetailDrawer id={id} onClose={() => open(undefined)} />}
    </>
  );
};
