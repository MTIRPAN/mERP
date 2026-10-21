import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { EmployeeDetails } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { ContactsFormDrawer } from './ContactsFormDrawer';

const mutateAsync = vi.fn().mockResolvedValue(undefined);

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useUpdateEmployeeContacts: () => ({ mutateAsync }),
  getGetEmployeeQueryKey: (id: string) => [`/api/hr/employees/${id}`],
  getListEmployeesQueryKey: () => ['/api/hr/employees'],
  getListPositionsQueryKey: () => ['/api/hr/positions'],
}));

const texts: Record<string, string> = {
  'web.common.save': 'Kaydet',
  'web.hr.employees.contacts.add_phone': 'Telefon ekle',
  'web.hr.employees.contacts.phone': 'Telefon',
};

const employee = {
  id: 'e1',
  version: 7,
  emails: [{ kind: 'Work', address: 'ayse@acme.test', isPrimary: true }],
  phones: [],
  addresses: [
    { kind: 'Home', line: 'Bağdat Cd. 1', district: null, city: 'İstanbul', postalCode: null, countryCode: 'TR' },
  ],
  emergencyContacts: [],
} as unknown as EmployeeDetails;

describe('ContactsFormDrawer', () => {
  it('sends every list with the card version, new rows included', async () => {
    renderWithProviders(
      <QueryClientProvider client={new QueryClient()}>
        <ContactsFormDrawer open employee={employee} onClose={() => undefined} />
      </QueryClientProvider>,
      texts,
    );

    await userEvent.click(screen.getByRole('button', { name: /Telefon ekle/ }));
    await userEvent.type(screen.getByLabelText(/^Telefon/), ' 0555 111 22 33 ');
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }));

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({
        id: 'e1',
        data: {
          version: 7,
          emails: [{ kind: 'Work', address: 'ayse@acme.test', isPrimary: true }],
          phones: [{ kind: 'Mobile', number: '0555 111 22 33', isPrimary: false }],
          addresses: [
            {
              kind: 'Home',
              line: 'Bağdat Cd. 1',
              district: null,
              city: 'İstanbul',
              postalCode: null,
              countryCode: 'TR',
            },
          ],
          emergencyContacts: [],
        },
      }),
    );
  });
});
