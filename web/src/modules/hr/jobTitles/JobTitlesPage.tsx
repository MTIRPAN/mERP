import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { Alert, Button, Flex, Input, Segmented, Space } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { activeFilter, toActiveParam, type ActiveFilter } from '@/modules/platform/organization/filters';
import { getListJobsQueryKey, listJobs } from '@/shared/api/generated/endpoints/hr/hr';
import type { JobListItem } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusTag } from '@/shared/components/StatusTag';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import { hrPermissions } from '../permissions';
import { JobFormDrawer } from './JobFormDrawer';

type Drawer = { open: false } | { open: true; jobId?: string };

/**
 * The job catalog shared by the tenant's companies (PLAN H1b, D37). Everyone with hr.org.read sees it; changing it
 * needs hr.org.manage with the Tenant scope.
 */
export const JobTitlesPage = () => {
  const { t } = useTranslation();
  const canManage = useHasPermission(hrPermissions.orgManage);
  const [search, setSearch] = useState<string>();
  const [active, setActive] = useState<ActiveFilter>('active');
  const [drawer, setDrawer] = useState<Drawer>({ open: false });

  const columns = useMemo<DataTableColumn<JobListItem>[]>(
    () => [
      { key: 'code', dataIndex: 'code', title: t('web.platform.fields.code'), sorter: true, width: 140 },
      { key: 'name', dataIndex: 'name', title: t('web.hr.job_titles.fields.name'), sorter: true },
      { key: 'family', dataIndex: 'family', title: t('web.hr.job_titles.fields.family'), sorter: true },
      { key: 'level', dataIndex: 'level', title: t('web.hr.job_titles.fields.level'), sorter: true, width: 100 },
      {
        key: 'positionCount',
        dataIndex: 'positionCount',
        title: t('web.hr.job_titles.fields.positions'),
        align: 'right',
        width: 110,
      },
      {
        key: 'isActive',
        title: t('web.platform.fields.status'),
        sorter: true,
        render: (_, row) => <StatusTag status={row.isActive ? 'active' : 'inactive'} />,
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
                onClick={() => setDrawer({ open: true, jobId: row.id })}
              />
            )}
            <AuditHistoryButton compact module="hr" entityType="Job" entityId={row.id} title={row.name} />
          </Flex>
        ),
      },
    ],
    [t, canManage],
  );

  const fetchPage = (params: ListParams) =>
    listJobs({
      page: params.page,
      pageSize: params.pageSize,
      sort: params.sort,
      search: params.filter?.search,
      isActive: toActiveParam(params.filter?.active as ActiveFilter | undefined),
    });

  return (
    <>
      <PageHeader
        title={t('web.hr.job_titles.title')}
        subtitle={t('web.hr.job_titles.subtitle')}
        extra={
          <Can permission={hrPermissions.orgManage}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
              {t('web.hr.job_titles.new')}
            </Button>
          </Can>
        }
      />
      {canManage && (
        <Alert type="info" showIcon title={t('web.hr.job_titles.tenant_only')} style={{ marginBottom: 16 }} />
      )}
      <DataTable<JobListItem>
        queryKey={getListJobsQueryKey()}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{ search, active }}
        toolbar={
          <Space wrap>
            <Input.Search
              allowClear
              placeholder={t('web.hr.job_titles.search')}
              onSearch={(value) => setSearch(value.trim() || undefined)}
              style={{ width: 260 }}
            />
            <Segmented<ActiveFilter>
              value={active}
              onChange={setActive}
              options={activeFilter.map((value) => ({ value, label: t(`web.platform.filters.${value}`) }))}
            />
          </Space>
        }
      />
      {drawer.open && <JobFormDrawer open onClose={() => setDrawer({ open: false })} jobId={drawer.jobId} />}
    </>
  );
};
