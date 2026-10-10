import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { getAccessToken } from '@/app/auth/oidc';

export const NOTIFICATIONS_HUB_PATH = '/hubs/notifications';

/** Opens the user's real-time connection (PLAN P8); tests replace it. */
export const createNotificationsConnection = (): HubConnection =>
  new HubConnectionBuilder()
    .withUrl(NOTIFICATIONS_HUB_PATH, { accessTokenFactory: async () => (await getAccessToken()) ?? '' })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
