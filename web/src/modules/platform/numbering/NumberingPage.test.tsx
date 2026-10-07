import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { UpdateNumberSequenceMutationVariables } from '@/shared/api/generated/endpoints/platform/platform';
import type { MeResponse, NumberSequenceItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { NumberingPage } from './NumberingPage';

const updateSequence = vi.fn<(variables: UpdateNumberSequenceMutationVariables) => Promise<void>>();

const sequences: NumberSequenceItem[] = [
  {
    id: 's1',
    documentType: 'recruitment.offer',
    label: 'İş teklifi',
    companyId: null,
    companyName: null,
    pattern: 'TKL-{yyyy}-{seq:5}',
    resetPeriod: 'Yearly',
    example: 'TKL-2026-00001',
    version: 3,
  },
  {
    id: 's2',
    documentType: 'recruitment.offer',
    label: 'İş teklifi',
    companyId: 'c1',
    companyName: 'Acme',
    pattern: 'ACME-{yy}-{seq:3}',
    resetPeriod: 'Yearly',
    example: 'ACME-26-001',
    version: 1,
  },
];

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useListNumberSequences: () => ({ data: sequences }),
  getListNumberSequencesQueryKey: () => ['/api/platform/numbering/sequences'],
  useDeleteNumberSequence: () => ({ mutateAsync: vi.fn() }),
  useAddCompanyNumberSequence: () => ({ mutateAsync: vi.fn() }),
  useUpdateNumberSequence: () => ({ mutateAsync: updateSequence }),
}));

const texts: Record<string, string> = {
  'web.numbering.all_companies': 'Tüm şirketler (varsayılan)',
  'web.numbering.delete': 'Kaldır',
  'web.numbering.pattern': 'Desen',
  'web.numbering.preview': 'İlk numara:',
  'web.numbering.reset_period.Yearly': 'Her yıl',
  'web.common.edit': 'Düzenle',
  'web.common.save': 'Kaydet',
};

const me = { companies: [{ id: 'c1', name: 'Acme' }] } as unknown as MeResponse;

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={me}>
        <PermissionProvider permissions={permissions}>
          <NumberingPage />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('NumberingPage', () => {
  beforeEach(() => {
    updateSequence.mockReset().mockResolvedValue(undefined);
  });

  it('lists the default and the company patterns; only company patterns can be removed', () => {
    render(['platform.numbering.read', 'platform.numbering.manage']);

    expect(screen.getByText('Tüm şirketler (varsayılan)')).toBeInTheDocument();
    expect(screen.getByText('ACME-{yy}-{seq:3}')).toBeInTheDocument();
    expect(screen.getByText('TKL-2026-00001')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Düzenle' })).toHaveLength(2);
    expect(screen.getAllByRole('button', { name: 'Kaldır' })).toHaveLength(1);
  });

  it('is read-only without the manage permission', () => {
    render(['platform.numbering.read']);

    expect(screen.queryByRole('button', { name: 'Düzenle' })).not.toBeInTheDocument();
  });

  it('changes a pattern with a live preview', async () => {
    render(['platform.numbering.read', 'platform.numbering.manage']);

    await userEvent.click(screen.getAllByRole('button', { name: 'Düzenle' })[0]!);
    const drawer = within(await screen.findByRole('dialog'));
    const pattern = drawer.getByLabelText('Desen');
    await userEvent.clear(pattern);
    await userEvent.type(pattern, 'TKL/{{yyyy}/{{seq:4}');

    expect(drawer.getByText(`TKL/${new Date().getFullYear()}/0001`)).toBeInTheDocument();
    await userEvent.click(drawer.getByRole('button', { name: /Kaydet/ }));

    await waitFor(() =>
      expect(updateSequence).toHaveBeenCalledWith({
        id: 's1',
        data: { version: 3, pattern: 'TKL/{yyyy}/{seq:4}', resetPeriod: 'Yearly' },
      }),
    );
  });
});
