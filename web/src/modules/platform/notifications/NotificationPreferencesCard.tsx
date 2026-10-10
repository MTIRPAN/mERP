import { useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Flex, Skeleton, Switch, Table, Tag, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getGetMyNotificationPreferencesQueryKey,
  useGetMyNotificationPreferences,
  useSendTestNotification,
  useUpdateMyNotificationPreferences,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { NotificationPreferenceItem } from '@/shared/api/generated/model';

interface PreferenceTableProps {
  initial: NotificationPreferenceItem[];
}

/** Editable copy of the preferences; mandatory types cannot be switched off. */
const PreferenceTable = ({ initial }: PreferenceTableProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const [rows, setRows] = useState(initial);
  const update = useUpdateMyNotificationPreferences();
  const sendTest = useSendTestNotification();

  const change = (type: string, channel: 'inApp' | 'email', enabled: boolean) =>
    setRows((current) => current.map((row) => (row.type === type ? { ...row, [channel]: enabled } : row)));

  const save = async () => {
    await update.mutateAsync({
      data: { preferences: rows.map(({ type, inApp, email }) => ({ type, inApp, email })) },
    });
    await queryClient.invalidateQueries({ queryKey: getGetMyNotificationPreferencesQueryKey() });
    void message.success(t('web.notifications.preferences_saved'));
  };

  const test = async () => {
    await sendTest.mutateAsync();
    void message.success(t('web.notifications.test_sent'));
  };

  const toggle = (row: NotificationPreferenceItem, channel: 'inApp' | 'email', label: string) => (
    <Switch
      size="small"
      checked={row[channel]}
      disabled={row.mandatory}
      aria-label={`${row.label}: ${label}`}
      onChange={(enabled) => change(row.type, channel, enabled)}
    />
  );

  return (
    <Flex vertical gap={12}>
      <Table<NotificationPreferenceItem>
        size="small"
        rowKey="type"
        pagination={false}
        dataSource={rows}
        columns={[
          {
            key: 'label',
            title: t('web.notifications.type'),
            render: (_, row) => (
              <Flex gap={8} align="center">
                {row.label}
                {row.mandatory && <Tag>{t('web.notifications.mandatory')}</Tag>}
              </Flex>
            ),
          },
          {
            key: 'inApp',
            title: t('web.notifications.in_app'),
            width: 140,
            render: (_, row) => toggle(row, 'inApp', t('web.notifications.in_app')),
          },
          {
            key: 'email',
            title: t('web.notifications.email'),
            width: 140,
            render: (_, row) => toggle(row, 'email', t('web.notifications.email')),
          },
        ]}
      />
      <Flex gap={8}>
        <Button type="primary" loading={update.isPending} onClick={() => void save().catch(() => undefined)}>
          {t('web.common.save')}
        </Button>
        <Button loading={sendTest.isPending} onClick={() => void test().catch(() => undefined)}>
          {t('web.notifications.send_test')}
        </Button>
      </Flex>
    </Flex>
  );
};

/** Which notifications the user gets in the application and by e-mail (PLAN P8). */
export const NotificationPreferencesCard = () => {
  const { t } = useTranslation();
  const preferences = useGetMyNotificationPreferences();

  return (
    <Card title={t('web.notifications.preferences')}>
      <Typography.Paragraph type="secondary">{t('web.notifications.preferences_hint')}</Typography.Paragraph>
      {preferences.data ? (
        <PreferenceTable key={preferences.dataUpdatedAt} initial={preferences.data} />
      ) : (
        <Skeleton active />
      )}
    </Card>
  );
};
