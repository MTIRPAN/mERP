import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { CareerOpeningDetails, CareerPage as CareerPageData } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { CareerOpeningPage } from './CareerOpeningPage';
import { CareerPage } from './CareerPage';

const submit = vi.fn().mockResolvedValue(undefined);

const opening = {
  id: 'o1',
  title: 'Satış Uzmanı',
  companyId: 'c1',
  companyName: 'Acme',
  branchId: 'b1',
  branchName: 'Gebze Fabrika',
  workModel: 'Hybrid',
  employmentType: 'Indefinite',
  visibility: 'Public',
  publishFrom: '2026-10-20',
  publishUntil: null,
} as const;

const details: CareerOpeningDetails = {
  opening,
  description: '<p>Satış ekibimize katılacak arkadaş arıyoruz.</p>',
  qualifications: null,
  form: {
    requirePhone: true,
    requireCv: false,
    askCoverLetter: false,
    questions: [{ id: 'q1', text: 'Askerlik görevinizi tamamladınız mı?', type: 'YesNo', required: true, options: [] }],
    cvExtensions: ['docx', 'pdf'],
    cvMaxBytes: 5 * 1024 * 1024,
  },
  disclosure: { kind: 'Disclosure', revision: 2, culture: 'tr-TR', body: 'Aydınlatma metninin tamamı' },
  poolConsent: { kind: 'ExplicitConsent', revision: 1, culture: 'tr-TR', body: 'Açık rıza metni' },
};

const page: CareerPageData = {
  companies: [{ id: 'c1', name: 'Acme', hasLogo: false }],
  openings: [
    opening,
    { ...opening, id: 'o2', title: 'Muhasebe Uzmanı', workModel: 'OnSite', branchId: null, branchName: null },
  ],
};

vi.mock('@/shared/api/generated/endpoints/careers/careers', () => ({
  useGetCareerPage: () => ({ data: page, isLoading: false, isError: false }),
  useGetCareerOpening: () => ({ data: details, isError: false }),
  getGetCareerCompanyLogoUrl: (tenant: string, id: string) => `/api/public/${tenant}/careers/companies/${id}/logo`,
  submitCareerApplication: (...args: unknown[]) => submit(...args) as Promise<void>,
}));

const texts: Record<string, string> = {
  'web.recruitment.careers.apply': 'Başvur',
  'web.recruitment.careers.search': 'Ara',
  'web.recruitment.careers.fields.first_name': 'Ad',
  'web.recruitment.careers.fields.last_name': 'Soyad',
  'web.recruitment.careers.fields.email': 'E-posta',
  'web.recruitment.careers.fields.phone': 'Telefon',
  'web.recruitment.careers.disclosure': 'Aydınlatma metnini okudum',
  'web.recruitment.careers.read_text': '(metni oku)',
  'web.recruitment.careers.consent_required': 'Aydınlatma metnini onaylayın',
  'web.recruitment.careers.consent_titles.Disclosure': 'Aydınlatma metni',
  'web.recruitment.careers.sent_title': 'Başvurunuz alındı',
  'web.common.yes': 'Evet',
  'web.common.no': 'Hayır',
  'web.platform.fields.required': 'Zorunlu alan',
};

const render = (route: string) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <Routes>
        <Route path="/careers/:tenant" element={<CareerPage />} />
        <Route path="/careers/:tenant/openings/:id" element={<CareerOpeningPage />} />
      </Routes>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('Career page', () => {
  beforeEach(() => submit.mockClear());

  it('lists the openings and filters them by the text typed', async () => {
    render('/careers/acme');

    expect(screen.getByText('Satış Uzmanı')).toBeInTheDocument();
    expect(screen.getByText('Muhasebe Uzmanı')).toBeInTheDocument();
    await userEvent.type(screen.getByPlaceholderText('Ara'), 'muhasebe');
    expect(screen.queryByText('Satış Uzmanı')).not.toBeInTheDocument();
  });

  it('refuses to send without the KVKK consent, then sends the application with the text revisions', async () => {
    render('/careers/acme/openings/o1');

    await userEvent.type(screen.getByLabelText(/^Ad/), 'Ayşe');
    await userEvent.type(screen.getByLabelText(/^Soyad/), 'Yılmaz');
    await userEvent.type(screen.getByLabelText(/^E-posta/), 'ayse@ornek.test');
    await userEvent.type(screen.getByLabelText(/^Telefon/), '05321234567');
    await userEvent.click(screen.getByLabelText('Evet'));
    await userEvent.click(screen.getByRole('button', { name: 'Başvur' }));
    expect(await screen.findByText('Aydınlatma metnini onaylayın')).toBeInTheDocument();
    expect(submit).not.toHaveBeenCalled();

    await userEvent.click(screen.getAllByText('(metni oku)', { selector: 'a' })[0]!);
    expect(await screen.findByText('Aydınlatma metninin tamamı')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    await userEvent.click(screen.getByRole('checkbox', { name: /Aydınlatma metnini okudum/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Başvur' }));

    await waitFor(() => expect(submit).toHaveBeenCalled());
    const [tenant, id, body] = submit.mock.calls[0] as [string, string, { application: string }];
    expect([tenant, id]).toEqual(['acme', 'o1']);
    expect(JSON.parse(body.application)).toMatchObject({
      firstName: 'Ayşe',
      email: 'ayse@ornek.test',
      answers: [{ questionId: 'q1', values: ['yes'] }],
      disclosureAccepted: true,
      disclosureRevision: 2,
      poolConsent: false,
      poolConsentRevision: null,
    });
    expect(await screen.findByText('Başvurunuz alındı')).toBeInTheDocument();
  });
});
