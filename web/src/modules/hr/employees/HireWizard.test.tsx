import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { MeResponse } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { HireWizard } from './HireWizard';

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  getListEmployeesQueryKey: () => ['/api/hr/employees'],
  getListPositionsQueryKey: () => ['/api/hr/positions'],
  useGetOrgUnitTree: () => ({ data: { units: [] }, isLoading: false }),
  useListPositions: () => ({ data: { items: [] }, isFetching: false }),
  useHireEmployee: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useGetCustomFieldForm: () => ({ data: [] }),
  useListBranches: () => ({ data: { items: [] }, isFetching: false }),
}));

const texts: Record<string, string> = {
  'web.hr.employees.hire_steps.person': 'Kişi',
  'web.hr.employees.hire_steps.employment': 'İstihdam',
  'web.hr.employees.hire_steps.assignment': 'Görev',
  'web.hr.employees.wizard.next': 'İleri',
  'web.hr.employees.wizard.back': 'Geri',
  'web.hr.employees.fields.tckn': 'T.C. kimlik no',
  'web.hr.employees.fields.first_name': 'Ad',
  'web.hr.employees.fields.last_name': 'Soyad',
  'web.hr.employees.fields.company': 'Şirket',
  'web.hr.employees.tckn_format': 'T.C. kimlik no 11 rakamdır.',
  'web.platform.fields.required': 'Zorunlu alan',
};

const render = () =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <MeContext.Provider value={{ companies: [{ id: 'c1', code: 'ACME', name: 'Acme' }] } as unknown as MeResponse}>
        <PermissionProvider permissions={['hr.employees.create']}>
          <HireWizard open onClose={() => undefined} />
        </PermissionProvider>
      </MeContext.Provider>
    </QueryClientProvider>,
    texts,
  );

describe('HireWizard', () => {
  it('moves on only when the step is complete', async () => {
    render();

    await userEvent.click(screen.getByRole('button', { name: 'İleri' }));
    expect(await screen.findAllByText('Zorunlu alan')).toHaveLength(3);

    await userEvent.type(screen.getByLabelText(/T\.C\. kimlik no/), '123');
    await userEvent.type(screen.getByLabelText(/^Ad/), 'Ayşe');
    await userEvent.type(screen.getByLabelText(/Soyad/), 'Yılmaz');
    await userEvent.click(screen.getByRole('button', { name: 'İleri' }));
    expect(await screen.findByText('T.C. kimlik no 11 rakamdır.')).toBeInTheDocument();

    await userEvent.clear(screen.getByLabelText(/T\.C\. kimlik no/));
    await userEvent.type(screen.getByLabelText(/T\.C\. kimlik no/), '10000000146');
    await userEvent.click(screen.getByRole('button', { name: 'İleri' }));

    // The employment step, with the only company chosen
    expect(await screen.findByRole('button', { name: 'Geri' })).toBeInTheDocument();
    expect(screen.getByText('Acme')).toBeInTheDocument();
  });
});
