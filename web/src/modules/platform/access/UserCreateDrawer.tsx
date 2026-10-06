import { useQueryClient } from '@tanstack/react-query';
import { Alert, Col, Form, Input, Row, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { useMe } from '@/app/me/meContext';
import { getListUsersQueryKey, useCreateUser } from '@/shared/api/generated/endpoints/platform/platform';
import { FormDrawer } from '@/shared/components/FormDrawer';

interface UserCreateValues {
  userName?: string;
  email?: string;
  firstName?: string;
  lastName?: string;
  culture?: string;
}

const blank = (value?: string) => (value?.trim() ? value.trim() : null);

/** Creates the user in Keycloak; the user sets a password from the invitation e-mail. Opens the new user. */
export const UserCreateDrawer = ({ open, onClose }: { open: boolean; onClose: () => void }) => {
  const { t } = useTranslation();
  const me = useMe();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const create = useCreateUser();

  const submit = async (values: UserCreateValues) => {
    const created = await create.mutateAsync({
      data: {
        userName: blank(values.userName),
        email: blank(values.email),
        firstName: blank(values.firstName),
        lastName: blank(values.lastName),
        culture: values.culture ?? null,
      },
    });
    await queryClient.invalidateQueries({ queryKey: getListUsersQueryKey() });
    void navigate(`/access/users/${created.id}`);
  };

  const required = [{ required: true, message: t('web.platform.fields.required') }];

  return (
    <FormDrawer<UserCreateValues> title={t('web.platform.users.new')} open={open} onClose={onClose} onSubmit={submit}>
      <Alert type="info" showIcon title={t('web.platform.users.invitation_hint')} style={{ marginBottom: 16 }} />
      <Form.Item
        name="userName"
        label={t('web.platform.users.user_name')}
        tooltip={t('web.platform.users.user_name_hint')}
        rules={required}
      >
        <Input maxLength={255} autoComplete="off" />
      </Form.Item>
      <Form.Item name="email" label={t('web.platform.fields.email')} rules={required}>
        <Input maxLength={255} inputMode="email" autoComplete="off" />
      </Form.Item>
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item name="firstName" label={t('web.platform.users.first_name')} rules={required}>
            <Input maxLength={100} />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="lastName" label={t('web.platform.users.last_name')} rules={required}>
            <Input maxLength={100} />
          </Form.Item>
        </Col>
      </Row>
      <Form.Item name="culture" label={t('web.platform.users.culture')} tooltip={t('web.platform.users.culture_hint')}>
        <Select allowClear options={me.cultures.map((c) => ({ value: c.code, label: c.name }))} />
      </Form.Item>
    </FormDrawer>
  );
};
