import { BellOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Badge, Button, Flex, Popover, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import {
  getCountMyUnreadNotificationsQueryKey,
  getListMyNotificationsQueryKey,
  useCountMyUnreadNotifications,
  useListMyNotifications,
  useMarkAllNotificationsRead,
} from '@/shared/api/generated/endpoints/platform/platform';
import { NotificationList } from './NotificationList';
import { useOpenNotification } from './useOpenNotification';

const LATEST = 8;

/** The bell in the header (PLAN P8): unread count, the latest notifications and "mark all read". */
export const NotificationBell = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const unread = useCountMyUnreadNotifications().data?.count ?? 0;
  const latest = useListMyNotifications({ pageSize: LATEST }, { query: { enabled: open } });
  const markAll = useMarkAllNotificationsRead();
  const openNotification = useOpenNotification(() => setOpen(false));

  const markAllRead = async () => {
    await markAll.mutateAsync();
    await queryClient.invalidateQueries({ queryKey: getCountMyUnreadNotificationsQueryKey() });
    await queryClient.invalidateQueries({ queryKey: getListMyNotificationsQueryKey() });
  };

  return (
    <Popover
      trigger="click"
      placement="bottomRight"
      open={open}
      onOpenChange={setOpen}
      title={
        <Flex justify="space-between" align="center" gap={16}>
          <Typography.Text strong>{t('web.shell.header.notifications')}</Typography.Text>
          <Button
            type="link"
            size="small"
            disabled={unread === 0}
            loading={markAll.isPending}
            onClick={() => void markAllRead()}
          >
            {t('web.notifications.mark_all_read')}
          </Button>
        </Flex>
      }
      content={
        <div style={{ width: 360 }}>
          <div style={{ maxHeight: 420, overflowY: 'auto' }}>
            <NotificationList
              items={latest.data?.items ?? []}
              loading={latest.isLoading}
              onOpen={(item) => void openNotification(item).catch(() => undefined)}
            />
          </div>
          <Button
            type="link"
            block
            onClick={() => {
              setOpen(false);
              void navigate('/notifications');
            }}
          >
            {t('web.notifications.see_all')}
          </Button>
        </div>
      }
    >
      <Badge count={unread} size="small" overflowCount={99}>
        <Button type="text" shape="circle" icon={<BellOutlined />} aria-label={t('web.shell.header.notifications')} />
      </Badge>
    </Popover>
  );
};
