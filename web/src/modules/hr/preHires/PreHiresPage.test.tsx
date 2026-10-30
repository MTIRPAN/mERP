import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { CancelPreHireCommand, PreHireItem } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { completionOf } from './preHires';
import { PreHiresPage } from './PreHiresPage';

const cancel = vi.fn<(id: string, body: CancelPreHireCommand) => Promise<void>>();

const preHire = (id: string, extra: Partial<PreHireItem> = {}): PreHireItem => ({
  id,
  applicationId: `a-${id}`,
  firstName: 'Ayşe',
  lastName: 'Yılmaz',
  email: `${id}@ornek.test`,
  phone: null,
  companyId: 'co1',
  companyName: 'Acme',
  orgUnitId: 'u1',
  orgUnitName: 'Satış Direktörlüğü',
  positionId: 'p1',
  positionTitle: 'Satış Uzmanı',
  startDate: '2026-12-01',
  employmentType: 'Indefinite',
  existingEmployeeId: null,
  existingEmployeeName: null,
  openEmploymentId: null,
  status: 'Pending',
  employeeId: null,
  completedAt: null,
  cancelReason: null,
  createdAt: '2026-10-30T09:00:00Z',
  ...extra,
});

const items = [
  preHire('h1'),
  preHire('h2', {
    firstName: 'Can',
    lastName: 'Demir',
    existingEmployeeId: 'e2',
    existingEmployeeName: 'Can Demir',
    openEmploymentId: 'm2',
  }),
];

vi.mock('@/shared/api/generated/endpoints/hr/hr', () => ({
  useListPreHires: () => ({ data: { items, page: 1, pageSize: 20, totalCount: 2 }, isLoading: false }),
  cancelPreHire: (id: string, body: CancelPreHireCommand) => cancel(id, body),
  getListPreHiresQueryKey: () => ['/api/hr/pre-hires'],
}));

vi.mock('../employees/HireWizard', () => ({ HireWizard: () => null }));
vi.mock('../employees/forms/RehireDrawer', () => ({ RehireDrawer: () => null }));
vi.mock('../employees/forms/ChangeAssignmentDrawer', () => ({ ChangeAssignmentDrawer: () => null }));

const texts: Record<string, string> = {
  'web.hr.pre_hires.complete.hire': 'İşe girişi tamamla',
  'web.hr.pre_hires.complete.assignment': 'Görev değişikliği',
  'web.hr.pre_hires.cancel': 'Ön kaydı iptal et',
  'web.hr.pre_hires.fields.cancel_reason': 'İptal nedeni',
  'web.hr.pre_hires.new_person': 'Yeni personel',
};

describe('PreHiresPage', () => {
  beforeEach(() => cancel.mockReset().mockResolvedValue(undefined));

  it('offers the hire for a new person and the assignment change for an employee of the company', async () => {
    renderWithProviders(
      <QueryClientProvider client={new QueryClient()}>
        <PreHiresPage />
      </QueryClientProvider>,
      texts,
    );

    expect(await screen.findByRole('button', { name: 'İşe girişi tamamla' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Görev değişikliği' })).toBeInTheDocument();
    expect(screen.getByText('Yeni personel')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Can Demir' })).toHaveAttribute('href', '/hr/employees/e2');
  });

  it('calls a pre-hire off with the reason', async () => {
    renderWithProviders(
      <QueryClientProvider client={new QueryClient()}>
        <PreHiresPage />
      </QueryClientProvider>,
      texts,
    );

    await userEvent.click((await screen.findAllByRole('button', { name: 'Ön kaydı iptal et' }))[0]!);
    await userEvent.type(await screen.findByRole('textbox', { name: 'İptal nedeni' }), 'Aday işe başlamadı');
    const buttons = screen.getAllByRole('button', { name: 'Ön kaydı iptal et' });
    await userEvent.click(buttons.at(-1)!);

    await waitFor(() => expect(cancel).toHaveBeenCalledWith('h1', { reason: 'Aday işe başlamadı' }));
  });
});

describe('completionOf', () => {
  it('chooses the form by the person the pre-hire is for', () => {
    expect(completionOf(preHire('x'))).toBe('hire');
    expect(completionOf(preHire('x', { existingEmployeeId: 'e1' }))).toBe('rehire');
    expect(completionOf(preHire('x', { existingEmployeeId: 'e1', openEmploymentId: 'm1' }))).toBe('assignment');
  });
});
