import { PlusOutlined } from '@ant-design/icons';
import { Button, Input, Segmented, Select, Space, Tag, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { activeFilter, toActiveParam, type ActiveFilter } from '@/modules/platform/organization/filters';
import { platformPermissions } from '@/modules/platform/permissions';
import { getListUsersQueryKey, listUsers, useListRoles } from '@/shared/api/generated/endpoints/platform/platform';
import type { UserListItem } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusTag } from '@/shared/components/StatusTag';
import { UserCreateDrawer } from './UserCreateDrawer';

/** Users of the tenant with the roles in force today (PLAN P2). */
export const UsersPage = () => {
  const { t } = useTranslation();
  const [search, setSearch] = useState<string>();
  const [active, setActive] = useState<ActiveFilter>('active');
  const [roleId, setRoleId] = useState<string>();
  const [creating, setCreating] = useState(false);
  const roles = useListRoles({ pageSize: 200 });

  const columns = useMemo<DataTableColumn<UserListItem>[]>(
    () => [
      {
        key: 'userName',
        dataIndex: 'userName',
        title: t('web.platform.users.user_name'),
        sorter: true,
        render: (_, row) => <Link to={`/access/users/${row.id}`}>{row.userName}</Link>,
      },
      // The display name sorts by first name
      { key: 'firstName', dataIndex: 'displayName', title: t('web.platform.users.name'), sorter: true },
      { key: 'email', dataIndex: 'email', title: t('web.platform.fields.email'), sorter: true },
      {
        key: 'roles',
        title: t('web.platform.users.roles'),
        render: (_, row) =>
          row.roles.length ? (
            <Space size={[4, 4]} wrap>
              {row.roles.map((role) => (
                <Tag key={role}>{role}</Tag>
              ))}
            </Space>
          ) : (
            <Typography.Text type="secondary">{t('web.platform.users.no_role')}</Typography.Text>
          ),
      },
      {
        key: 'isActive',
        title: t('web.platform.fields.status'),
        sorter: true,
        render: (_, row) => <StatusTag status={row.isActive ? 'active' : 'inactive'} />,
      },
    ],
    [t],
  );

  const fetchPage = (params: ListParams) =>
    listUsers({
      page: params.page,
      pageSize: params.pageSize,
      sort: params.sort,
      search: params.filter?.search,
      isActive: toActiveParam(params.filter?.active as ActiveFilter | undefined),
      roleId: params.filter?.roleId,
    });

  return (
    <>
      <PageHeader
        title={t('web.platform.users.title')}
        subtitle={t('web.platform.users.subtitle')}
        extra={
          <Can permission={platformPermissions.usersManage}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
              {t('web.platform.users.new')}
            </Button>
          </Can>
        }
      />
      <DataTable<UserListItem>
        queryKey={getListUsersQueryKey()}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{ search, active, roleId }}
        toolbar={
          <Space wrap>
            <Input.Search
              allowClear
              placeholder={t('web.platform.users.search')}
              onSearch={(value) => setSearch(value.trim() || undefined)}
              style={{ width: 280 }}
            />
            <Select
              allowClear
              showSearch={{ optionFilterProp: 'label' }}
              placeholder={t('web.platform.users.role_filter')}
              aria-label={t('web.platform.users.role_filter')}
              style={{ minWidth: 200 }}
              value={roleId}
              onChange={(value?: string) => setRoleId(value)}
              loading={roles.isFetching}
              options={roles.data?.items.map((role) => ({ value: role.id, label: role.name }))}
            />
            <Segmented<ActiveFilter>
              value={active}
              onChange={setActive}
              options={activeFilter.map((value) => ({ value, label: t(`web.platform.filters.${value}`) }))}
            />
          </Space>
        }
      />
      <UserCreateDrawer open={creating} onClose={() => setCreating(false)} />
    </>
  );
};
