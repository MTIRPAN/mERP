import type { HubConnection } from '@microsoft/signalr';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { NotificationsHub } from './NotificationsHub';

vi.mock('@/app/auth/oidc', () => ({ getAccessToken: () => Promise.resolve('token') }));

/** A connection that records its handlers instead of talking to a server. */
const fakeConnection = () => {
  const handlers = new Map<string, () => void>();
  const connection = {
    on: vi.fn((method: string, handler: () => void) => handlers.set(method, handler)),
    start: vi.fn(() => Promise.resolve()),
    stop: vi.fn(() => Promise.resolve()),
  };
  return { connection, push: (method: string) => handlers.get(method)?.() };
};

describe('NotificationsHub', () => {
  it('reloads the unread count and the lists when a notification arrives', () => {
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const { connection, push } = fakeConnection();

    const { unmount } = render(
      <QueryClientProvider client={queryClient}>
        <NotificationsHub connect={() => connection as unknown as HubConnection} />
      </QueryClientProvider>,
    );
    expect(connection.start).toHaveBeenCalledTimes(1);

    push('notificationReceived');

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['/api/platform/notifications/unread-count'] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['/api/platform/notifications'] });

    unmount();
    expect(connection.stop).toHaveBeenCalledTimes(1);
  });
});
