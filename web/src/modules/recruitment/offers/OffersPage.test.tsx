import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  OfferDetails,
  OfferResponsePage as OfferResponsePageModel,
  RecordOfferResponseCommand,
  RespondToOfferCommand,
  SubmitOfferCommand,
} from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { OfferResponsePage } from './OfferResponsePage';
import { OfferLinkPage } from './OffersPage';

const submit = vi.fn<(id: string, body: SubmitOfferCommand) => Promise<void>>();
const record = vi.fn<(id: string, body: RecordOfferResponseCommand) => Promise<void>>();
const respond = vi.fn<(tenant: string, token: string, body: RespondToOfferCommand) => Promise<void>>();

const details: OfferDetails = {
  offer: {
    id: 'f1',
    number: 'TKL-2026-00012',
    applicationId: 'a1',
    candidateId: 'c1',
    candidateName: 'Ayşe Yılmaz',
    openingId: 'o1',
    openingTitle: 'Satış Uzmanı',
    companyId: 'co1',
    positionTitle: 'Satış Uzmanı',
    startDate: '2026-12-01',
    expiresOn: '2026-11-15',
    status: 'Draft',
    isAboveBudget: false,
    sentAt: null,
    respondedAt: null,
  },
  grade: 'M2',
  salary: 90000,
  currency: 'TRY',
  period: 'Monthly',
  salaryVisible: true,
  budgetMax: 100000,
  budgetCurrency: 'TRY',
  benefits: 'Özel sağlık sigortası',
  hiringManagerUserId: 'u1',
  hiringManagerName: 'Zeynep Ak',
  requesterUserId: 'u2',
  requesterName: 'Can Demir',
  returnComment: null,
  withdrawalReason: null,
  submittedAt: null,
  decidedAt: null,
  respondedVia: null,
  respondedByName: null,
  responseNote: null,
  canEdit: true,
  canSend: false,
  canRecordResponse: false,
  canWithdraw: true,
  version: 4,
};

let current: OfferDetails = details;

const page: OfferResponsePageModel = {
  companyName: 'Acme',
  candidateName: 'Ayşe Yılmaz',
  positionTitle: 'Satış Uzmanı',
  startDate: '2026-12-01',
  salary: 90000,
  currency: 'TRY',
  period: 'Monthly',
  benefits: null,
  expiresOn: '2026-11-15',
  status: 'Sent',
  canRespond: true,
};

vi.mock('@/app/company/companyStore', () => ({ useActiveCompanyId: () => undefined }));

vi.mock('@/shared/approvals/ApprovalTimeline', () => ({ ApprovalTimeline: () => null }));

vi.mock('@/shared/components/DataTable', () => ({ DataTable: () => null }));

vi.mock('@/shared/api/generated/endpoints/recruitment/recruitment', () => ({
  useGetOffer: () => ({ data: current }),
  useGetMyHiringOffer: () => ({
    data: { ...current, salaryVisible: false, salary: null, canEdit: false, canWithdraw: false },
  }),
  submitOffer: (id: string, body: SubmitOfferCommand) => submit(id, body),
  recordOfferResponse: (id: string, body: RecordOfferResponseCommand) => record(id, body),
  sendOffer: vi.fn(),
  withdrawOffer: vi.fn(),
  downloadOfferLetter: vi.fn(),
  downloadMyHiringOfferLetter: vi.fn(),
  listOffers: vi.fn(),
  createOffer: vi.fn(),
  updateOffer: vi.fn(),
  getListOffersQueryKey: () => ['/api/recruitment/offers'],
  getGetOfferQueryKey: (id: string) => [`/api/recruitment/offers/${id}`],
  getGetApplicationQueryKey: (id: string) => [`/api/recruitment/applications/${id}`],
}));

vi.mock('@/shared/api/generated/endpoints/offers/offers', () => ({
  useGetOfferResponsePage: () => ({ data: page, isError: false, refetch: () => Promise.resolve() }),
  respondToOffer: (tenant: string, token: string, body: RespondToOfferCommand) => respond(tenant, token, body),
}));

const texts: Record<string, string> = {
  'web.recruitment.offers.submit': 'Onaya gönder',
  'web.recruitment.offers.record_response': 'Aday yanıtını kaydet',
  'web.recruitment.offers.answers.declined': 'Reddetti',
  'web.recruitment.offers.budget': 'Talepteki aylık üst sınır: {max}',
  'web.recruitment.offers.salary_hidden': 'Ücreti görme yetkiniz yok.',
  'web.recruitment.offers.periods.Monthly': 'Aylık',
  'web.recruitment.offer_response.accept': 'Teklifi kabul ediyorum',
  'web.recruitment.offer_response.note': 'Notunuz',
  'web.common.save': 'Kaydet',
};

const render = (route: string, permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <Routes>
          <Route path="/recruitment/offers/:id" element={<OfferLinkPage />} />
          <Route path="/careers/:tenant/offers/:token" element={<OfferResponsePage />} />
        </Routes>
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('OfferLinkPage', () => {
  beforeEach(() => {
    current = details;
    submit.mockReset().mockResolvedValue(undefined);
    record.mockReset().mockResolvedValue(undefined);
  });

  it('shows the salary against the budget and sends a draft for approval', async () => {
    render('/recruitment/offers/f1', ['recruitment.offers.read', 'recruitment.offers.manage']);

    const drawer = await screen.findByRole('dialog');
    expect(within(drawer).getByText(/90\.000,00 TRY/)).toBeInTheDocument();
    expect(within(drawer).getByText('Talepteki aylık üst sınır: 100.000,00 TRY')).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('button', { name: 'Onaya gönder' }));
    await userEvent.click(await screen.findByRole('button', { name: /OK|Tamam/ }));

    await waitFor(() => expect(submit).toHaveBeenCalledWith('f1', { version: 4 }));
  });

  it('records the answer HR heard from the candidate', async () => {
    current = { ...details, offer: { ...details.offer, status: 'Sent' }, canEdit: false, canRecordResponse: true };
    render('/recruitment/offers/f1', ['recruitment.offers.read', 'recruitment.offers.manage']);

    await userEvent.click(await screen.findByRole('button', { name: 'Aday yanıtını kaydet' }));
    await userEvent.click(await screen.findByText('Reddetti'));
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }));

    await waitFor(() => expect(record).toHaveBeenCalledWith('f1', { version: 4, accepted: false, note: null }));
  });

  it('lets the hiring manager read the offer from self service without the salary', async () => {
    render('/recruitment/offers/f1', ['recruitment.self.hiring']);

    const drawer = await screen.findByRole('dialog');
    expect(within(drawer).getByText('Ücreti görme yetkiniz yok.')).toBeInTheDocument();
    expect(within(drawer).queryByRole('button', { name: 'Onaya gönder' })).not.toBeInTheDocument();
  });
});

describe('OfferResponsePage', () => {
  beforeEach(() => respond.mockReset().mockResolvedValue(undefined));

  it('lets the candidate accept the offer with a note', async () => {
    render('/careers/acme/offers/tok123', []);

    expect(await screen.findByText(/90\.000,00 TRY/)).toBeInTheDocument();
    await userEvent.type(screen.getByRole('textbox', { name: 'Notunuz' }), 'Teşekkürler');
    await userEvent.click(screen.getByRole('button', { name: 'Teklifi kabul ediyorum' }));
    await userEvent.click(await screen.findByRole('button', { name: /OK|Tamam/ }));

    await waitFor(() =>
      expect(respond).toHaveBeenCalledWith('acme', 'tok123', { accepted: true, note: 'Teşekkürler' }),
    );
  });
});
