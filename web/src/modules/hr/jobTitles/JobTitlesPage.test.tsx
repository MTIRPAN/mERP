import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { CreateJobMutationVariables } from '@/shared/api/generated/endpoints/hr/hr';
import type { ListJobsParams, PagedResultOfJobListItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { JobTitlesPage } from './JobTitlesPage';

const listJobs = vi.fn<(params: ListJobsParams) => Promise<PagedResultOfJobListItem>>();
const createJob = vi.fn<(variables: CreateJobMutationVariables) => Promise<unknown>>();

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  listJobs: (params: ListJobsParams) => listJobs(params),
  getListJobsQueryKey: () => ['/api/hr/jobs'],
  getGetJobQueryKey: (id: string) => [`/api/hr/jobs/${id}`],
  useGetJob: () => ({ data: undefined }),
  useCreateJob: () => ({ mutateAsync: createJob }),
  useUpdateJob: () => ({ mutateAsync: vi.fn() }),
}));

const texts: Record<string, string> = {
  'web.hr.job_titles.title': 'Unvanlar',
  'web.hr.job_titles.new': 'Yeni unvan',
  'web.hr.job_titles.fields.name': 'Unvan adı',
  'web.hr.job_titles.fields.family': 'Unvan ailesi',
  'web.hr.job_titles.fields.level': 'Seviye',
  'web.platform.fields.code': 'Kod',
  'web.platform.fields.required': 'Bu alan zorunludur.',
  'web.common.save': 'Kaydet',
  'web.status.active': 'Aktif',
};

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <PermissionProvider permissions={permissions}>
        <JobTitlesPage />
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
  );

describe('JobTitlesPage', () => {
  beforeEach(() => {
    listJobs.mockReset().mockResolvedValue({
      items: [
        {
          id: 'j1',
          code: 'SAT-MDR',
          name: 'Satış Müdürü',
          family: 'Satış',
          level: 'M2',
          isActive: true,
          positionCount: 3,
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    createJob.mockReset().mockResolvedValue({ id: 'j2' });
  });

  it('lists the active jobs of the catalog', async () => {
    render(['hr.org.read']);

    expect(await screen.findByText('Satış Müdürü')).toBeInTheDocument();
    expect(screen.getByText('M2')).toBeInTheDocument();
    expect(listJobs).toHaveBeenCalledWith(expect.objectContaining({ isActive: true }));
    expect(screen.queryByRole('button', { name: /Yeni unvan/ })).not.toBeInTheDocument();
  });

  it('adds a job', async () => {
    render(['hr.org.read', 'hr.org.manage']);
    await userEvent.click(await screen.findByRole('button', { name: /Yeni unvan/ }));
    const drawer = await screen.findByRole('dialog');

    await userEvent.type(within(drawer).getByLabelText('Kod'), 'uzm');
    await userEvent.type(within(drawer).getByLabelText('Unvan adı'), ' Uzman ');
    await userEvent.type(within(drawer).getByLabelText(/^Seviye/), 'U2');
    await userEvent.click(screen.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() =>
      expect(createJob).toHaveBeenCalledWith({ data: { code: 'uzm', name: 'Uzman', family: null, level: 'U2' } }),
    );
  });
});
