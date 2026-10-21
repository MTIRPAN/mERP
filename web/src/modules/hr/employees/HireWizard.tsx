import { useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App,
  Button,
  Col,
  DatePicker,
  Flex,
  Form,
  Input,
  InputNumber,
  Modal,
  Row,
  Select,
  Steps,
  TreeSelect,
} from 'antd';
import dayjs from 'dayjs';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { useMe } from '@/app/me/meContext';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  getListEmployeesQueryKey,
  getListPositionsQueryKey,
  useGetOrgUnitTree,
  useHireEmployee,
  useListPositions,
} from '@/shared/api/generated/endpoints/hr/hr';
import { useGetCustomFieldForm, useListBranches } from '@/shared/api/generated/endpoints/platform/platform';
import { EmploymentType, Gender, MaritalStatus, MilitaryStatus, PositionStatus } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { DynamicFields } from '@/shared/customFields/DynamicFields';
import { toApiValues } from '@/shared/customFields/customFieldValues';
import { buildUnitTree, toUnitOptions } from '../organization/unitTree';
import { EMPLOYEE_ENTITY } from './employeeFilters';
import { hireSteps, stepOfField, toHireCommand, type HireFormValues } from './hireForm';

const LOOKUP_SIZE = 100;
const TCKN_EXISTS = 'hr.employees.errors.tckn_exists';

const options = <T extends string>(values: Record<string, T>, prefix: string, t: (key: string) => string) =>
  Object.values(values).map((value) => ({ value, label: t(`${prefix}${value}`) }));

interface HireWizardProps {
  open: boolean;
  onClose: () => void;
}

/**
 * İşe giriş (PLAN H2c, D38): the person, the employment and the first assignment, in three steps. The server checks
 * everything again; its errors open the step of the field. A TCKN already registered links to the person, whose
 * card offers rehiring.
 */
export const HireWizard = ({ open, onClose }: HireWizardProps) => {
  const { t } = useTranslation();
  const me = useMe();
  const activeCompanyId = useActiveCompanyId();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const canReadBranches = useHasPermission(platformPermissions.branchesRead);
  const hire = useHireEmployee();
  const [form] = Form.useForm<HireFormValues>();
  const [step, setStep] = useState(0);
  const [existingTckn, setExistingTckn] = useState<string>();
  const companyId = Form.useWatch('companyId', form);
  const orgUnitId = Form.useWatch('orgUnitId', form);
  const employmentType = Form.useWatch('employmentType', form);
  const seniorityDate = Form.useWatch('seniorityDate', form);
  const hireDate = Form.useWatch('hireDate', form);

  const tree = useGetOrgUnitTree({ companyId }, { query: { enabled: open && !!companyId } });
  const positions = useListPositions(
    { companyId, orgUnitId, includeSubUnits: true, status: PositionStatus.Active, pageSize: LOOKUP_SIZE },
    { query: { enabled: open && !!companyId } },
  );
  const branches = useListBranches(
    { companyId, isActive: true, pageSize: LOOKUP_SIZE },
    { query: { enabled: open && !!companyId && canReadBranches } },
  );
  const fields = useGetCustomFieldForm(
    { entityType: EMPLOYEE_ENTITY, companyId },
    { query: { enabled: open && !!companyId } },
  );
  const customFields = useMemo(() => fields.data ?? [], [fields.data]);
  const unitOptions = useMemo(() => toUnitOptions(buildUnitTree(tree.data?.units ?? [])), [tree.data]);

  const required = [{ required: true, message: t('web.platform.fields.required') }];
  const differentSeniority = !!seniorityDate && !!hireDate && !seniorityDate.isSame(hireDate, 'day');

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
        data: toHireCommand(values, toApiValues(customFields, values.customFields)),
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: getListEmployeesQueryKey() }),
        queryClient.invalidateQueries({ queryKey: getListPositionsQueryKey() }),
      ]);
      void message.success(t('web.hr.employees.hired'));
      onClose();
      void navigate(`/hr/employees/${created.id}`);
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
      title={t('web.hr.employees.hire_title')}
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
        initialValues={{
          companyId: activeCompanyId ?? (me.companies.length === 1 ? me.companies[0]?.id : undefined),
          hireDate: dayjs(),
          employmentType: EmploymentType.Indefinite,
          nationality: 'TR',
        }}
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
          <Row gutter={12}>
            <Col span={8}>
              <Form.Item name="birthDate" label={t('web.hr.employees.fields.birth_date')}>
                <DatePicker style={{ width: '100%' }} format="L" disabledDate={(d) => d.isAfter(dayjs())} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="birthPlace" label={t('web.hr.employees.fields.birth_place')}>
                <Input maxLength={100} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item
                name="nationality"
                label={t('web.hr.employees.fields.nationality')}
                extra={t('web.hr.employees.nationality_hint')}
              >
                <Input maxLength={2} style={{ textTransform: 'uppercase' }} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={8}>
              <Form.Item name="gender" label={t('web.hr.employees.fields.gender')}>
                <Select allowClear options={options(Gender, 'web.hr.gender.', t)} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="maritalStatus" label={t('web.hr.employees.fields.marital_status')}>
                <Select allowClear options={options(MaritalStatus, 'web.hr.marital_status.', t)} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="militaryStatus" label={t('web.hr.employees.fields.military_status')}>
                <Select allowClear options={options(MilitaryStatus, 'web.hr.military_status.', t)} />
              </Form.Item>
            </Col>
          </Row>
        </div>

        <div style={shown(1)}>
          <Row gutter={12}>
            <Col span={12}>
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
            <Col span={6}>
              <Form.Item name="hireDate" label={t('web.hr.employees.fields.hire_date')} rules={required}>
                <DatePicker style={{ width: '100%' }} format="L" />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="probationEndDate" label={t('web.hr.employees.fields.probation_end_date')}>
                <DatePicker style={{ width: '100%' }} format="L" />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item name="employmentType" label={t('web.hr.employees.fields.employment_type')} rules={required}>
                <Select options={options(EmploymentType, 'web.hr.employment_type.', t)} />
              </Form.Item>
            </Col>
            {employmentType === EmploymentType.FixedTerm && (
              <Col span={12}>
                <Form.Item
                  name="contractEndDate"
                  label={t('web.hr.employees.fields.contract_end_date')}
                  rules={required}
                >
                  <DatePicker style={{ width: '100%' }} format="L" />
                </Form.Item>
              </Col>
            )}
            {employmentType === EmploymentType.PartTime && (
              <Col span={12}>
                <Form.Item name="weeklyHours" label={t('web.hr.employees.fields.weekly_hours')} rules={required}>
                  <InputNumber min={1} max={45} precision={1} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
            )}
          </Row>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item
                name="seniorityDate"
                label={t('web.hr.employees.fields.seniority_date')}
                extra={t('web.hr.employees.seniority_hint')}
              >
                <DatePicker style={{ width: '100%' }} format="L" />
              </Form.Item>
            </Col>
            {differentSeniority && (
              <Col span={12}>
                <Form.Item
                  name="seniorityReason"
                  label={t('web.hr.employees.fields.seniority_reason')}
                  rules={required}
                >
                  <Input maxLength={500} />
                </Form.Item>
              </Col>
            )}
          </Row>
        </div>

        <div style={shown(2)}>
          <Form.Item
            name="orgUnitId"
            label={t('web.hr.employees.fields.org_unit')}
            extra={t('web.hr.employees.org_unit_hint')}
          >
            <TreeSelect
              allowClear
              showSearch
              treeNodeFilterProp="title"
              treeDefaultExpandAll
              loading={tree.isLoading}
              treeData={unitOptions}
              onChange={() => form.setFieldsValue({ positionId: undefined })}
            />
          </Form.Item>
          <Form.Item name="positionId" label={t('web.hr.employees.fields.position')} rules={required}>
            <Select
              showSearch={{ optionFilterProp: 'label' }}
              loading={positions.isFetching}
              options={(positions.data?.items ?? []).map((p) => ({
                value: p.id,
                label: `${p.code} · ${p.jobName} · ${p.orgUnitName}${p.vacant > 0 ? '' : ` (${t('web.hr.employees.position_full')})`}`,
              }))}
            />
          </Form.Item>
          <Row gutter={12}>
            {canReadBranches && (
              <Col span={12}>
                <Form.Item name="branchId" label={t('web.hr.employees.fields.branch')}>
                  <Select
                    allowClear
                    showSearch={{ optionFilterProp: 'label' }}
                    loading={branches.isFetching}
                    options={(branches.data?.items ?? []).map((b) => ({ value: b.id, label: `${b.name} (${b.code})` }))}
                  />
                </Form.Item>
              </Col>
            )}
            <Col span={6}>
              <Form.Item
                name="level"
                label={t('web.hr.employees.fields.level')}
                tooltip={t('web.hr.employees.level_hint')}
              >
                <Input maxLength={10} />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item
                name="costCenterCode"
                label={t('web.hr.employees.fields.cost_center')}
                tooltip={t('web.hr.employees.cost_center_hint')}
              >
                <Input maxLength={30} />
              </Form.Item>
            </Col>
          </Row>
          <DynamicFields fields={customFields} />
        </div>
      </Form>
    </Modal>
  );
};
