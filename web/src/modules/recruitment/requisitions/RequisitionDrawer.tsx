import { App, Button, Col, DatePicker, Drawer, Flex, Form, Input, InputNumber, Row, Segmented, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { useMe } from '@/app/me/meContext';
import {
  createRequisition,
  updateRequisition,
  useListRequisitionPositions,
  useListRequisitionUnits,
  useSearchRequisitionEmployees,
  useSearchRequisitionUsers,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type {
  CreateRequisitionCommand,
  RequisitionDetails,
  RequisitionEmploymentType,
  RequisitionReason,
} from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import { employmentTypes, requisitionReasons, useRequisitionRefresh, useSettled } from './requisitions';

const CURRENCIES = ['TRY', 'USD', 'EUR'];

type Kind = 'existing' | 'new';

interface RequisitionValues {
  companyId?: string;
  orgUnitId?: string;
  positionId?: string;
  newPositionTitle?: string;
  newPositionLevel?: string;
  headcount?: number;
  reason?: RequisitionReason;
  replacedEmployeeId?: string;
  employmentType?: RequisitionEmploymentType;
  desiredStartDate?: Dayjs;
  salaryMin?: number;
  salaryMax?: number;
  currency?: string;
  justification?: string;
  hiringManagerUserId?: string;
}

/** Fields the API may name in its errors; others are shown in a message. */
const FORM_FIELDS = [
  'companyId',
  'orgUnitId',
  'positionId',
  'newPositionTitle',
  'newPositionLevel',
  'headcount',
  'reason',
  'replacedEmployeeId',
  'employmentType',
  'desiredStartDate',
  'salaryMin',
  'salaryMax',
  'currency',
  'justification',
  'hiringManagerUserId',
];

const initialValues = (details: RequisitionDetails | undefined, companyId: string | undefined): RequisitionValues =>
  details
    ? {
        companyId: details.requisition.companyId,
        orgUnitId: details.orgUnitId,
        positionId: details.requisition.positionId ?? undefined,
        newPositionTitle: details.newPositionTitle ?? undefined,
        newPositionLevel: details.newPositionLevel ?? undefined,
        headcount: details.requisition.headcount,
        reason: details.requisition.reason,
        replacedEmployeeId: details.replacedEmployeeId ?? undefined,
        employmentType: details.employmentType,
        desiredStartDate: details.requisition.desiredStartDate
          ? dayjs(details.requisition.desiredStartDate)
          : undefined,
        salaryMin: details.salaryMin ?? undefined,
        salaryMax: details.salaryMax ?? undefined,
        currency: details.currency ?? 'TRY',
        justification: details.justification,
        hiringManagerUserId: details.hiringManagerUserId,
      }
    : { companyId, headcount: 1, reason: 'NewHeadcount', employmentType: 'Indefinite', currency: 'TRY' };

const toBody = (values: RequisitionValues, kind: Kind): CreateRequisitionCommand => ({
  companyId: values.companyId ?? '',
  orgUnitId: values.orgUnitId ?? null,
  positionId: kind === 'existing' ? (values.positionId ?? null) : null,
  newPositionTitle: kind === 'new' ? values.newPositionTitle?.trim() || null : null,
  newPositionLevel: kind === 'new' ? values.newPositionLevel?.trim() || null : null,
  headcount: values.headcount ?? 0,
  reason: values.reason ?? 'NewHeadcount',
  replacedEmployeeId: values.reason === 'Replacement' ? (values.replacedEmployeeId ?? null) : null,
  employmentType: values.employmentType ?? 'Indefinite',
  desiredStartDate: values.desiredStartDate?.format('YYYY-MM-DD') ?? null,
  salaryMin: values.salaryMin ?? null,
  salaryMax: values.salaryMax ?? null,
  currency: values.salaryMin != null || values.salaryMax != null ? (values.currency ?? null) : null,
  justification: values.justification?.trim() || null,
  hiringManagerUserId: values.hiringManagerUserId ?? '',
});

interface RequisitionDrawerProps {
  /** A draft or a returned requisition to correct; a new one otherwise. */
  details?: RequisitionDetails;
  onClose: () => void;
  onSaved?: (id: string) => void;
}

/**
 * Opens or corrects a requisition (PLAN R2, D48): an existing position with the seats left after the open
 * requisitions, or a new position proposed for a unit; saved as a draft or sent for approval.
 */
export const RequisitionDrawer = ({ details, onClose, onSaved }: RequisitionDrawerProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const me = useMe();
  const activeCompanyId = useActiveCompanyId();
  const refresh = useRequisitionRefresh();
  const [form] = Form.useForm<RequisitionValues>();
  const values = Form.useWatch((all: RequisitionValues) => all, { form, preserve: true }) ?? {};
  const [kind, setKind] = useState<Kind>(details && !details.requisition.positionId ? 'new' : 'existing');
  const [saving, setSaving] = useState<'draft' | 'send'>();
  const [employeeSearch, setEmployeeSearch] = useState('');
  const [userSearch, setUserSearch] = useState('');
  const settledEmployee = useSettled(employeeSearch);
  const settledUser = useSettled(userSearch);
  const companyId = values.companyId;
  const units = useListRequisitionUnits({ companyId: companyId ?? '' }, { query: { enabled: !!companyId } });
  const positions = useListRequisitionPositions(
    { companyId: companyId ?? '', orgUnitId: values.orgUnitId },
    { query: { enabled: !!companyId && kind === 'existing' } },
  );
  const employees = useSearchRequisitionEmployees(
    { companyId: companyId ?? '', search: settledEmployee || undefined },
    { query: { enabled: !!companyId && values.reason === 'Replacement' } },
  );
  const users = useSearchRequisitionUsers({ search: settledUser || undefined });
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (send: boolean) => {
    try {
      await form.validateFields();
    } catch {
      return;
    }

    setSaving(send ? 'send' : 'draft');
    try {
      const body = { ...toBody(form.getFieldsValue(true) as RequisitionValues, kind), submit: send };
      let id = details?.requisition.id;
      if (id) {
        await updateRequisition(id, { ...body, version: details?.version ?? 0 });
      } else {
        id = (await createRequisition(body)).id;
      }

      void message.success(
        t(send ? 'web.recruitment.requisitions.saved.sent' : 'web.recruitment.requisitions.saved.draft'),
      );
      await refresh(id);
      onSaved?.(id);
      onClose();
    } catch (error) {
      if (!isApiError(error)) {
        throw error;
      }

      const fieldErrors = error.problem?.fieldErrors ?? [];
      applyApiErrorsToForm(form, error);
      const elsewhere = fieldErrors.filter((e) => !FORM_FIELDS.includes(e.field));
      if (fieldErrors.length === 0 || elsewhere.length > 0) {
        void message.error(elsewhere.length > 0 ? elsewhere.map((e) => e.message).join(' ') : error.message);
      }
    } finally {
      setSaving(undefined);
    }
  };

  const unitOptions = (units.data ?? []).map((u) => ({ value: u.orgUnitId, label: `${u.name} (${u.code})` }));
  const positionOptions = (positions.data ?? []).map((p) => ({
    value: p.positionId,
    label: t('web.recruitment.requisitions.position_option', {
      code: p.code,
      job: p.jobName,
      unit: p.orgUnitName,
      available: p.available,
      norm: p.norm,
    }),
    disabled: p.available === 0 && p.positionId !== details?.requisition.positionId,
  }));
  const employeeOptions = [
    ...(employees.data ?? []).map((e) => ({ value: e.employeeId, label: `${e.fullName} · ${e.registryNumber}` })),
    ...(details?.replacedEmployeeId &&
    details.replacedEmployeeName &&
    !(employees.data ?? []).some((e) => e.employeeId === details.replacedEmployeeId)
      ? [{ value: details.replacedEmployeeId, label: details.replacedEmployeeName }]
      : []),
  ];
  const userOptions = [
    ...(users.data ?? []).map((u) => ({ value: u.id, label: `${u.displayName} (${u.userName})` })),
    ...(details?.requisition.hiringManagerName && !(users.data ?? []).some((u) => u.id === details.hiringManagerUserId)
      ? [{ value: details.hiringManagerUserId, label: details.requisition.hiringManagerName }]
      : []),
  ];

  return (
    <Drawer
      title={
        details
          ? t('web.recruitment.requisitions.edit_title', { number: details.requisition.number })
          : t('web.recruitment.requisitions.new_title')
      }
      open
      onClose={onClose}
      size={640}
      destroyOnHidden
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose}>{t('web.common.cancel')}</Button>
          <Button loading={saving === 'draft'} disabled={!!saving} onClick={() => void submit(false)}>
            {t('web.recruitment.requisitions.save_draft')}
          </Button>
          <Button type="primary" loading={saving === 'send'} disabled={!!saving} onClick={() => void submit(true)}>
            {t('web.recruitment.requisitions.send')}
          </Button>
        </Flex>
      }
    >
      <Form<RequisitionValues>
        form={form}
        layout="vertical"
        requiredMark="optional"
        initialValues={initialValues(details, activeCompanyId ?? me.companies[0]?.id)}
      >
        <Form.Item name="companyId" label={t('web.recruitment.requisitions.fields.company')} rules={required}>
          <Select
            disabled={!!details}
            options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
            onChange={() =>
              form.setFieldsValue({ orgUnitId: undefined, positionId: undefined, replacedEmployeeId: undefined })
            }
          />
        </Form.Item>
        <Form.Item label={t('web.recruitment.requisitions.fields.kind')}>
          <Segmented<Kind>
            value={kind}
            onChange={setKind}
            options={[
              { value: 'existing', label: t('web.recruitment.requisitions.kinds.existing') },
              { value: 'new', label: t('web.recruitment.requisitions.kinds.new') },
            ]}
          />
        </Form.Item>
        <Form.Item
          name="orgUnitId"
          label={t('web.recruitment.requisitions.fields.unit')}
          rules={kind === 'new' ? required : []}
          extra={kind === 'existing' ? t('web.recruitment.requisitions.unit_filter_hint') : undefined}
        >
          <Select
            allowClear
            showSearch={{ optionFilterProp: 'label' }}
            loading={units.isFetching}
            options={unitOptions}
            onChange={() => form.setFieldsValue({ positionId: undefined })}
          />
        </Form.Item>
        {kind === 'existing' ? (
          <Form.Item
            name="positionId"
            label={t('web.recruitment.requisitions.fields.position')}
            rules={required}
            extra={t('web.recruitment.requisitions.position_hint')}
          >
            <Select
              showSearch={{ optionFilterProp: 'label' }}
              loading={positions.isFetching}
              options={positionOptions}
            />
          </Form.Item>
        ) : (
          <Row gutter={12}>
            <Col span={16}>
              <Form.Item
                name="newPositionTitle"
                label={t('web.recruitment.requisitions.fields.new_position_title')}
                rules={required}
              >
                <Input maxLength={150} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="newPositionLevel" label={t('web.recruitment.requisitions.fields.new_position_level')}>
                <Input maxLength={20} />
              </Form.Item>
            </Col>
          </Row>
        )}
        <Row gutter={12}>
          <Col span={8}>
            <Form.Item name="headcount" label={t('web.recruitment.requisitions.fields.headcount')} rules={required}>
              <InputNumber min={1} max={99} precision={0} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={16}>
            <Form.Item name="reason" label={t('web.recruitment.requisitions.fields.reason')} rules={required}>
              <Select
                options={requisitionReasons.map((r) => ({
                  value: r,
                  label: t(`web.recruitment.requisitions.reasons.${r}`),
                }))}
              />
            </Form.Item>
          </Col>
        </Row>
        {values.reason === 'Replacement' && (
          <Form.Item
            name="replacedEmployeeId"
            label={t('web.recruitment.requisitions.fields.replaced_employee')}
            rules={required}
          >
            <Select
              showSearch={{ filterOption: false, onSearch: setEmployeeSearch }}
              loading={employees.isFetching}
              options={employeeOptions}
              placeholder={t('web.recruitment.requisitions.search_person')}
            />
          </Form.Item>
        )}
        <Row gutter={12}>
          <Col span={12}>
            <Form.Item
              name="employmentType"
              label={t('web.recruitment.requisitions.fields.employment_type')}
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
          <Col span={12}>
            <Form.Item name="desiredStartDate" label={t('web.recruitment.requisitions.fields.desired_start_date')}>
              <DatePicker format="L" style={{ width: '100%' }} />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={12}>
          <Col span={9}>
            <Form.Item name="salaryMin" label={t('web.recruitment.requisitions.fields.salary_min')}>
              <InputNumber min={0} precision={2} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={9}>
            <Form.Item name="salaryMax" label={t('web.recruitment.requisitions.fields.salary_max')}>
              <InputNumber min={0} precision={2} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={6}>
            <Form.Item name="currency" label={t('web.recruitment.requisitions.fields.currency')}>
              <Select options={CURRENCIES.map((c) => ({ value: c, label: c }))} />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item
          name="hiringManagerUserId"
          label={t('web.recruitment.requisitions.fields.hiring_manager')}
          rules={required}
        >
          <Select
            showSearch={{ filterOption: false, onSearch: setUserSearch }}
            loading={users.isFetching}
            options={userOptions}
            placeholder={t('web.recruitment.requisitions.search_person')}
          />
        </Form.Item>
        <Form.Item name="justification" label={t('web.recruitment.requisitions.fields.justification')} rules={required}>
          <Input.TextArea maxLength={2000} showCount autoSize={{ minRows: 3, maxRows: 8 }} />
        </Form.Item>
      </Form>
    </Drawer>
  );
};
