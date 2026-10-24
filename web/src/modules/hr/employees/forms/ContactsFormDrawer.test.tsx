import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { EmployeeDetails } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { ContactsFormDrawer } from './ContactsFormDrawer';

const mutateAsync = vi.fn().mockResolvedValue(undefined);
const submitChange = vi.fn().mockResolvedValue('r1');

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useUpdateEmployeeContacts: () => ({ mutateAsync }),
  useSubmitContactsChange: () => ({ mutateAsync: submitChange }),
  getListMyProfileChangesQueryKey: () => ['/api/hr/me/profile-changes'],
  getGetEmployeeQueryKey: (id: string) => [`/api/hr/employees/${id}`],
  getListEmployeesQueryKey: () => ['/api/hr/employees'],
  getListPositionsQueryKey: () => ['/api/hr/positions'],
}));

const texts: Record<string, string> = {
  'web.common.save': 'Kaydet',
  'web.hr.employees.contacts.add_phone': 'Telefon ekle',
  'web.hr.employees.contacts.phone': 'Telefon',
  'web.hr.profile_changes.request.send': 'Onaya gönder',
  'web.hr.profile_changes.note': 'Açıklama',
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

  it('sends a change request with a note instead of saving, correcting the returned one', async () => {
    const mutateCallsBefore = mutateAsync.mock.calls.length;
    renderWithProviders(
      <QueryClientProvider client={new QueryClient()}>
        <ContactsFormDrawer
          open
          employee={employee}
          onClose={() => undefined}
          request={{
            requestId: 'r1',
            initial: {
              emails: [],
              phones: [{ kind: 'Mobile', number: '0532 000 00 00', isPrimary: true }],
              addresses: [],
              emergencyContacts: [],
            },
            note: 'Yeni hat',
          }}
        />
      </QueryClientProvider>,
      texts,
    );

    expect(screen.getByLabelText(/^Telefon/)).toHaveValue('0532 000 00 00');
    await userEvent.type(screen.getByLabelText(/^Açıklama/), ' aldım');
    await userEvent.click(screen.getByRole('button', { name: 'Onaya gönder' }));

    await waitFor(() =>
      expect(submitChange).toHaveBeenCalledWith({
        data: {
          emails: [],
          phones: [{ kind: 'Mobile', number: '0532 000 00 00', isPrimary: true }],
          addresses: [],
          emergencyContacts: [],
          note: 'Yeni hat aldım',
          requestId: 'r1',
        },
      }),
    );
    // Nothing is written to the card directly
    expect(mutateAsync).toHaveBeenCalledTimes(mutateCallsBefore);
  });
});
