import { useCreateJobsDashboardSession } from '@/shared/api/generated/endpoints/platform/platform';

/** Opens the Hangfire dashboard: the API first issues a short-lived cookie, then the dashboard opens in a new tab. */
export const useOpenJobsDashboard = () => {
  const session = useCreateJobsDashboardSession();

  const open = async () => {
    await session.mutateAsync();
    window.open('/admin/jobs', '_blank', 'noopener');
  };

  return { open, pending: session.isPending };
};
