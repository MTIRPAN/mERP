import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { Button, Flex, Input, Segmented, Select, Space } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import { platformPermissions } from '@/modules/platform/permissions';
import { getListBranchesQueryKey, listBranches } from '@/shared/api/generated/endpoints/platform/platform';
import { BranchType, type BranchListItem } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusTag } from '@/shared/components/StatusTag';
import { BranchFormDrawer } from './BranchFormDrawer';
import { activeFilter, toActiveParam, type ActiveFilter } from './filters';

/** Branches in the caller's scope, narrowed by the active company of the header (D4). */
export const BranchesPage = () => {
  const { t } = useTranslation();
  const canManage = useHasPermission(platformPermissions.branchesManage);
  const canAudit = useHasPermission(platformPermissions.auditRead);
  const activeCompanyId = useActiveCompanyId();
  const [search, setSearch] = useState<string>();
  const [type, setType] = useState<BranchType>();
  const [active, setActive] = useState<ActiveFilter>('active');
  const [drawer, setDrawer] = useState<{ open: boolean; branchId?: string }>({ open: false });

  const columns = useMemo<DataTableColumn<BranchListItem>[]>(
    () => [
      { key: 'code', dataIndex: 'code', title: t('web.platform.fields.code'), sorter: true, width: 120 },
      { key: 'name', dataIndex: 'name', title: t('web.platform.branches.name'), sorter: true },
      { key: 'companyName', dataIndex: 'companyName', title: t('web.platform.branches.company') },
      {
        key: 'type',
        title: t('web.platform.branches.type'),
        sorter: true,
        render: (_, row) => t(`web.platform.branch_type.${row.type}`),
      },
      { key: 'city', dataIndex: 'city', title: t('web.platform.address.province') },
      { key: 'district', dataIndex: 'district', title: t('web.platform.address.district'), hiddenByDefault: true },
      {
        key: 'isActive',
        title: t('web.platform.fields.status'),
        sorter: true,
        render: (_, row) => <StatusTag status={row.isActive ? 'active' : 'inactive'} />,
      },
      ...(canManage || canAudit
        ? [
            {
              key: 'actions',
              title: '',
              width: 96,
              render: (_: unknown, row: BranchListItem) => (
                <Flex gap={4}>
                  {canManage && (
                    <Button
                      type="text"
                      icon={<EditOutlined />}
                      aria-label={t('web.common.edit')}
                      onClick={() => setDrawer({ open: true, branchId: row.id })}
                    />
                  )}
                  <AuditHistoryButton
                    compact
                    module="platform"
                    entityType="Branch"
                    entityId={row.id}
                    title={row.name}
                  />
                </Flex>
              ),
            },
          ]
        : []),
    ],
    [t, canManage, canAudit],
  );

  const fetchPage = (params: ListParams) =>
    listBranches({
      page: params.page,
      pageSize: params.pageSize,
      sort: params.sort,
      search: params.filter?.search,
      type: params.filter?.type as BranchType | undefined,
      isActive: toActiveParam(params.filter?.active as ActiveFilter | undefined),
    });

  return (
    <>
      <PageHeader
        title={t('web.platform.branches.title')}
        subtitle={t('web.platform.branches.subtitle')}
        extra={
          <Can permission={platformPermissions.branchesManage}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
              {t('web.platform.branches.new')}
            </Button>
          </Can>
        }
      />
      <DataTable<BranchListItem>
        // The active company is sent as X-Company-Id; it is part of the key so the list refreshes when it changes
        queryKey={[...getListBranchesQueryKey(), activeCompanyId]}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{ search, type, active }}
        toolbar={
          <Space wrap>
            <Input.Search
              allowClear
              placeholder={t('web.platform.branches.search')}
              onSearch={(value) => setSearch(value.trim() || undefined)}
              style={{ width: 260 }}
            />
            <Select<BranchType>
              allowClear
              placeholder={t('web.platform.branches.type')}
              value={type}
              onChange={setType}
              style={{ width: 160 }}
              options={Object.values(BranchType).map((value) => ({
                value,
                label: t(`web.platform.branch_type.${value}`),
              }))}
            />
            <Segmented<ActiveFilter>
              value={active}
              onChange={setActive}
              options={activeFilter.map((value) => ({ value, label: t(`web.platform.filters.${value}`) }))}
            />
          </Space>
        }
      />
      <BranchFormDrawer open={drawer.open} branchId={drawer.branchId} onClose={() => setDrawer({ open: false })} />
    </>
  );
};
