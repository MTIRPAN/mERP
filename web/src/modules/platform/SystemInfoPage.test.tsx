import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { SystemInfoPage } from './SystemInfoPage';

const createSession = vi.fn<() => Promise<void>>();

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetSystemInfo: () => ({
    data: {
      version: '1.2.3',
      environment: 'Production',
      tenantCode: 'acme',
      tenantName: 'Acme Holding',
      subject: 'sub-1',
      userName: 'admin',
      culture: 'tr-TR',
    },
  }),
  useCreateJobsDashboardSession: () => ({ mutateAsync: createSession, isPending: false }),
}));

const texts: Record<string, string> = {
  'web.system_info.tools': 'Yönetim araçları',
  'web.system_info.jobs': 'Arka plan işleri panelini aç',
  'web.shell.menu.audit': 'Denetim kayıtları',
};

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <SystemInfoPage />
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
  );

describe('SystemInfoPage', () => {
  const open = vi.fn();

  beforeEach(() => {
    createSession.mockReset().mockResolvedValue();
    vi.stubGlobal('open', open);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    open.mockReset();
  });

  it('shows the version and the tenant', () => {
    render(['platform.system.read']);

    expect(screen.getByText('1.2.3')).toBeInTheDocument();
    expect(screen.getByText('Acme Holding (acme)')).toBeInTheDocument();
    expect(screen.queryByText('Yönetim araçları')).not.toBeInTheDocument();
  });

  it('opens the jobs dashboard for users who may view jobs', async () => {
    render(['platform.system.read', 'platform.jobs.view', 'platform.audit.read']);

    expect(screen.getByRole('button', { name: /Denetim kayıtları/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Arka plan işleri panelini aç/ }));

    expect(createSession).toHaveBeenCalledTimes(1);
    expect(open).toHaveBeenCalledWith('/admin/jobs', '_blank', 'noopener');
  });
});
