import { DisconnectOutlined, LinkOutlined, LoadingOutlined, UserAddOutlined } from '@ant-design/icons';
import { Alert, App, Button, Card, Flex, Form, Input, Popconfirm, Select, Space, Tag, Typography } from 'antd';
import { useDeferredValue, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useCreateEmployeeUser,
  useLinkEmployeeUser,
  useSearchEmployeeUserCandidates,
  useUnlinkEmployeeUser,
} from '@/shared/api/generated/endpoints/hr/hr';
import type { EmployeeDetails } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { hrPermissions } from '../../permissions';
import { text } from '../forms/formValues';
import { useEmployeeRefresh } from '../forms/useEmployeeRefresh';

type UserForm = 'none' | 'create' | 'link';

/**
 * The person's system user on the card (PLAN H3c, D39). Platform opens and links users on HR's request, so a request
 * shows as waiting until Platform answers, and a refused one shows why.
 */
export const EmployeeUserSection = ({ employee }: { employee: EmployeeDetails }) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const canManage = useHasPermission(hrPermissions.employeesUser);
  const unlink = useUnlinkEmployeeUser();
  const refresh = useEmployeeRefresh(employee.id);
  const [form, setForm] = useState<UserForm>('none');
  const state = employee.userLinkState;
  const user = employee.user;

  if (!canManage && state !== 'Linked') {
    return null;
  }

  const remove = async () => {
    await unlink.mutateAsync({ id: employee.id });
    await refresh();
    message.info(t('web.hr.employees.user.requested'));
  };

  const close = () => setForm('none');
  return (
    <Card size="small" title={t('web.hr.employees.user.title')} style={{ minWidth: 260 }}>
      <Flex vertical gap={8}>
        {state === 'Linked' && user && (
          <Flex vertical gap={2}>
            <Space wrap>
              <Typography.Text strong>{user.userName}</Typography.Text>
              <Tag color={user.isActive ? 'success' : 'default'}>
                {t(user.isActive ? 'web.hr.employees.user.active' : 'web.hr.employees.user.inactive')}
              </Tag>
            </Space>
            {user.email && <Typography.Text type="secondary">{user.email}</Typography.Text>}
          </Flex>
        )}
        {state === 'Pending' && (
          <Space>
            <LoadingOutlined />
            <Typography.Text type="secondary">{t('web.hr.employees.user.pending')}</Typography.Text>
          </Space>
        )}
        {state === 'Failed' && (
          <Alert
            type="error"
            showIcon
            message={t('web.hr.employees.user.failed')}
            description={employee.userLinkError}
          />
        )}
        {state === 'None' && <Typography.Text type="secondary">{t('web.hr.employees.user.none')}</Typography.Text>}
        {canManage && (state === 'None' || state === 'Failed') && (
          <Space wrap size={4}>
            <Button size="small" icon={<UserAddOutlined />} onClick={() => setForm('create')}>
              {t('web.hr.employees.user.create')}
            </Button>
            <Button size="small" icon={<LinkOutlined />} onClick={() => setForm('link')}>
              {t('web.hr.employees.user.link')}
            </Button>
          </Space>
        )}
        {canManage && state === 'Linked' && (
          <Popconfirm
            title={t('web.hr.employees.user.unlink_confirm')}
            okText={t('web.hr.employees.user.unlink')}
            cancelText={t('web.common.cancel')}
            onConfirm={remove}
          >
            <Button size="small" danger icon={<DisconnectOutlined />} style={{ alignSelf: 'flex-start' }}>
              {t('web.hr.employees.user.unlink')}
            </Button>
          </Popconfirm>
        )}
      </Flex>
      {form === 'create' && <CreateUserDrawer employee={employee} onClose={close} />}
      {form === 'link' && <LinkUserDrawer employee={employee} onClose={close} />}
    </Card>
  );
};

/** Opens a user for the person with the Employee role; the e-mail defaults to the card's primary one. */
const CreateUserDrawer = ({ employee, onClose }: { employee: EmployeeDetails; onClose: () => void }) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const create = useCreateEmployeeUser();
  const refresh = useEmployeeRefresh(employee.id);
  const primary = employee.emails.find((e) => e.isPrimary) ?? employee.emails[0];

  const submit = async (values: { email?: string; userName?: string }) => {
    await create.mutateAsync({ id: employee.id, data: { email: text(values.email), userName: text(values.userName) } });
    await refresh();
    message.info(t('web.hr.employees.user.requested'));
  };

  return (
    <FormDrawer<{ email?: string; userName?: string }>
      title={t('web.hr.employees.user.create')}
      submitLabel={t('web.hr.employees.user.create_submit')}
      open
      onClose={onClose}
      onSubmit={submit}
      initialValues={{ email: primary?.address }}
    >
      <Form.Item
        name="email"
        label={t('web.hr.employees.contacts.email')}
        rules={[{ type: 'email', message: t('web.hr.employees.contacts.email_invalid') }]}
        extra={t('web.hr.employees.user.email_hint')}
      >
        <Input maxLength={200} />
      </Form.Item>
      <Form.Item
        name="userName"
        label={t('web.hr.employees.user.user_name')}
        extra={t('web.hr.employees.user.user_name_hint')}
      >
        <Input maxLength={100} autoComplete="off" />
      </Form.Item>
    </FormDrawer>
  );
};

/** Links a user that belongs to nobody yet; the search lists only those. */
const LinkUserDrawer = ({ employee, onClose }: { employee: EmployeeDetails; onClose: () => void }) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const link = useLinkEmployeeUser();
  const refresh = useEmployeeRefresh(employee.id);
  const [search, setSearch] = useState('');
  const deferred = useDeferredValue(search);
  const candidates = useSearchEmployeeUserCandidates({ search: deferred || undefined });

  const submit = async (values: { userId: string }) => {
    await link.mutateAsync({ id: employee.id, data: { userId: values.userId } });
    await refresh();
    message.info(t('web.hr.employees.user.requested'));
  };

  return (
    <FormDrawer<{ userId: string }>
      title={t('web.hr.employees.user.link')}
      submitLabel={t('web.hr.employees.user.link_submit')}
      open
      onClose={onClose}
      onSubmit={submit}
    >
      <Form.Item
        name="userId"
        label={t('web.hr.employees.user.user')}
        rules={[{ required: true, message: t('web.platform.fields.required') }]}
      >
        <Select
          showSearch={{ filterOption: false, onSearch: setSearch }}
          options={(candidates.data ?? []).map((user) => ({
            value: user.id,
            label: [user.displayName, user.email ?? user.userName].filter(Boolean).join(' · '),
          }))}
          loading={candidates.isFetching}
          placeholder={t('web.hr.employees.user.search')}
          notFoundContent={t('web.hr.employees.user.no_candidates')}
        />
      </Form.Item>
    </FormDrawer>
  );
};
