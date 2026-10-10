import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import {
  getCountMyUnreadNotificationsQueryKey,
  getListMyNotificationsQueryKey,
  useMarkNotificationRead,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { NotificationItem } from '@/shared/api/generated/model';

/** Opening a notification marks it read and follows its link (D32: always a path inside the application). */
export const useOpenNotification = (onOpened?: () => void) => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const markRead = useMarkNotificationRead();

  return async (item: NotificationItem) => {
    if (!item.readAt) {
      await markRead.mutateAsync({ id: item.id });
      await queryClient.invalidateQueries({ queryKey: getCountMyUnreadNotificationsQueryKey() });
      await queryClient.invalidateQueries({ queryKey: getListMyNotificationsQueryKey() });
    }

    onOpened?.();
    if (item.link) {
      void navigate(item.link);
    }
  };
};
