import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes, useLocation } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { MeResponse, NotificationItem } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { DashboardPage } from './DashboardPage';

const notifications: NotificationItem[] = [
  {
    id: 'n1',
    type: 'platform.approval_assigned',
    title: 'Onayınızı bekleyen bir talep var',
    body: null,
    link: '/approvals',
    createdAt: new Date().toISOString(),
    readAt: null,
  },
];

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  getCountMyUnreadNotificationsQueryKey: () => ['/api/platform/notifications/unread-count'],
  getListMyNotificationsQueryKey: () => ['/api/platform/notifications'],
  useCountMyApprovalTasks: () => ({ data: { count: 3 } }),
  useCountMyUnreadNotifications: () => ({ data: { count: 2 } }),
  useListMyNotifications: () => ({ data: { items: notifications }, isLoading: false }),
  useMarkNotificationRead: () => ({ mutateAsync: vi.fn() }),
}));

const texts: Record<string, string> = {
  'web.dashboard.greeting': 'Hoş geldiniz, {name}',
  'web.dashboard.cards.approvals': 'Bekleyen onaylar',
  'web.dashboard.cards.notifications': 'Okunmamış bildirimler',
  'web.dashboard.cards.recent_notifications': 'Son bildirimler',
  'web.dashboard.cards.quick_links': 'Hızlı bağlantılar',
  'web.shell.menu.companies': 'Şirketler',
  'web.shell.user.profile': 'Profilim',
};

const me = {
  user: { displayName: 'Ayşe Yılmaz', userName: 'ayse' },
  tenant: { code: 'acme', name: 'Acme' },
  ai: { enabled: false, features: [] },
  menu: [
    {
      key: 'organization',
      labelKey: 'web.shell.menu.organization',
      path: null,
      icon: 'organization',
      ai: false,
      children: [
        {
          key: 'companies',
          labelKey: 'web.shell.menu.companies',
          path: '/organization/companies',
          icon: 'company',
          children: null,
          ai: false,
        },
      ],
    },
  ],
} as unknown as MeResponse;

const Location = () => <p data-testid="location">{useLocation().pathname}</p>;

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={me}>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="*" element={<Location />} />
        </Routes>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('DashboardPage', () => {
  it('shows what waits for the user and their latest notifications', () => {
    render();

    expect(screen.getByText('Hoş geldiniz, Ayşe Yılmaz')).toBeInTheDocument();
    expect(screen.getByText('Bekleyen onaylar').closest('.ant-card')).toHaveTextContent('3');
    expect(screen.getByText('Okunmamış bildirimler').closest('.ant-card')).toHaveTextContent('2');
    expect(screen.getByText('Onayınızı bekleyen bir talep var')).toBeInTheDocument();
  });

  it('links to the screens in the user menu and to the profile', async () => {
    render();

    expect(screen.getByRole('button', { name: /Profilim/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Şirketler/ }));

    expect(await screen.findByTestId('location')).toHaveTextContent('/organization/companies');
  });
});
