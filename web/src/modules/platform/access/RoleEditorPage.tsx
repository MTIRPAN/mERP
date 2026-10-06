import { DeleteOutlined, RollbackOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { App, Alert, Button, Card, Col, Flex, Form, Input, Popconfirm, Row, Skeleton, Tag, Tooltip } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { useMe } from '@/app/me/meContext';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  getGetMeQueryKey,
  getGetRoleQueryKey,
  getListRolesQueryKey,
  useCreateRole,
  useDeleteRole,
  useGetPermissionTree,
  useGetRole,
  useResetRole,
  useUpdateRole,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { RoleDetails } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { NotFoundPage } from '@/shared/pages/NotFoundPage';
import { PermissionTreeField } from './PermissionTreeField';

interface RoleFormValues {
  code?: string;
  /** Name per culture. */
  name?: Record<string, string | undefined>;
  description?: string;
  permissions?: string[];
}

const blank = (value?: string) => (value?.trim() ? value.trim() : null);

const toValues = (role?: RoleDetails): RoleFormValues =>
  role
    ? { code: role.code, name: role.name, description: role.description ?? undefined, permissions: role.permissions }
    : { name: {}, permissions: [] };

/** Names with text only; the API needs at least the default culture. */
const toNames = (name: RoleFormValues['name']): Record<string, string> =>
  Object.fromEntries(
    Object.entries(name ?? {}).flatMap(([culture, text]) => (text?.trim() ? [[culture, text.trim()]] : [])),
  );

/** Creates a role or edits one: names, description and the permission matrix. Route "new" creates. */
export const RoleEditorPage = () => {
  const { t } = useTranslation();
  const { id = 'new' } = useParams();
  const creating = id === 'new';
  const me = useMe();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<RoleFormValues>();
  const [saving, setSaving] = useState(false);
  const canManage = useHasPermission(platformPermissions.rolesManage);
  const role = useGetRole(id, { query: { enabled: !creating } });
  const tree = useGetPermissionTree();
  const create = useCreateRole();
  const update = useUpdateRole();
  const remove = useDeleteRole();
  const reset = useResetRole();

  if (role.isError && isApiError(role.error) && role.error.status === 404) {
    return <NotFoundPage />;
  }

  if (!tree.data || (!creating && !role.data)) {
    return <Skeleton active />;
  }

  const data = role.data;

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: getListRolesQueryKey() });
    // The signed-in user's menu and permissions follow the matrix
    await queryClient.invalidateQueries({ queryKey: getGetMeQueryKey() });
    if (!creating) {
      await queryClient.invalidateQueries({ queryKey: getGetRoleQueryKey(id) });
    }
  };

  const submit = async (values: RoleFormValues) => {
    setSaving(true);
    try {
      const fields = {
        name: toNames(values.name),
        description: blank(values.description),
        permissions: values.permissions ?? [],
      };
      if (data) {
        await update.mutateAsync({ id, data: { ...fields, version: data.version } });
        await refresh();
        void message.success(t('web.platform.roles.saved'));
      } else {
        const created = await create.mutateAsync({ data: { ...fields, code: blank(values.code) } });
        await refresh();
        void message.success(t('web.platform.roles.saved'));
        void navigate(`/access/roles/${created.id}`, { replace: true });
      }
    } catch (error) {
      applyApiErrorsToForm(form, error, { name: ['name', DEFAULT_CULTURE] });
    } finally {
      setSaving(false);
    }
  };

  const deleteRole = async () => {
    try {
      await remove.mutateAsync({ id });
      await queryClient.invalidateQueries({ queryKey: getListRolesQueryKey() });
      void navigate('/access/roles', { replace: true });
    } catch {
      // Reported by ApiErrorNotifier
    }
  };

  const resetRole = async () => {
    try {
      await reset.mutateAsync({ id });
      await refresh();
      void message.success(t('web.platform.roles.reset_done'));
    } catch {
      // Reported by ApiErrorNotifier
    }
  };

  const title = creating ? t('web.platform.roles.new') : (data?.name[DEFAULT_CULTURE] ?? data?.code);

  return (
    <>
      <PageHeader
        title={
          <Flex align="center" gap={8}>
            {title}
            {data?.isSystem && (
              <Tag color="processing" variant="filled">
                {t('web.platform.roles.system')}
              </Tag>
            )}
          </Flex>
        }
        subtitle={data ? t('web.platform.roles.users_holding', { count: data.userCount }) : undefined}
        breadcrumb={[{ title: <Link to="/access/roles">{t('web.platform.roles.title')}</Link> }, { title }]}
        extra={
          <>
            {data && <AuditHistoryButton module="platform" entityType="Role" entityId={data.id} title={title} />}
            {canManage && (
              <>
                {data?.isSystem && (
                  <Popconfirm
                    title={t('web.platform.roles.reset_confirm')}
                    okText={t('web.common.yes')}
                    cancelText={t('web.common.no')}
                    onConfirm={resetRole}
                  >
                    <Button icon={<RollbackOutlined />}>{t('web.platform.roles.reset')}</Button>
                  </Popconfirm>
                )}
                {data && !data.isSystem && (
                  <Tooltip title={data.userCount > 0 ? t('web.platform.roles.delete_in_use') : undefined}>
                    <Popconfirm
                      title={t('web.platform.roles.delete_confirm')}
                      okText={t('web.common.yes')}
                      cancelText={t('web.common.no')}
                      onConfirm={deleteRole}
                      disabled={data.userCount > 0}
                    >
                      <Button danger icon={<DeleteOutlined />} disabled={data.userCount > 0}>
                        {t('web.platform.roles.delete')}
                      </Button>
                    </Popconfirm>
                  </Tooltip>
                )}
                <Button type="primary" loading={saving} onClick={() => form.submit()}>
                  {t('web.common.save')}
                </Button>
              </>
            )}
          </>
        }
      />
      <Form<RoleFormValues>
        key={data ? `${data.id}-${data.version}` : 'new'}
        form={form}
        layout="vertical"
        requiredMark="optional"
        initialValues={toValues(data)}
        onFinish={submit}
        disabled={!canManage}
      >
        <Row gutter={16}>
          <Col xs={24} lg={8}>
            <Card title={t('web.platform.roles.details')}>
              <Form.Item
                name="code"
                label={t('web.platform.fields.code')}
                tooltip={t('web.platform.fields.code_hint')}
                rules={[{ required: true, message: t('web.platform.fields.required') }]}
              >
                <Input maxLength={50} disabled={!creating} style={{ textTransform: 'uppercase' }} />
              </Form.Item>
              {me.cultures.map((culture) => (
                <Form.Item
                  key={culture.code}
                  name={['name', culture.code]}
                  label={
                    me.cultures.length > 1
                      ? t('web.platform.roles.name_in', { language: culture.name })
                      : t('web.platform.roles.name')
                  }
                  rules={
                    culture.code === DEFAULT_CULTURE
                      ? [{ required: true, message: t('web.platform.fields.required') }]
                      : undefined
                  }
                >
                  <Input maxLength={100} />
                </Form.Item>
              ))}
              <Form.Item name="description" label={t('web.platform.roles.description')}>
                <Input.TextArea maxLength={500} autoSize={{ minRows: 3, maxRows: 8 }} />
              </Form.Item>
            </Card>
          </Col>
          <Col xs={24} lg={16}>
            <Card title={t('web.platform.roles.permissions')}>
              {data?.isSystem && canManage && (
                <Alert type="info" showIcon title={t('web.platform.roles.system_hint')} style={{ marginBottom: 12 }} />
              )}
              <Form.Item name="permissions">
                <PermissionTreeField modules={tree.data} />
              </Form.Item>
            </Card>
          </Col>
        </Row>
      </Form>
    </>
  );
};
