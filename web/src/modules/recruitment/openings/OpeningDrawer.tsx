import { DeleteOutlined, PlusOutlined, StarFilled } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  Checkbox,
  Col,
  DatePicker,
  Divider,
  Drawer,
  Flex,
  Form,
  Input,
  InputNumber,
  Row,
  Select,
  Space,
  Switch,
  Tag,
  Typography,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { useMe } from '@/app/me/meContext';
import {
  createOpening,
  updateOpening,
  useListOpeningBranches,
  useListOpeningStageTemplates,
  useSearchOpeningUsers,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type {
  CreateOpeningCommand,
  OpeningDetails,
  OpeningLanguageCheck,
  OpeningVisibility,
  QuestionType,
  RequisitionDetails,
  RequisitionEmploymentType,
  WorkModel,
} from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import { RichTextEditor } from '@/shared/components/RichTextEditor';
import { employmentTypes, useSettled } from '../requisitions/requisitions';
import { AiDraftModal, LanguageCheckButton, LanguageCheckResult } from './OpeningTextAssist';
import {
  answersOf,
  canKnockOut,
  isChoice,
  NO,
  questionTypes,
  useOpeningRefresh,
  useOpeningTextAi,
  visibilities,
  workModels,
  YES,
} from './openings';

interface QuestionValues {
  id?: string;
  text?: string;
  type?: QuestionType;
  required?: boolean;
  options?: string[];
  acceptedAnswers?: string[];
}

interface OpeningValues {
  companyId?: string;
  requisitionId?: string;
  title?: string;
  description?: string | null;
  qualifications?: string | null;
  branchId?: string;
  workModel?: WorkModel;
  employmentType?: RequisitionEmploymentType;
  headcount?: number;
  visibility?: OpeningVisibility;
  period?: [Dayjs | null, Dayjs | null];
  stageTemplateId?: string;
  hiringManagerUserId?: string;
  recruiterUserIds?: string[];
  interviewerUserIds?: string[];
  requirePhone?: boolean;
  requireCv?: boolean;
  askCoverLetter?: boolean;
  questions?: QuestionValues[];
  /** The texts were filled from an AI draft in this edit (D57). */
  aiDrafted?: boolean;
  aiDraftReviewed?: boolean;
}

/** Fields the API may name in its errors; others are shown in a message. */
const FORM_FIELDS = new Set([
  'companyId',
  'requisitionId',
  'title',
  'description',
  'qualifications',
  'branchId',
  'workModel',
  'employmentType',
  'headcount',
  'visibility',
  'publishFrom',
  'publishUntil',
  'stageTemplateId',
  'hiringManagerUserId',
  'recruiterUserIds',
  'interviewerUserIds',
]);

/** API fields shown on another form field: the dates on the period, a question on its text. */
const ALIASES: Record<string, (string | number)[]> = {
  publishFrom: ['period'],
  publishUntil: ['period'],
  ...Object.fromEntries(Array.from({ length: 20 }, (_, i) => [`questions[${i}]`, ['questions', i, 'text']])),
};

/** "PZ-01 · Satış Uzmanı" → "Satış Uzmanı": an existing position's title without its code. */
const jobTitle = (requisition: RequisitionDetails) =>
  requisition.newPositionTitle ?? requisition.requisition.positionTitle.split(' · ').at(-1) ?? '';

const initialValues = (
  details: OpeningDetails | undefined,
  requisition: RequisitionDetails | undefined,
  companyId: string | undefined,
): OpeningValues => {
  if (details) {
    const opening = details.opening;
    return {
      companyId: opening.companyId,
      requisitionId: opening.requisitionId ?? undefined,
      title: opening.title,
      description: details.description,
      qualifications: details.qualifications,
      branchId: details.branchId ?? undefined,
      workModel: opening.workModel,
      employmentType: opening.employmentType,
      headcount: opening.headcount,
      visibility: opening.visibility,
      period: [
        opening.publishFrom ? dayjs(opening.publishFrom) : null,
        opening.publishUntil ? dayjs(opening.publishUntil) : null,
      ],
      stageTemplateId: details.stageTemplateId,
      hiringManagerUserId: details.hiringManager.userId,
      recruiterUserIds: details.recruiters.map((m) => m.userId),
      interviewerUserIds: details.interviewers.map((m) => m.userId),
      requirePhone: details.form.requirePhone,
      requireCv: details.form.requireCv,
      askCoverLetter: details.form.askCoverLetter,
      questions: details.form.questions.map((q) => ({
        id: q.id,
        text: q.text,
        type: q.type,
        required: q.required,
        options: [...q.options],
        acceptedAnswers: [...(q.acceptedAnswers ?? [])],
      })),
    };
  }

  return {
    companyId: requisition?.requisition.companyId ?? companyId,
    requisitionId: requisition?.requisition.id,
    title: requisition ? jobTitle(requisition) : undefined,
    workModel: 'OnSite',
    employmentType: requisition?.employmentType ?? 'Indefinite',
    headcount: requisition ? Math.max(requisition.requisition.headcount - requisition.requisition.filledCount, 1) : 1,
    visibility: 'Public',
    hiringManagerUserId: requisition?.hiringManagerUserId,
    recruiterUserIds: [],
    interviewerUserIds: [],
    requirePhone: true,
    requireCv: true,
    askCoverLetter: false,
    questions: [],
  };
};

const toBody = (values: OpeningValues): CreateOpeningCommand => ({
  companyId: values.companyId ?? '',
  requisitionId: values.requisitionId ?? null,
  title: values.title?.trim() || null,
  description: values.description ?? null,
  qualifications: values.qualifications ?? null,
  branchId: values.branchId ?? null,
  workModel: values.workModel ?? 'OnSite',
  employmentType: values.employmentType ?? 'Indefinite',
  headcount: values.headcount ?? 0,
  visibility: values.visibility ?? 'Public',
  publishFrom: values.period?.[0]?.format('YYYY-MM-DD') ?? null,
  publishUntil: values.period?.[1]?.format('YYYY-MM-DD') ?? null,
  stageTemplateId: values.stageTemplateId ?? null,
  hiringManagerUserId: values.hiringManagerUserId ?? '',
  recruiterUserIds: values.recruiterUserIds ?? [],
  interviewerUserIds: values.interviewerUserIds ?? [],
  requirePhone: values.requirePhone ?? false,
  requireCv: values.requireCv ?? false,
  askCoverLetter: values.askCoverLetter ?? false,
  aiDrafted: values.aiDrafted ?? false,
  aiDraftReviewed: values.aiDraftReviewed ?? false,
  questions: (values.questions ?? []).map((q) => ({
    id: q.id ?? null,
    text: q.text?.trim() || null,
    type: q.type ?? 'Text',
    required: q.required ?? false,
    options: isChoice(q.type) ? (q.options ?? []) : [],
    acceptedAnswers: canKnockOut(q.type) ? (q.acceptedAnswers ?? []) : [],
  })),
});

/** The questions of the application form; yes/no and choice questions may knock out other answers. */
const QuestionsField = () => {
  const { t } = useTranslation();
  const form = Form.useFormInstance<OpeningValues>();
  const questions = Form.useWatch('questions', form) ?? [];
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  return (
    <Form.List name="questions">
      {(fields, { add, remove }) => (
        <Flex vertical gap={8}>
          {fields.map((field, index) => {
            const question = questions[index] ?? {};
            const answers = answersOf(question);
            return (
              <Card
                key={field.key}
                size="small"
                title={t('web.recruitment.openings.question_n', { n: index + 1 })}
                extra={
                  <Button
                    type="text"
                    danger
                    icon={<DeleteOutlined />}
                    aria-label={t('web.recruitment.openings.remove_question')}
                    onClick={() => remove(field.name)}
                  />
                }
              >
                <Form.Item
                  name={[field.name, 'text']}
                  label={t('web.recruitment.openings.fields.question')}
                  rules={required}
                >
                  <Input maxLength={300} />
                </Form.Item>
                <Row gutter={12}>
                  <Col span={14}>
                    <Form.Item name={[field.name, 'type']} label={t('web.recruitment.openings.fields.question_type')}>
                      <Select
                        options={questionTypes.map((type) => ({
                          value: type,
                          label: t(`web.recruitment.openings.question_types.${type}`),
                        }))}
                        onChange={() => form.setFieldValue(['questions', field.name, 'acceptedAnswers'], [])}
                      />
                    </Form.Item>
                  </Col>
                  <Col span={10}>
                    <Form.Item
                      name={[field.name, 'required']}
                      valuePropName="checked"
                      label={t('web.recruitment.openings.fields.question_required')}
                    >
                      <Switch />
                    </Form.Item>
                  </Col>
                </Row>
                {isChoice(question.type) && (
                  <Form.Item
                    name={[field.name, 'options']}
                    label={t('web.recruitment.openings.fields.options')}
                    extra={t('web.recruitment.openings.options_hint')}
                    rules={required}
                  >
                    <Select mode="tags" open={false} tokenSeparators={[';']} />
                  </Form.Item>
                )}
                {canKnockOut(question.type) && (
                  <Form.Item
                    name={[field.name, 'acceptedAnswers']}
                    label={t('web.recruitment.openings.fields.accepted_answers')}
                    extra={t('web.recruitment.openings.knockout_hint')}
                  >
                    <Checkbox.Group
                      options={answers.map((a) => ({
                        value: a,
                        label: a === YES || a === NO ? t(`web.recruitment.openings.answers.${a}`) : a,
                      }))}
                    />
                  </Form.Item>
                )}
              </Card>
            );
          })}
          {fields.length < 20 && (
            <Button
              type="dashed"
              icon={<PlusOutlined />}
              onClick={() => add({ type: 'Text', required: false, options: [], acceptedAnswers: [] })}
            >
              {t('web.recruitment.openings.add_question')}
            </Button>
          )}
        </Flex>
      )}
    </Form.List>
  );
};

interface OpeningDrawerProps {
  /** An opening to correct; a new one otherwise. */
  details?: OpeningDetails;
  /** The approved requisition a new opening is for: its company, title, people and hiring manager are filled in. */
  requisition?: RequisitionDetails;
  onClose: () => void;
  onSaved?: (id: string) => void;
}

/**
 * Opens or corrects a job opening (PLAN R3a, D49): what is offered and asked (rich text), where and how, the
 * pipeline copied from a stage template, the hiring team and the application form with its questions.
 */
export const OpeningDrawer = ({ details, requisition, onClose, onSaved }: OpeningDrawerProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const me = useMe();
  const activeCompanyId = useActiveCompanyId();
  const refresh = useOpeningRefresh();
  const [form] = Form.useForm<OpeningValues>();
  const companyId = Form.useWatch('companyId', form);
  const templateId = Form.useWatch('stageTemplateId', form);
  // No form item holds it, so the watch keeps values set from code
  const aiDrafted = Form.useWatch('aiDrafted', { form, preserve: true });
  const canDraft = useOpeningTextAi();
  const [drafting, setDrafting] = useState(false);
  const [languageCheck, setLanguageCheck] = useState<OpeningLanguageCheck>();
  const [saving, setSaving] = useState(false);
  const [userSearch, setUserSearch] = useState('');
  const settledUser = useSettled(userSearch);
  const branches = useListOpeningBranches({ companyId: companyId ?? '' }, { query: { enabled: !!companyId } });
  const templates = useListOpeningStageTemplates();
  const users = useSearchOpeningUsers({ search: settledUser || undefined });
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  // Users already in the team keep their names while the search shows others
  const known = useMemo(() => {
    const names = new Map<string, string>();
    if (details) {
      for (const member of [details.hiringManager, ...details.recruiters, ...details.interviewers]) {
        names.set(member.userId, member.displayName ?? member.userId);
      }
    }

    if (requisition?.requisition.hiringManagerName) {
      names.set(requisition.hiringManagerUserId, requisition.requisition.hiringManagerName);
    }

    for (const user of users.data ?? []) {
      names.set(user.id, `${user.displayName} (${user.userName})`);
    }

    return names;
  }, [details, requisition, users.data]);
  const userOptions = [...known].map(([value, label]) => ({ value, label }));
  const userSelect = {
    showSearch: { filterOption: false, onSearch: setUserSearch },
    loading: users.isFetching,
    options: userOptions,
    placeholder: t('web.recruitment.requisitions.search_person'),
  } as const;

  const stagesOf = (id: string | undefined) =>
    (templates.data ?? []).find((tpl) => tpl.id === id) ?? (templates.data ?? []).find((tpl) => tpl.isDefault);
  const stages = details && !details.canChangeStages ? details.stages : (stagesOf(templateId)?.stages ?? []);

  const save = async () => {
    try {
      await form.validateFields();
    } catch {
      return;
    }

    setSaving(true);
    try {
      const body = toBody(form.getFieldsValue(true) as OpeningValues);
      let id = details?.opening.id;
      if (id) {
        await updateOpening(id, { ...body, version: details?.version ?? 0 });
      } else {
        id = (await createOpening(body)).id;
      }

      void message.success(t('web.recruitment.openings.saved'));
      await refresh(id);
      onSaved?.(id);
      onClose();
    } catch (error) {
      if (!isApiError(error)) {
        throw error;
      }

      const fieldErrors = error.problem?.fieldErrors ?? [];
      applyApiErrorsToForm(form, error, ALIASES);
      const elsewhere = fieldErrors.filter((e) => !FORM_FIELDS.has(e.field) && !(e.field in ALIASES));
      if (fieldErrors.length === 0 || elsewhere.length > 0) {
        void message.error(elsewhere.length > 0 ? elsewhere.map((e) => e.message).join(' ') : error.message);
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Drawer
      title={
        details
          ? t('web.recruitment.openings.edit_title', { title: details.opening.title })
          : t('web.recruitment.openings.new_title')
      }
      open
      onClose={onClose}
      size={760}
      destroyOnHidden
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose}>{t('web.common.cancel')}</Button>
          <Button type="primary" loading={saving} onClick={() => void save()}>
            {t('web.common.save')}
          </Button>
        </Flex>
      }
    >
      {drafting && (
        <AiDraftModal
          request={{
            companyId: form.getFieldValue('companyId') as string,
            requisitionId: (form.getFieldValue('requisitionId') as string | undefined) ?? null,
            title: form.getFieldValue('title') as string,
            workModel: (form.getFieldValue('workModel') as WorkModel | undefined) ?? 'OnSite',
            employmentType:
              (form.getFieldValue('employmentType') as RequisitionEmploymentType | undefined) ?? 'Indefinite',
          }}
          replaces={!!form.getFieldValue('description') || !!form.getFieldValue('qualifications')}
          onDraft={(draft) => {
            form.setFieldsValue({
              description: draft.description,
              qualifications: draft.qualifications,
              aiDrafted: true,
              aiDraftReviewed: false,
            });
            setLanguageCheck({ warnings: draft.warnings, checkedWithAi: true });
          }}
          onClose={() => setDrafting(false)}
        />
      )}
      <Form<OpeningValues>
        form={form}
        layout="vertical"
        requiredMark="optional"
        initialValues={initialValues(details, requisition, activeCompanyId ?? me.companies[0]?.id)}
      >
        {(requisition ?? details?.opening.requisitionNumber) && (
          <Typography.Paragraph type="secondary">
            {t('web.recruitment.openings.for_requisition', {
              number: requisition?.requisition.number ?? details?.opening.requisitionNumber,
            })}
          </Typography.Paragraph>
        )}
        <Row gutter={12}>
          <Col span={12}>
            <Form.Item name="companyId" label={t('web.recruitment.openings.fields.company')} rules={required}>
              <Select
                disabled={!!details || !!requisition}
                options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
                onChange={() => form.setFieldsValue({ branchId: undefined })}
              />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item name="branchId" label={t('web.recruitment.openings.fields.branch')}>
              <Select
                allowClear
                showSearch={{ optionFilterProp: 'label' }}
                loading={branches.isFetching}
                options={(branches.data ?? []).map((b) => ({ value: b.id, label: b.name }))}
              />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item name="title" label={t('web.recruitment.openings.fields.title')} rules={required}>
          <Input maxLength={150} />
        </Form.Item>
        <Row gutter={12}>
          <Col span={8}>
            <Form.Item name="workModel" label={t('web.recruitment.openings.fields.work_model')} rules={required}>
              <Select
                options={workModels.map((w) => ({ value: w, label: t(`web.recruitment.openings.work_models.${w}`) }))}
              />
            </Form.Item>
          </Col>
          <Col span={10}>
            <Form.Item
              name="employmentType"
              label={t('web.recruitment.openings.fields.employment_type')}
              rules={required}
            >
              <Select
                options={employmentTypes.map((e) => ({
                  value: e,
                  label: t(`web.recruitment.requisitions.employment_types.${e}`),
                }))}
              />
            </Form.Item>
          </Col>
          <Col span={6}>
            <Form.Item name="headcount" label={t('web.recruitment.openings.fields.headcount')} rules={required}>
              <InputNumber min={1} max={99} precision={0} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={12}>
          <Col span={10}>
            <Form.Item
              name="visibility"
              label={t('web.recruitment.openings.fields.visibility')}
              rules={required}
              extra={t('web.recruitment.openings.visibility_hint')}
            >
              <Select
                options={visibilities.map((v) => ({
                  value: v,
                  label: t(`web.recruitment.openings.visibilities.${v}`),
                }))}
              />
            </Form.Item>
          </Col>
          <Col span={14}>
            <Form.Item
              name="period"
              label={t('web.recruitment.openings.fields.period')}
              extra={t('web.recruitment.openings.period_hint')}
            >
              <DatePicker.RangePicker format="L" allowEmpty={[true, true]} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
        </Row>
        <Flex gap={8} wrap style={{ marginBottom: 12 }}>
          {canDraft && (
            <Button
              size="small"
              icon={<StarFilled />}
              onClick={() =>
                void form.validateFields(['companyId', 'title']).then(
                  () => setDrafting(true),
                  () => undefined,
                )
              }
            >
              {t('web.recruitment.openings.ai_draft.button')}
            </Button>
          )}
          <LanguageCheckButton
            texts={() => form.getFieldsValue(['title', 'description', 'qualifications']) as OpeningValues}
            onChecked={setLanguageCheck}
          />
        </Flex>
        {languageCheck && (
          <div style={{ marginBottom: 12 }}>
            <LanguageCheckResult check={languageCheck} />
          </div>
        )}
        {(aiDrafted || details?.aiDraftPending) && (
          <>
            <Alert
              type="info"
              showIcon
              icon={<StarFilled />}
              style={{ marginBottom: 8 }}
              title={t('web.recruitment.openings.ai_draft.notice')}
            />
            <Form.Item
              name="aiDraftReviewed"
              valuePropName="checked"
              extra={t('web.recruitment.openings.ai_draft.review_required')}
            >
              <Checkbox>{t('web.recruitment.openings.ai_draft.reviewed')}</Checkbox>
            </Form.Item>
          </>
        )}
        <Form.Item name="description" label={t('web.recruitment.openings.fields.description')}>
          <RichTextEditor aria-label={t('web.recruitment.openings.fields.description')} />
        </Form.Item>
        <Form.Item name="qualifications" label={t('web.recruitment.openings.fields.qualifications')}>
          <RichTextEditor aria-label={t('web.recruitment.openings.fields.qualifications')} minHeight={120} />
        </Form.Item>

        <Divider titlePlacement="start">{t('web.recruitment.openings.sections.pipeline')}</Divider>
        <Form.Item
          name="stageTemplateId"
          label={t('web.recruitment.openings.fields.stage_template')}
          extra={
            details && !details.canChangeStages
              ? t('web.recruitment.openings.stages_fixed_hint')
              : t('web.recruitment.openings.stage_template_hint')
          }
        >
          <Select
            allowClear={!details}
            disabled={!!details && !details.canChangeStages}
            loading={templates.isFetching}
            placeholder={t('web.recruitment.openings.default_template')}
            options={(templates.data ?? []).map((tpl) => ({
              value: tpl.id,
              label: tpl.isDefault ? `${tpl.name} · ${t('web.recruitment.openings.default')}` : tpl.name,
            }))}
          />
        </Form.Item>
        <Space size={[4, 4]} wrap style={{ marginBottom: 16 }}>
          {stages.map((stage) => (
            <Tag key={stage.id}>{stage.name}</Tag>
          ))}
        </Space>
        <Form.Item
          name="hiringManagerUserId"
          label={t('web.recruitment.openings.fields.hiring_manager')}
          rules={required}
        >
          <Select {...userSelect} />
        </Form.Item>
        <Row gutter={12}>
          <Col span={12}>
            <Form.Item name="recruiterUserIds" label={t('web.recruitment.openings.fields.recruiters')}>
              <Select mode="multiple" {...userSelect} />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item name="interviewerUserIds" label={t('web.recruitment.openings.fields.interviewers')}>
              <Select mode="multiple" {...userSelect} />
            </Form.Item>
          </Col>
        </Row>

        <Divider titlePlacement="start">{t('web.recruitment.openings.sections.form')}</Divider>
        <Typography.Paragraph type="secondary">{t('web.recruitment.openings.form_hint')}</Typography.Paragraph>
        <Space size={24} wrap style={{ marginBottom: 16 }}>
          <Form.Item name="requirePhone" valuePropName="checked" noStyle>
            <Checkbox>{t('web.recruitment.openings.fields.require_phone')}</Checkbox>
          </Form.Item>
          <Form.Item name="requireCv" valuePropName="checked" noStyle>
            <Checkbox>{t('web.recruitment.openings.fields.require_cv')}</Checkbox>
          </Form.Item>
          <Form.Item name="askCoverLetter" valuePropName="checked" noStyle>
            <Checkbox>{t('web.recruitment.openings.fields.ask_cover_letter')}</Checkbox>
          </Form.Item>
        </Space>
        <QuestionsField />
      </Form>
    </Drawer>
  );
};
