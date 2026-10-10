import { App, Button, Card, Descriptions, Flex, Form, Select } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import { useCulturePreference } from '@/app/me/useCulturePreference';
import { PageHeader } from '@/shared/components/PageHeader';
import { NotificationPreferencesCard } from './notifications/NotificationPreferencesCard';

/** Option value for "no preference"; antd Select cannot hold null. */
const DEVICE = '';

/** The signed-in user's account and preferences (PLAN P2: Profilim). Identity data is managed in Keycloak. */
export const ProfilePage = () => {
  const { t } = useTranslation();
  const me = useMe();
  const { message } = App.useApp();
  const applyCulture = useCulturePreference();
  const [saving, setSaving] = useState(false);

  const submit = async ({ culture }: { culture: string }) => {
    setSaving(true);
    try {
      await applyCulture(culture || null);
      void message.success(t('web.profile.saved'));
    } catch {
      // Reported by ApiErrorNotifier
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <PageHeader title={t('web.profile.title')} subtitle={t('web.profile.subtitle')} />
      <Flex vertical gap={16} style={{ maxWidth: 720 }}>
        <Card title={t('web.profile.account')}>
          <Descriptions
            column={1}
            items={[
              { key: 'name', label: t('web.profile.name'), children: me.user.displayName ?? '—' },
              { key: 'userName', label: t('web.profile.user_name'), children: me.user.userName ?? me.user.subject },
              { key: 'email', label: t('web.profile.email'), children: me.user.email ?? '—' },
              { key: 'tenant', label: t('web.profile.tenant'), children: me.tenant.name },
            ]}
          />
        </Card>
        <Card title={t('web.profile.preferences')}>
          <Form<{ culture: string }>
            layout="vertical"
            initialValues={{ culture: me.preferences.preferredCulture ?? DEVICE }}
            onFinish={submit}
          >
            <Form.Item name="culture" label={t('web.profile.language')} extra={t('web.profile.language_hint')}>
              <Select
                style={{ maxWidth: 320 }}
                options={[
                  { value: DEVICE, label: t('web.profile.language_device') },
                  ...me.cultures.map((culture) => ({ value: culture.code, label: culture.name })),
                ]}
              />
            </Form.Item>
            <Button type="primary" htmlType="submit" loading={saving}>
              {t('web.common.save')}
            </Button>
          </Form>
        </Card>
        <NotificationPreferencesCard />
      </Flex>
    </>
  );
};
