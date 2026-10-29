import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ConsentTextItem, RecruitmentLookupItem, StageTemplateItem } from '@/shared/api/generated/model';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { RecruitmentSettingsPage } from './RecruitmentSettingsPage';

const create = vi.fn().mockResolvedValue({ id: 't2' });
const createEvaluation = vi.fn().mockResolvedValue({ id: 'e2' });

const stage = (
  id: string,
  name: string,
  type: StageTemplateItem['stages'][number]['type'],
  slaDays: number | null = null,
) => ({
  id,
  name,
  names: { 'tr-TR': name },
  type,
  slaDays,
});

const template: StageTemplateItem = {
  id: 't1',
  name: 'Standart işe alım',
  names: { 'tr-TR': 'Standart işe alım' },
  isDefault: true,
  isActive: true,
  stages: [
    stage('s1', 'Başvuru', 'Applied', 3),
    stage('s2', 'Ön eleme', 'Screening', 5),
    stage('s3', 'İşe alındı', 'Hired'),
    stage('s4', 'Reddedildi', 'Rejected'),
    stage('s5', 'Geri çekildi', 'Withdrawn'),
  ],
  version: 1,
};

const reason: RecruitmentLookupItem = {
  id: 'l1',
  kind: 'RejectionReason',
  code: 'SALARY',
  name: 'Ücret beklentisi uyuşmuyor',
  names: { 'tr-TR': 'Ücret beklentisi uyuşmuyor' },
  sortOrder: 10,
  isActive: true,
  version: 1,
};

const builtIn: ConsentTextItem = {
  id: null,
  kind: 'Disclosure',
  companyId: null,
  culture: 'tr-TR',
  revision: 0,
  body: 'İşe alım süreçlerine ilişkin aydınlatma metni\n\nİkinci paragraf.',
  publishedAt: null,
};

vi.mock('@/app/me/meContext', () => ({
  useMe: () => ({ cultures: [{ code: 'tr-TR', name: 'Türkçe' }], companies: [{ id: 'c1', name: 'Acme' }] }),
}));

vi.mock('@/shared/api/generated/endpoints/recruitment/recruitment', () => ({
  useListStageTemplates: () => ({ data: [template], isLoading: false }),
  useCreateStageTemplate: () => ({ mutateAsync: create }),
  useUpdateStageTemplate: () => ({ mutateAsync: vi.fn() }),
  getListStageTemplatesQueryKey: () => ['/api/recruitment/settings/stage-templates'],
  useListRecruitmentLookups: () => ({ data: [reason], isLoading: false }),
  useCreateRecruitmentLookup: () => ({ mutateAsync: vi.fn() }),
  useUpdateRecruitmentLookup: () => ({ mutateAsync: vi.fn() }),
  getListRecruitmentLookupsQueryKey: () => ['/api/recruitment/settings/lookups'],
  useGetCurrentConsentText: () => ({ data: builtIn, isLoading: false }),
  useListConsentTexts: () => ({ data: [] }),
  usePublishConsentText: () => ({ mutateAsync: vi.fn() }),
  getListConsentTextsQueryKey: () => ['/api/recruitment/settings/consent-texts'],
  getGetCurrentConsentTextQueryKey: () => ['/api/recruitment/settings/consent-texts/current'],
  useListEvaluationTemplates: () => ({ data: [], isLoading: false }),
  useCreateEvaluationTemplate: () => ({ mutateAsync: createEvaluation }),
  useUpdateEvaluationTemplate: () => ({ mutateAsync: vi.fn() }),
  getListEvaluationTemplatesQueryKey: () => ['/api/recruitment/settings/evaluation-templates'],
  getListInterviewTemplatesQueryKey: () => ['/api/recruitment/interviews/templates'],
}));

const texts: Record<string, string> = {
  'web.recruitment.settings.tabs.stages': 'Aşama şablonları',
  'web.recruitment.settings.tabs.lists': 'Listeler',
  'web.recruitment.settings.tabs.consents': 'KVKK metinleri',
  'web.recruitment.settings.tabs.evaluations': 'Değerlendirme şablonları',
  'web.recruitment.settings.evaluation.new': 'Yeni değerlendirme şablonu',
  'web.recruitment.settings.evaluation.name': 'Şablon adı',
  'web.recruitment.settings.evaluation.section_title': 'Bölüm adı',
  'web.recruitment.settings.evaluation.criterion': 'Kriter',
  'web.recruitment.settings.evaluation.score_description': '{score} puanın anlamı',
  'web.recruitment.settings.evaluation.add_question': 'Soru ekle',
  'web.recruitment.settings.evaluation.question': 'Soru',
  'web.recruitment.settings.stage_templates.new': 'Yeni şablon',
  'web.recruitment.settings.stage_templates.default': 'Varsayılan',
  'web.recruitment.settings.stage_templates.days': '{days} gün',
  'web.recruitment.settings.stage_types.Applied': 'Başvuru',
  'web.recruitment.settings.stage_types.Screening': 'Ön eleme',
  'web.recruitment.settings.stage_types.Interview': 'Mülakat',
  'web.recruitment.settings.stage_types.Offer': 'Teklif',
  'web.recruitment.settings.stage_types.Hired': 'İşe alındı',
  'web.recruitment.settings.stage_types.Rejected': 'Reddedildi',
  'web.recruitment.settings.stage_types.Withdrawn': 'Geri çekildi',
  'web.recruitment.settings.name': 'Ad',
  'web.recruitment.settings.consents.built_in': 'Yerleşik metin',
  'web.common.save': 'Kaydet',
};

const render = (permissions: string[]) =>
  renderWithProviders(
    <QueryClientProvider client={new QueryClient()}>
      <PermissionProvider permissions={permissions}>
        <RecruitmentSettingsPage />
      </PermissionProvider>
    </QueryClientProvider>,
    texts,
    '/recruitment/settings',
  );

describe('RecruitmentSettingsPage', () => {
  beforeEach(() => {
    create.mockClear();
    createEvaluation.mockClear();
  });

  it('shows the pipeline, the lists and the built-in KVKK text to a reader', async () => {
    render(['recruitment.settings.read']);

    expect(screen.getByText('Standart işe alım')).toBeInTheDocument();
    expect(screen.getByText('Varsayılan')).toBeInTheDocument();
    expect(screen.getByText('Ön eleme · 5 gün')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Yeni şablon/ })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Listeler' }));
    expect(await screen.findByText('Ücret beklentisi uyuşmuyor')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'KVKK metinleri' }));
    expect(await screen.findByText('Yerleşik metin')).toBeInTheDocument();
    expect(screen.getByText('İkinci paragraf.')).toBeInTheDocument();
  });

  it('creates a template starting from the stages every pipeline needs', async () => {
    render(['recruitment.settings.read', 'recruitment.settings.manage']);

    await userEvent.click(screen.getByRole('button', { name: /Yeni şablon/ }));
    const drawer = await screen.findByRole('dialog');
    await userEvent.type(within(drawer).getByLabelText(/^Ad/), 'Satış ekibi');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Kaydet' }));

    await waitFor(() => expect(create).toHaveBeenCalled());
    const data = (create.mock.calls[0]![0] as { data: { name: Record<string, string>; stages: { type: string }[] } })
      .data;
    expect(data.name).toEqual({ 'tr-TR': 'Satış ekibi' });
    expect(data.stages.map((s) => s.type)).toEqual([
      'Applied',
      'Screening',
      'Interview',
      'Offer',
      'Hired',
      'Rejected',
      'Withdrawn',
    ]);
  });

  it('creates an evaluation template of weighted criteria and open questions', async () => {
    render(['recruitment.settings.read', 'recruitment.settings.manage']);

    await userEvent.click(screen.getByRole('tab', { name: 'Değerlendirme şablonları' }));
    await userEvent.click(await screen.findByRole('button', { name: /Yeni değerlendirme şablonu/ }));
    const drawer = await screen.findByRole('dialog');
    await userEvent.type(within(drawer).getByLabelText(/Şablon adı/), 'Teknik mülakat');
    await userEvent.type(within(drawer).getByRole('textbox', { name: 'Bölüm adı' }), 'Teknik');
    await userEvent.type(within(drawer).getByRole('textbox', { name: 'Kriter' }), 'Problem çözme');
    await userEvent.type(within(drawer).getByRole('textbox', { name: '5 puanın anlamı' }), 'Çok iyi');
    await userEvent.click(within(drawer).getByRole('button', { name: /Soru ekle/ }));
    await userEvent.type(within(drawer).getByRole('textbox', { name: 'Soru' }), 'En güçlü yanı ne?');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Kaydet' }));

    await waitFor(() =>
      expect(createEvaluation).toHaveBeenCalledWith({
        data: {
          name: 'Teknik mülakat',
          form: {
            sections: [
              {
                title: 'Teknik',
                criteria: [
                  {
                    id: '00000000-0000-0000-0000-000000000000',
                    name: 'Problem çözme',
                    weight: 1,
                    scoreDescriptions: ['', '', '', '', 'Çok iyi'],
                  },
                ],
              },
            ],
            questions: [{ id: '00000000-0000-0000-0000-000000000000', text: 'En güçlü yanı ne?' }],
          },
        },
      }),
    );
  });
});
