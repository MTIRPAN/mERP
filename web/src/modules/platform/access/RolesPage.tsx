import { PlusOutlined } from '@ant-design/icons';
import { Button, Input, Tag } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import { platformPermissions } from '@/modules/platform/permissions';
import { getListRolesQueryKey, listRoles } from '@/shared/api/generated/endpoints/platform/platform';
import type { RoleListItem } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';

/** System and custom roles of the tenant (PLAN P2). */
export const RolesPage = () => {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [search, setSearch] = useState<string>();
  const count = useMemo(() => new Intl.NumberFormat(i18n.language), [i18n.language]);

  const columns = useMemo<DataTableColumn<RoleListItem>[]>(
    () => [
      {
        key: 'name',
        dataIndex: 'name',
        title: t('web.platform.roles.name'),
        sorter: true,
        render: (_, row) => <Link to={`/access/roles/${row.id}`}>{row.name}</Link>,
      },
      { key: 'code', dataIndex: 'code', title: t('web.platform.fields.code'), sorter: true },
      {
        key: 'isSystem',
        title: t('web.platform.roles.kind'),
        render: (_, row) =>
          row.isSystem ? (
            <Tag color="processing" variant="filled">
              {t('web.platform.roles.system')}
            </Tag>
          ) : (
            <Tag variant="filled">{t('web.platform.roles.custom')}</Tag>
          ),
      },
      {
        key: 'permissionCount',
        title: t('web.platform.roles.permission_count'),
        sorter: true,
        align: 'right',
        render: (_, row) => count.format(row.permissionCount),
      },
      {
        key: 'userCount',
        title: t('web.platform.roles.user_count'),
        sorter: true,
        align: 'right',
        render: (_, row) => count.format(row.userCount),
      },
    ],
    [t, count],
  );

  const fetchPage = (params: ListParams) =>
    listRoles({ page: params.page, pageSize: params.pageSize, sort: params.sort, search: params.filter?.search });

  return (
    <>
      <PageHeader
        title={t('web.platform.roles.title')}
        subtitle={t('web.platform.roles.subtitle')}
        extra={
          <Can permission={platformPermissions.rolesManage}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => void navigate('/access/roles/new')}>
              {t('web.platform.roles.new')}
            </Button>
          </Can>
        }
      />
      <DataTable<RoleListItem>
        queryKey={getListRolesQueryKey()}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{ search }}
        toolbar={
          <Input.Search
            allowClear
            placeholder={t('web.platform.roles.search')}
            onSearch={(value) => setSearch(value.trim() || undefined)}
            style={{ width: 280 }}
          />
        }
      />
    </>
  );
};
