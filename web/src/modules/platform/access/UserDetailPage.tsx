import { DeleteOutlined, EditOutlined, MailOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import {
  App,
  Button,
  Card,
  Descriptions,
  Flex,
  Popconfirm,
  Skeleton,
  Table,
  Tag,
  Tooltip,
  type TableColumnsType,
} from 'antd';
import dayjs from 'dayjs';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { useMe } from '@/app/me/meContext';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  getGetMeQueryKey,
  getGetUserQueryKey,
  getListUsersQueryKey,
  useGetUser,
  useRemoveAssignment,
  useSendPasswordReset,
  useSetUserActive,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { AssignmentItem } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusTag } from '@/shared/components/StatusTag';
import { formatDateTime } from '@/shared/format/date';
import { NotFoundPage } from '@/shared/pages/NotFoundPage';
import { AssignmentDrawer } from './AssignmentDrawer';
import { assignmentState, periodText, scopeText, type AssignmentState } from './assignments';

const stateColors: Record<AssignmentState, string> = { current: 'success', future: 'processing', expired: 'default' };

/** A user with the role assignments; administrators activate, reset the password and assign roles here. */
export const UserDetailPage = () => {
  const { t, i18n } = useTranslation();
  const { id = '' } = useParams();
  const me = useMe();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const canManage = useHasPermission(platformPermissions.usersManage);
  const user = useGetUser(id);
  const setActive = useSetUserActive();
  const passwordReset = useSendPasswordReset();
  const removeAssignment = useRemoveAssignment();
  const [drawer, setDrawer] = useState<{ open: boolean; assignment?: AssignmentItem }>({ open: false });

  if (user.isError && isApiError(user.error) && user.error.status === 404) {
    return <NotFoundPage />;
  }

  if (!user.data) {
    return <Skeleton active />;
  }

  const data = user.data;
  const isSelf = me.user.id === data.id;
  const today = dayjs().format('YYYY-MM-DD');

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: getGetUserQueryKey(id) });
    await queryClient.invalidateQueries({ queryKey: getListUsersQueryKey() });
    await queryClient.invalidateQueries({ queryKey: getGetMeQueryKey() });
  };

  // Failures are reported by ApiErrorNotifier; the confirm popup just closes
  const handled = (action: () => Promise<unknown>) => () => action().catch(() => undefined);

  const toggleActive = handled(async () => {
    await setActive.mutateAsync({ id, data: { isActive: !data.isActive } });
    await refresh();
    void message.success(t(data.isActive ? 'web.platform.users.deactivated' : 'web.platform.users.activated'));
  });

  const sendPasswordReset = handled(async () => {
    await passwordReset.mutateAsync({ id });
    void message.success(t('web.platform.users.password_reset_sent', { email: data.email ?? data.userName }));
  });

  const remove = (assignment: AssignmentItem) =>
    handled(async () => {
      await removeAssignment.mutateAsync({ id, assignmentId: assignment.id });
      await refresh();
    })();

  const culture = me.cultures.find((c) => c.code === data.culture)?.name ?? t('web.platform.users.culture_browser');

  const columns: TableColumnsType<AssignmentItem> = [
    { key: 'role', dataIndex: 'roleName', title: t('web.platform.assignments.role') },
    { key: 'scope', title: t('web.platform.assignments.scope'), render: (_, row) => scopeText(row, t) },
    {
      key: 'period',
      title: t('web.platform.assignments.period'),
      render: (_, row) => periodText(row, t, i18n.language),
    },
    {
      key: 'state',
      title: t('web.platform.fields.status'),
      render: (_, row) => {
        const state = assignmentState(row, today);
        return (
          <Tag color={stateColors[state]} variant="filled">
            {t(`web.platform.assignments.state.${state}`)}
          </Tag>
        );
      },
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            width: 96,
            render: (_: unknown, row: AssignmentItem) => (
              <Flex gap={4}>
                <Button
                  type="text"
                  icon={<EditOutlined />}
                  aria-label={t('web.platform.assignments.edit')}
                  onClick={() => setDrawer({ open: true, assignment: row })}
                />
                <Popconfirm
                  title={t('web.platform.assignments.remove_confirm', { role: row.roleName })}
                  okText={t('web.common.yes')}
                  cancelText={t('web.common.no')}
                  onConfirm={() => remove(row)}
                >
                  <Button
                    type="text"
                    danger
                    icon={<DeleteOutlined />}
                    aria-label={t('web.platform.assignments.remove')}
                  />
                </Popconfirm>
              </Flex>
            ),
          },
        ]
      : []),
  ];

  return (
    <>
      <PageHeader
        title={data.displayName}
        subtitle={data.userName}
        breadcrumb={[
          { title: <Link to="/access/users">{t('web.platform.users.title')}</Link> },
          { title: data.displayName },
        ]}
        extra={
          <>
            <AuditHistoryButton module="platform" entityType="User" entityId={data.id} title={data.displayName} />
            {canManage && (
              <>
                <Popconfirm
                  title={t('web.platform.users.password_reset_confirm')}
                  okText={t('web.common.yes')}
                  cancelText={t('web.common.no')}
                  onConfirm={sendPasswordReset}
                  disabled={!data.isActive}
                >
                  <Button icon={<MailOutlined />} disabled={!data.isActive}>
                    {t('web.platform.users.password_reset')}
                  </Button>
                </Popconfirm>
                <Tooltip title={isSelf ? t('web.platform.users.cannot_deactivate_self') : undefined}>
                  <Popconfirm
                    title={t(
                      data.isActive ? 'web.platform.users.deactivate_confirm' : 'web.platform.users.activate_confirm',
                    )}
                    okText={t('web.common.yes')}
                    cancelText={t('web.common.no')}
                    onConfirm={toggleActive}
                    disabled={isSelf && data.isActive}
                  >
                    <Button danger={data.isActive} disabled={isSelf && data.isActive}>
                      {t(data.isActive ? 'web.platform.users.deactivate' : 'web.platform.users.activate')}
                    </Button>
                  </Popconfirm>
                </Tooltip>
              </>
            )}
          </>
        }
      />
      <Flex vertical gap={16}>
        <Card title={t('web.platform.users.details')}>
          <Descriptions
            column={{ xs: 1, md: 2 }}
            items={[
              { key: 'userName', label: t('web.platform.users.user_name'), children: data.userName },
              { key: 'email', label: t('web.platform.fields.email'), children: data.email ?? '—' },
              { key: 'firstName', label: t('web.platform.users.first_name'), children: data.firstName ?? '—' },
              { key: 'lastName', label: t('web.platform.users.last_name'), children: data.lastName ?? '—' },
              {
                key: 'status',
                label: t('web.platform.fields.status'),
                children: <StatusTag status={data.isActive ? 'active' : 'inactive'} />,
              },
              { key: 'culture', label: t('web.platform.users.culture'), children: culture },
              {
                key: 'createdAt',
                label: t('web.platform.users.created_at'),
                children: formatDateTime(data.createdAt, i18n.language),
              },
            ]}
          />
        </Card>
        <Card
          title={t('web.platform.assignments.title')}
          extra={
            canManage && (
              <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
                {t('web.platform.assignments.new')}
              </Button>
            )
          }
        >
          <Table<AssignmentItem>
            rowKey="id"
            columns={columns}
            dataSource={data.assignments}
            pagination={false}
            locale={{ emptyText: t('web.platform.assignments.empty') }}
            scroll={{ x: 'max-content' }}
          />
        </Card>
      </Flex>
      <AssignmentDrawer
        userId={id}
        assignment={drawer.assignment}
        open={drawer.open}
        onClose={() => setDrawer({ open: false })}
      />
    </>
  );
};
