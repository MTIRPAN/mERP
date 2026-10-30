import { useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Col, DatePicker, Flex, Form, Input, Modal, Row, Select, Steps } from 'antd';
import dayjs from 'dayjs';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { useMe } from '@/app/me/meContext';
import {
  getListEmployeesQueryKey,
  getListPositionsQueryKey,
  getListPreHiresQueryKey,
  useHireEmployee,
} from '@/shared/api/generated/endpoints/hr/hr';
import { useGetCustomFieldForm } from '@/shared/api/generated/endpoints/platform/platform';
import { EmploymentType, type PreHireItem } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import { DynamicFields } from '@/shared/customFields/DynamicFields';
import { toApiValues } from '@/shared/customFields/customFieldValues';
import { EMPLOYEE_ENTITY } from './employeeFilters';
import { AssignmentFields } from './forms/AssignmentFields';
import { PersonFields } from './forms/PersonFields';
import { TermsFields } from './forms/TermsFields';
import { hireSteps, stepOfField, toHireCommand, type HireFormValues } from './hireForm';

const TCKN_EXISTS = 'hr.employees.errors.tckn_exists';

interface HireWizardProps {
  open: boolean;
  onClose: () => void;
  /** Completes this pre-hire of a candidate Recruitment hires (D54); its values fill the form. */
  preHire?: PreHireItem;
  onHired?: () => void;
}

/**
 * İşe giriş (PLAN H2c, D38): the person, the employment and the first assignment, in three steps. The server checks
 * everything again; its errors open the step of the field. A TCKN already registered links to the person, whose
 * card offers rehiring.
 */
export const HireWizard = ({ open, onClose, preHire, onHired }: HireWizardProps) => {
  const { t } = useTranslation();
  const me = useMe();
  const activeCompanyId = useActiveCompanyId();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const hire = useHireEmployee();
  const [form] = Form.useForm<HireFormValues>();
  const [step, setStep] = useState(0);
  const [existingTckn, setExistingTckn] = useState<string>();
  const companyId = Form.useWatch('companyId', form);
  const fields = useGetCustomFieldForm(
    { entityType: EMPLOYEE_ENTITY, companyId },
    { query: { enabled: open && !!companyId } },
  );
  const customFields = useMemo(() => fields.data ?? [], [fields.data]);
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const next = async () => {
    try {
      await form.validateFields([...hireSteps[step]!.fields].filter((f) => f !== 'customFields'));
      setStep((current) => current + 1);
    } catch {
      // The form shows what is missing
    }
  };

  const submit = async () => {
    let values: HireFormValues;
    try {
      values = await form.validateFields();
    } catch {
      return;
    }

    setExistingTckn(undefined);
    try {
      const created = await hire.mutateAsync({
        data: {
          ...toHireCommand(values, toApiValues(customFields, values.customFields)),
          preHireId: preHire?.id ?? null,
        },
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: getListEmployeesQueryKey() }),
        queryClient.invalidateQueries({ queryKey: getListPositionsQueryKey() }),
        queryClient.invalidateQueries({ queryKey: getListPreHiresQueryKey() }),
      ]);
      void message.success(t('web.hr.employees.hired'));
      onClose();
      if (preHire) {
        onHired?.();
      } else {
        void navigate(`/hr/employees/${created.id}`);
      }
    } catch (error) {
      if (!applyApiErrorsToForm(form, error)) {
        throw error;
      }

      const fieldErrors = isApiError(error) ? (error.problem?.fieldErrors ?? []) : [];
      if (fieldErrors.some((e) => e.code === TCKN_EXISTS)) {
        setExistingTckn(values.tckn?.trim());
      }

      const first = fieldErrors[0];
      if (first) {
        setStep(stepOfField(first.field));
      }
    }
  };

  const shown = (index: number) => (index === step ? undefined : { display: 'none' });

  return (
    <Modal
      title={
        preHire
          ? t('web.hr.pre_hires.complete_title', { name: `${preHire.firstName} ${preHire.lastName}` })
          : t('web.hr.employees.hire_title')
      }
      open={open}
      onCancel={onClose}
      width={760}
      destroyOnHidden
      footer={
        <Flex justify="space-between">
          <Button onClick={onClose}>{t('web.common.cancel')}</Button>
          <Flex gap={8}>
            {step > 0 && (
              <Button onClick={() => setStep((current) => current - 1)}>{t('web.hr.employees.wizard.back')}</Button>
            )}
            {step < hireSteps.length - 1 ? (
              <Button type="primary" onClick={() => void next()}>
                {t('web.hr.employees.wizard.next')}
              </Button>
            ) : (
              <Button type="primary" loading={hire.isPending} onClick={() => void submit().catch(() => undefined)}>
                {t('web.hr.employees.hire_submit')}
              </Button>
            )}
          </Flex>
        </Flex>
      }
    >
      <Steps
        size="small"
        current={step}
        style={{ marginBottom: 24 }}
        items={hireSteps.map((s) => ({ key: s.key, title: t(`web.hr.employees.hire_steps.${s.key}`) }))}
      />
      {existingTckn && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message={t('web.hr.employees.tckn_exists_title')}
          description={
            <Link to={`/hr/employees?search=${encodeURIComponent(existingTckn)}`} onClick={onClose}>
              {t('web.hr.employees.tckn_exists_link')}
            </Link>
          }
        />
      )}
      <Form<HireFormValues>
        form={form}
        layout="vertical"
        requiredMark="optional"
        initialValues={
          preHire
            ? {
                firstName: preHire.firstName,
                lastName: preHire.lastName,
                companyId: preHire.companyId,
                hireDate: dayjs(preHire.startDate),
                employmentType: preHire.employmentType,
                nationality: 'TR',
                orgUnitId: preHire.orgUnitId ?? undefined,
                positionId: preHire.positionId ?? undefined,
              }
            : {
                companyId: activeCompanyId ?? (me.companies.length === 1 ? me.companies[0]?.id : undefined),
                hireDate: dayjs(),
                employmentType: EmploymentType.Indefinite,
                nationality: 'TR',
              }
        }
      >
        <div style={shown(0)}>
          <Row gutter={12}>
            <Col span={8}>
              <Form.Item
                name="tckn"
                label={t('web.hr.employees.fields.tckn')}
                rules={[...required, { pattern: /^\d{11}$/, message: t('web.hr.employees.tckn_format') }]}
              >
                <Input maxLength={11} inputMode="numeric" autoComplete="off" />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="firstName" label={t('web.hr.employees.fields.first_name')} rules={required}>
                <Input maxLength={100} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="lastName" label={t('web.hr.employees.fields.last_name')} rules={required}>
                <Input maxLength={100} />
              </Form.Item>
            </Col>
          </Row>
          <PersonFields />
        </div>

        <div style={shown(1)}>
          <Row gutter={12}>
            <Col span={16}>
              <Form.Item name="companyId" label={t('web.hr.employees.fields.company')} rules={required}>
                <Select
                  showSearch={{ optionFilterProp: 'label' }}
                  options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
                  onChange={() =>
                    form.setFieldsValue({ orgUnitId: undefined, positionId: undefined, branchId: undefined })
                  }
                />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="hireDate" label={t('web.hr.employees.fields.hire_date')} rules={required}>
                <DatePicker style={{ width: '100%' }} format="L" />
              </Form.Item>
            </Col>
          </Row>
          <TermsFields form={form} startField="hireDate" />
        </div>

        <div style={shown(2)}>
          <AssignmentFields form={form} companyId={companyId} enabled={open} />
          <DynamicFields fields={customFields} />
        </div>
      </Form>
    </Modal>
  );
};
