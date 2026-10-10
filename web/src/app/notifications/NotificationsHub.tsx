import type { HubConnection } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import {
  getCountMyUnreadNotificationsQueryKey,
  getListMyNotificationsQueryKey,
} from '@/shared/api/generated/endpoints/platform/platform';
import { createNotificationsConnection } from './notificationsConnection';

/**
 * Keeps the bell current: when the server pushes `notificationReceived` (D32), the unread count and the lists
 * are reloaded. Mounted once in the signed-in layout. Without a connection the bell still updates on focus.
 */
export const NotificationsHub = ({ connect = createNotificationsConnection }: { connect?: () => HubConnection }) => {
  const queryClient = useQueryClient();

  useEffect(() => {
    const connection = connect();
    connection.on('notificationReceived', () => {
      void queryClient.invalidateQueries({ queryKey: getCountMyUnreadNotificationsQueryKey() });
      void queryClient.invalidateQueries({ queryKey: getListMyNotificationsQueryKey() });
    });
    // A failed start only means no live updates; the bell falls back to refetching
    connection.start().catch(() => undefined);
    return () => {
      void connection.stop();
    };
  }, [connect, queryClient]);

  return null;
};
