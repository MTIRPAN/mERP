import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { NotificationPreferenceItem, UpdateMyNotificationPreferencesCommand } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { NotificationPreferencesCard } from './NotificationPreferencesCard';

const update = vi.fn<(variables: { data: UpdateMyNotificationPreferencesCommand }) => Promise<void>>();
const sendTest = vi.fn<() => Promise<void>>();

const preferences: NotificationPreferenceItem[] = [
  { type: 'platform.test', label: 'Test bildirimi', mandatory: false, inApp: true, email: true },
  { type: 'platform.security', label: 'Güvenlik uyarısı', mandatory: true, inApp: true, email: true },
];

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  getGetMyNotificationPreferencesQueryKey: () => ['/api/platform/notifications/preferences'],
  useGetMyNotificationPreferences: () => ({ data: preferences, dataUpdatedAt: 1 }),
  useUpdateMyNotificationPreferences: () => ({ mutateAsync: update, isPending: false }),
  useSendTestNotification: () => ({ mutateAsync: sendTest, isPending: false }),
}));

const texts: Record<string, string> = {
  'web.notifications.preferences': 'Bildirimler',
  'web.notifications.type': 'Bildirim',
  'web.notifications.in_app': 'Uygulama içi',
  'web.notifications.email': 'E-posta',
  'web.notifications.mandatory': 'Zorunlu',
  'web.notifications.preferences_saved': 'Bildirim tercihleriniz kaydedildi.',
  'web.notifications.send_test': 'Test bildirimi gönder',
  'web.notifications.test_sent': 'Test bildirimi gönderildi.',
  'web.common.save': 'Kaydet',
};

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <NotificationPreferencesCard />
    </QueryClientProvider>,
    texts,
  );

describe('NotificationPreferencesCard', () => {
  beforeEach(() => {
    update.mockReset().mockResolvedValue(undefined);
    sendTest.mockReset().mockResolvedValue(undefined);
  });

  it('saves the switched channels of every type', async () => {
    render();

    await userEvent.click(screen.getByRole('switch', { name: 'Test bildirimi: E-posta' }));
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }));

    await waitFor(() => expect(update).toHaveBeenCalledTimes(1));
    expect(update.mock.calls[0]![0].data.preferences).toEqual([
      { type: 'platform.test', inApp: true, email: false },
      { type: 'platform.security', inApp: true, email: true },
    ]);
    expect(await screen.findByText('Bildirim tercihleriniz kaydedildi.')).toBeInTheDocument();
  });

  it('cannot switch off mandatory notifications', () => {
    render();

    expect(screen.getByText('Zorunlu')).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Güvenlik uyarısı: E-posta' })).toBeDisabled();
    expect(screen.getByRole('switch', { name: 'Güvenlik uyarısı: Uygulama içi' })).toBeDisabled();
  });

  it('sends a test notification', async () => {
    render();

    await userEvent.click(screen.getByRole('button', { name: 'Test bildirimi gönder' }));

    await waitFor(() => expect(sendTest).toHaveBeenCalledTimes(1));
    expect(await screen.findByText('Test bildirimi gönderildi.')).toBeInTheDocument();
  });
});
