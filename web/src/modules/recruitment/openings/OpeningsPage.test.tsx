import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  CreateOpeningCommand,
  OpeningDetails,
  OpeningListItem,
  RequisitionDetails,
} from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { answersOf } from './openings';
import { OpeningsPage } from './OpeningsPage';

const publish = vi.fn<(id: string) => Promise<void>>().mockResolvedValue(undefined);
const create = vi.fn<(body: CreateOpeningCommand) => Promise<{ id: string }>>().mockResolvedValue({ id: 'o2' });

const item: OpeningListItem = {
  id: 'o1',
  companyId: 'c1',
  companyName: 'Acme',
  title: 'Satış Uzmanı',
  branchName: 'Gebze Fabrika',
  workModel: 'Hybrid',
  employmentType: 'Indefinite',
  headcount: 2,
  visibility: 'Public',
  status: 'Draft',
  publishFrom: null,
  publishUntil: '2026-12-31',
  requisitionId: null,
  requisitionNumber: null,
  hiringManagerName: 'Mehmet Kaya',
  createdAt: '2026-10-25T09:00:00Z',
};

const details: OpeningDetails = {
  opening: item,
  description: '<p>Satış ekibimize <strong>katılın</strong>.</p>',
  qualifications: null,
  branchId: 'b1',
  stageTemplateId: 't1',
  stages: [
    { id: 's1', name: 'Başvuru', type: 'Applied', slaDays: 3 },
    { id: 's2', name: 'İşe alındı', type: 'Hired', slaDays: null },
  ],
  hiringManager: { userId: 'u1', displayName: 'Mehmet Kaya' },
  recruiters: [],
  interviewers: [],
  form: {
    requirePhone: true,
    requireCv: true,
    askCoverLetter: false,
    questions: [
      {
        id: 'q1',
        text: 'Askerlik görevinizi tamamladınız mı?',
        type: 'YesNo',
        required: true,
        options: [],
        acceptedAnswers: ['yes'],
        isChoice: false,
        isKnockout: true,
      },
    ],
  },
  publishedAt: null,
  closedAt: null,
  closeReason: null,
  canEdit: true,
  canChangeStages: true,
  canPublish: true,
  canHold: false,
  canClose: true,
  version: 1,
};

const requisition = {
  requisition: {
    id: 'r1',
    number: 'PT-2026-00012',
    companyId: 'c1',
    positionId: 'p1',
    positionTitle: 'P-001 · Satış Uzmanı',
    headcount: 3,
    filledCount: 1,
    hiringManagerName: 'Mehmet Kaya',
  },
  newPositionTitle: null,
  employmentType: 'FixedTerm',
  hiringManagerUserId: 'u1',
} as unknown as RequisitionDetails;

vi.mock('@/app/company/companyStore', () => ({ useActiveCompanyId: () => undefined }));

vi.mock('@/app/me/meContext', () => ({ useMe: () => ({ companies: [{ id: 'c1', name: 'Acme' }] }) }));

vi.mock('@/shared/api/generated/endpoints/recruitment/recruitment', () => ({
  listOpenings: () => Promise.resolve({ items: [item], page: 1, pageSize: 20, totalCount: 1 }),
  useGetOpening: () => ({ data: details }),
  useGetRequisition: (id: string) => ({ data: id ? requisition : undefined }),
  useListOpeningBranches: () => ({ data: [], isFetching: false }),
  useListOpeningStageTemplates: () => ({
    data: [{ id: 't1', name: 'Standart işe alım', isDefault: true, stages: details.stages }],
    isFetching: false,
  }),
  useSearchOpeningUsers: () => ({ data: [], isFetching: false }),
  publishOpening: (id: string) => publish(id),
  holdOpening: vi.fn(),
  closeOpening: vi.fn(),
  createOpening: (body: CreateOpeningCommand) => create(body),
  updateOpening: vi.fn(),
  getListOpeningsQueryKey: () => ['/api/recruitment/openings'],
  getGetOpeningQueryKey: (id: string) => [`/api/recruitment/openings/${id}`],
}));

const texts: Record<string, string> = {
  'web.recruitment.openings.new_title': 'Yeni ilan',
  'web.recruitment.openings.publish': 'Yayınla',
  'web.recruitment.openings.status.Draft': 'Taslak',
  'web.recruitment.openings.fields.title': 'İlan başlığı',
  'web.recruitment.openings.fields.headcount': 'Kişi sayısı',
  'web.recruitment.openings.knockout': 'Eleme sorusu; kabul edilen cevaplar: {answers}',
  'web.recruitment.openings.answers.yes': 'Evet',
  'web.recruitment.openings.for_requisition': '{number} numaralı personel talebi için',
  'web.common.save': 'Kaydet',
};

const render = (permissions: string[], route = '/recruitment/openings') =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <Routes>
          <Route path="/recruitment/openings" element={<OpeningsPage />} />
          <Route path="/recruitment/openings/:id" element={<OpeningsPage />} />
        </Routes>
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    route,
  );

describe('OpeningsPage', () => {
  beforeEach(() => {
    publish.mockClear();
    create.mockClear();
  });

  it('lists the openings and offers a new one only to those who manage them', async () => {
    render(['recruitment.openings.read']);

    expect(await screen.findByText('Satış Uzmanı')).toBeInTheDocument();
    expect(screen.getByText('Gebze Fabrika')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni ilan/ })).not.toBeInTheDocument();
  });

  it('shows an opening as candidates will read it, and publishes it', async () => {
    render(['recruitment.openings.read', 'recruitment.openings.manage'], '/recruitment/openings/o1');

    expect(await screen.findByText('katılın')).toBeInTheDocument();
    expect(screen.getByText('Başvuru')).toBeInTheDocument();
    expect(screen.getByText('Eleme sorusu; kabul edilen cevaplar: Evet')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Yayınla' }));

    await waitFor(() => expect(publish).toHaveBeenCalledWith('o1'));
  });

  it('opens an opening of an approved requisition filled from it', async () => {
    render(['recruitment.openings.read', 'recruitment.openings.manage'], '/recruitment/openings?fromRequisition=r1');

    expect(await screen.findByText('PT-2026-00012 numaralı personel talebi için')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: /İlan başlığı/ })).toHaveValue('Satış Uzmanı');
    expect(screen.getByRole('spinbutton', { name: /Kişi sayısı/ })).toHaveValue('2');
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }));

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(
        expect.objectContaining({
          companyId: 'c1',
          requisitionId: 'r1',
          title: 'Satış Uzmanı',
          headcount: 2,
          employmentType: 'FixedTerm',
          hiringManagerUserId: 'u1',
          requirePhone: true,
          questions: [],
        }),
      ),
    );
  });
});

describe('answersOf', () => {
  it('gives yes and no, or the filled options of a choice question', () => {
    expect(answersOf({ type: 'YesNo' })).toEqual(['yes', 'no']);
    expect(answersOf({ type: 'SingleChoice', options: ['İstanbul', ' ', 'Ankara '] })).toEqual(['İstanbul', 'Ankara']);
    expect(answersOf({ type: 'Text' })).toEqual([]);
  });
});
