import { useQueryClient } from '@tanstack/react-query';
import { Button, Card, Flex, Pagination, Segmented } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getCountMyUnreadNotificationsQueryKey,
  getListMyNotificationsQueryKey,
  useCountMyUnreadNotifications,
  useListMyNotifications,
  useMarkAllNotificationsRead,
} from '@/shared/api/generated/endpoints/platform/platform';
import { PageHeader } from '@/shared/components/PageHeader';
import { NotificationList } from './NotificationList';
import { useOpenNotification } from './useOpenNotification';

const PAGE_SIZE = 20;

type Filter = 'all' | 'unread';

/** All of the caller's notifications (PLAN P8), newest first. */
export const NotificationsPage = () => {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [filter, setFilter] = useState<Filter>('all');
  const [page, setPage] = useState(1);
  const list = useListMyNotifications({ page, pageSize: PAGE_SIZE, unreadOnly: filter === 'unread' || undefined });
  const unread = useCountMyUnreadNotifications().data?.count ?? 0;
  const markAll = useMarkAllNotificationsRead();
  const openNotification = useOpenNotification();

  const markAllRead = async () => {
    await markAll.mutateAsync();
    await queryClient.invalidateQueries({ queryKey: getCountMyUnreadNotificationsQueryKey() });
    await queryClient.invalidateQueries({ queryKey: getListMyNotificationsQueryKey() });
  };

  return (
    <>
      <PageHeader
        title={t('web.notifications.title')}
        subtitle={t('web.notifications.subtitle')}
        extra={
          <Button disabled={unread === 0} loading={markAll.isPending} onClick={() => void markAllRead()}>
            {t('web.notifications.mark_all_read')}
          </Button>
        }
      />
      <Card style={{ maxWidth: 840 }}>
        <Flex vertical gap={12}>
          <Segmented<Filter>
            value={filter}
            onChange={(value) => {
              setFilter(value);
              setPage(1);
            }}
            options={[
              { value: 'all', label: t('web.notifications.all') },
              { value: 'unread', label: t('web.notifications.unread', { count: unread }) },
            ]}
          />
          <NotificationList
            items={list.data?.items ?? []}
            loading={list.isLoading}
            onOpen={(item) => void openNotification(item).catch(() => undefined)}
          />
          {(list.data?.totalCount ?? 0) > PAGE_SIZE && (
            <Pagination
              align="end"
              current={page}
              pageSize={PAGE_SIZE}
              total={list.data?.totalCount ?? 0}
              showSizeChanger={false}
              onChange={setPage}
            />
          )}
        </Flex>
      </Card>
    </>
  );
};
