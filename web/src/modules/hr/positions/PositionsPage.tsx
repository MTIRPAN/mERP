import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { Button, Flex, Input, Segmented, Select, Space } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import { getListPositionsQueryKey, listPositions } from '@/shared/api/generated/endpoints/hr/hr';
import { PositionStatus, type PositionListItem } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { hrPermissions } from '../permissions';
import { PositionFormDrawer } from './PositionFormDrawer';
import { PositionStatusTag } from './PositionStatusTag';

type Seats = 'all' | 'vacant';

type Drawer = { open: false } | { open: true; positionId?: string };

/**
 * Positions in the caller's scope with their seats (PLAN H1b), narrowed by the active company (D4). "Boş kadrolar"
 * is the vacancy report: active positions with empty seats.
 */
export const PositionsPage = () => {
  const { t } = useTranslation();
  const canManage = useHasPermission(hrPermissions.orgManage);
  const activeCompanyId = useActiveCompanyId();
  const [search, setSearch] = useState<string>();
  const [status, setStatus] = useState<PositionStatus>();
  const [seats, setSeats] = useState<Seats>('all');
  const [drawer, setDrawer] = useState<Drawer>({ open: false });

  const columns = useMemo<DataTableColumn<PositionListItem>[]>(
    () => [
      { key: 'code', dataIndex: 'code', title: t('web.platform.fields.code'), sorter: true, width: 120 },
      { key: 'job', dataIndex: 'jobName', title: t('web.hr.positions.fields.job') },
      { key: 'orgUnit', dataIndex: 'orgUnitName', title: t('web.hr.positions.fields.org_unit') },
      { key: 'company', dataIndex: 'companyName', title: t('web.hr.org.company'), hiddenByDefault: true },
      {
        key: 'reportsTo',
        title: t('web.hr.positions.fields.reports_to'),
        hiddenByDefault: true,
        render: (_, row) => row.reportsToPositionCode ?? '—',
      },
      { key: 'norm', dataIndex: 'norm', title: t('web.hr.positions.fields.norm'), sorter: true, align: 'right' },
      { key: 'filled', dataIndex: 'filled', title: t('web.hr.positions.fields.filled'), align: 'right' },
      { key: 'vacant', dataIndex: 'vacant', title: t('web.hr.positions.fields.vacant'), align: 'right' },
      {
        key: 'status',
        title: t('web.hr.positions.fields.status'),
        sorter: true,
        render: (_, row) => <PositionStatusTag status={row.status} />,
      },
      {
        key: 'isManager',
        title: t('web.hr.positions.fields.is_manager'),
        hiddenByDefault: true,
        render: (_, row) => t(row.isManager ? 'web.common.yes' : 'web.common.no'),
      },
      {
        key: 'actions',
        title: '',
        width: 96,
        render: (_, row) => (
          <Flex gap={4}>
            {canManage && (
              <Button
                type="text"
                icon={<EditOutlined />}
                aria-label={t('web.common.edit')}
                onClick={() => setDrawer({ open: true, positionId: row.id })}
              />
            )}
            <AuditHistoryButton compact module="hr" entityType="Position" entityId={row.id} title={row.code} />
          </Flex>
        ),
      },
    ],
    [t, canManage],
  );

  const fetchPage = (params: ListParams) =>
    listPositions({
      page: params.page,
      pageSize: params.pageSize,
      sort: params.sort,
      search: params.filter?.search,
      status: params.filter?.status as PositionStatus | undefined,
      vacantOnly: params.filter?.seats === 'vacant' ? true : undefined,
    });

  return (
    <>
      <PageHeader
        title={t('web.hr.positions.title')}
        subtitle={t('web.hr.positions.subtitle')}
        extra={
          <Can permission={hrPermissions.orgManage}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
              {t('web.hr.positions.new')}
            </Button>
          </Can>
        }
      />
      <DataTable<PositionListItem>
        // The active company is sent as X-Company-Id; it is part of the key so the list refreshes when it changes
        queryKey={[...getListPositionsQueryKey(), activeCompanyId]}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{ search, status, seats }}
        toolbar={
          <Space wrap>
            <Input.Search
              allowClear
              placeholder={t('web.hr.positions.search')}
              onSearch={(value) => setSearch(value.trim() || undefined)}
              style={{ width: 260 }}
            />
            <Select<PositionStatus>
              allowClear
              placeholder={t('web.hr.positions.fields.status')}
              value={status}
              onChange={setStatus}
              style={{ width: 160 }}
              options={Object.values(PositionStatus).map((value) => ({
                value,
                label: t(`web.hr.position_status.${value}`),
              }))}
            />
            <Segmented<Seats>
              value={seats}
              onChange={setSeats}
              options={[
                { value: 'all', label: t('web.hr.positions.all') },
                { value: 'vacant', label: t('web.hr.positions.vacant_only') },
              ]}
            />
          </Space>
        }
      />
      {drawer.open && (
        <PositionFormDrawer
          open
          onClose={() => setDrawer({ open: false })}
          positionId={drawer.positionId}
          companyId={activeCompanyId}
        />
      )}
    </>
  );
};
