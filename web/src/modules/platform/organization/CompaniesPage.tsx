import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { Button, Flex, Input, Segmented, Space } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import { platformPermissions } from '@/modules/platform/permissions';
import { getListCompaniesQueryKey, listCompanies } from '@/shared/api/generated/endpoints/platform/platform';
import type { CompanyListItem } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusTag } from '@/shared/components/StatusTag';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { CompanyFormDrawer } from './CompanyFormDrawer';
import { activeFilter, toActiveParam, type ActiveFilter } from './filters';

/** Companies of the tenant in the caller's scope (PLAN P1). */
export const CompaniesPage = () => {
  const { t } = useTranslation();
  const canManage = useHasPermission(platformPermissions.companiesManage);
  const canAudit = useHasPermission(platformPermissions.auditRead);
  const [search, setSearch] = useState<string>();
  const [active, setActive] = useState<ActiveFilter>('active');
  const [drawer, setDrawer] = useState<{ open: boolean; companyId?: string }>({ open: false });

  const columns = useMemo<DataTableColumn<CompanyListItem>[]>(
    () => [
      { key: 'code', dataIndex: 'code', title: t('web.platform.fields.code'), sorter: true, width: 120 },
      { key: 'shortName', dataIndex: 'shortName', title: t('web.platform.companies.short_name'), sorter: true },
      {
        key: 'title',
        dataIndex: 'title',
        title: t('web.platform.companies.title_field'),
        sorter: true,
        ellipsis: true,
      },
      { key: 'taxNumber', dataIndex: 'taxNumber', title: t('web.platform.companies.tax_number'), sorter: true },
      { key: 'city', dataIndex: 'city', title: t('web.platform.address.province') },
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
              render: (_: unknown, row: CompanyListItem) => (
                <Flex gap={4}>
                  {canManage && (
                    <Button
                      type="text"
                      icon={<EditOutlined />}
                      aria-label={t('web.common.edit')}
                      onClick={() => setDrawer({ open: true, companyId: row.id })}
                    />
                  )}
                  <AuditHistoryButton
                    compact
                    module="platform"
                    entityType="Company"
                    entityId={row.id}
                    title={row.shortName}
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
    listCompanies({
      page: params.page,
      pageSize: params.pageSize,
      sort: params.sort,
      search: params.filter?.search,
      isActive: toActiveParam(params.filter?.active as ActiveFilter | undefined),
    });

  return (
    <>
      <PageHeader
        title={t('web.platform.companies.title')}
        subtitle={t('web.platform.companies.subtitle')}
        extra={
          <Can permission={platformPermissions.companiesManage}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
              {t('web.platform.companies.new')}
            </Button>
          </Can>
        }
      />
      <DataTable<CompanyListItem>
        queryKey={getListCompaniesQueryKey()}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{ search, active }}
        toolbar={
          <Space wrap>
            <Input.Search
              allowClear
              placeholder={t('web.platform.companies.search')}
              onSearch={(value) => setSearch(value.trim() || undefined)}
              style={{ width: 280 }}
            />
            <Segmented<ActiveFilter>
              value={active}
              onChange={setActive}
              options={activeFilter.map((value) => ({ value, label: t(`web.platform.filters.${value}`) }))}
            />
          </Space>
        }
      />
      <CompanyFormDrawer open={drawer.open} companyId={drawer.companyId} onClose={() => setDrawer({ open: false })} />
    </>
  );
};
