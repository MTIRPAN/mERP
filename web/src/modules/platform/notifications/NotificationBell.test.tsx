import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { NotificationItem } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { NotificationBell } from './NotificationBell';

const markRead = vi.fn<(variables: { id: string }) => Promise<void>>();
const markAll = vi.fn<() => Promise<void>>();

const items: NotificationItem[] = [
  {
    id: 'n1',
    type: 'platform.test',
    title: 'İzin talebiniz onaylandı',
    body: 'IZN-2026-000042',
    link: '/profile',
    createdAt: new Date(Date.now() - 5 * 60_000).toISOString(),
    readAt: null,
  },
  {
    id: 'n2',
    type: 'platform.test',
    title: 'Test bildirimi',
    body: null,
    link: null,
    createdAt: new Date(Date.now() - 2 * 3600_000).toISOString(),
    readAt: new Date().toISOString(),
  },
];

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  getCountMyUnreadNotificationsQueryKey: () => ['/api/platform/notifications/unread-count'],
  getListMyNotificationsQueryKey: () => ['/api/platform/notifications'],
  useCountMyUnreadNotifications: () => ({ data: { count: 1 } }),
  useListMyNotifications: () => ({ data: { items, page: 1, pageSize: 8, totalCount: 2 }, isLoading: false }),
  useMarkNotificationRead: () => ({ mutateAsync: markRead }),
  useMarkAllNotificationsRead: () => ({ mutateAsync: markAll, isPending: false }),
}));

const texts: Record<string, string> = {
  'web.shell.header.notifications': 'Bildirimler',
  'web.shell.notifications.empty': 'Yeni bildiriminiz yok',
  'web.notifications.mark_all_read': 'Tümünü okundu say',
  'web.notifications.see_all': 'Tüm bildirimler',
};

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <Routes>
        <Route path="/" element={<NotificationBell />} />
        <Route path="/profile" element={<p>Profil sayfası</p>} />
        <Route path="/notifications" element={<p>Bildirimler sayfası</p>} />
      </Routes>
    </QueryClientProvider>,
    texts,
  );

describe('NotificationBell', () => {
  beforeEach(() => {
    markRead.mockReset().mockResolvedValue(undefined);
    markAll.mockReset().mockResolvedValue(undefined);
  });

  it('shows the unread count and the latest notifications', async () => {
    render();

    expect(screen.getByText('1')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Bildirimler' }));

    expect(await screen.findByText('İzin talebiniz onaylandı')).toBeInTheDocument();
    expect(screen.getByText('IZN-2026-000042')).toBeInTheDocument();
    expect(screen.getByText('5 dakika önce')).toBeInTheDocument();
    expect(screen.getByText('Test bildirimi')).toBeInTheDocument();
  });

  it('marks an unread notification read and opens its link', async () => {
    render();
    await userEvent.click(screen.getByRole('button', { name: 'Bildirimler' }));

    await userEvent.click(await screen.findByText('İzin talebiniz onaylandı'));

    await waitFor(() => expect(markRead).toHaveBeenCalledWith({ id: 'n1' }));
    expect(await screen.findByText('Profil sayfası')).toBeInTheDocument();
  });

  it('does not mark a read notification again', async () => {
    render();
    await userEvent.click(screen.getByRole('button', { name: 'Bildirimler' }));

    await userEvent.click(await screen.findByText('Test bildirimi'));

    expect(markRead).not.toHaveBeenCalled();
  });

  it('marks all read, and leads to the full list', async () => {
    render();
    await userEvent.click(screen.getByRole('button', { name: 'Bildirimler' }));

    await userEvent.click(await screen.findByRole('button', { name: 'Tümünü okundu say' }));
    await waitFor(() => expect(markAll).toHaveBeenCalledTimes(1));

    await userEvent.click(screen.getByRole('button', { name: 'Tüm bildirimler' }));
    expect(await screen.findByText('Bildirimler sayfası')).toBeInTheDocument();
  });
});
