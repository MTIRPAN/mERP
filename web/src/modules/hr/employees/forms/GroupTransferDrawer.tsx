import { Alert, Col, DatePicker, Divider, Form, Row, Select, Switch } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import { useGroupTransferEmployee, useListSgkExitCodes } from '@/shared/api/generated/endpoints/hr/hr';
import { EmploymentType, type EmploymentItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { AssignmentFields, type AssignmentValues } from './AssignmentFields';
import { day } from './formValues';
import { TRANSFER_EXIT_CODE, toAssignment, toTerms } from './lifecycle';
import { TermsFields, type TermsValues } from './TermsFields';
import { useEmployeeRefresh } from './useEmployeeRefresh';

interface GroupTransferValues extends AssignmentValues, TermsValues {
  targetCompanyId?: string;
  transferDate?: Dayjs | null;
  sgkExitCode?: string;
  keepSeniority?: boolean;
  transferLeaveBalance?: boolean;
}

interface GroupTransferDrawerProps {
  employeeId: string;
  employment: EmploymentItem;
  open: boolean;
  onClose: () => void;
}

/**
 * Grup içi nakil (D38): the employment ends the day before the transfer date and a new one starts in the target
 * company on it. Seniority is kept by default; carrying the leave balance over is applied by the leave module.
 */
export const GroupTransferDrawer = ({ employeeId, employment, open, onClose }: GroupTransferDrawerProps) => {
  const { t } = useTranslation();
  const me = useMe();
  const [form] = Form.useForm<GroupTransferValues>();
  const transfer = useGroupTransferEmployee();
  const codes = useListSgkExitCodes(undefined, { query: { enabled: open } });
  const refresh = useEmployeeRefresh(employeeId);
  const targetCompanyId = Form.useWatch('targetCompanyId', form);
  const keepSeniority = Form.useWatch('keepSeniority', form) ?? true;
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: GroupTransferValues) => {
    const transferDate = day(values.transferDate);
    await transfer.mutateAsync({
      id: employeeId,
      data: {
        employmentId: employment.id,
        targetCompanyId: values.targetCompanyId ?? '',
        transferDate: transferDate ?? '',
        sgkExitCode: values.sgkExitCode ?? null,
        keepSeniority: values.keepSeniority ?? true,
        transferLeaveBalance: values.transferLeaveBalance ?? false,
        ...toTerms(values, transferDate, !(values.keepSeniority ?? true)),
        ...toAssignment(values),
      },
    });
    await refresh();
  };

  return (
    <FormDrawer<GroupTransferValues>
      title={t('web.hr.employees.lifecycle.group_transfer')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      form={form}
      width={680}
      initialValues={{
        transferDate: dayjs(),
        sgkExitCode: TRANSFER_EXIT_CODE,
        keepSeniority: true,
        transferLeaveBalance: true,
        employmentType: EmploymentType.Indefinite,
      }}
    >
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        message={t('web.hr.employees.lifecycle.group_transfer_hint', { company: employment.companyName })}
      />
      <Row gutter={12}>
        <Col span={14}>
          <Form.Item name="targetCompanyId" label={t('web.hr.employees.fields.target_company')} rules={required}>
            <Select
              showSearch={{ optionFilterProp: 'label' }}
              options={me.companies
                .filter((c) => c.id !== employment.companyId)
                .map((c) => ({ value: c.id, label: c.name }))}
              onChange={() => form.setFieldsValue({ orgUnitId: undefined, positionId: undefined, branchId: undefined })}
            />
          </Form.Item>
        </Col>
        <Col span={10}>
          <Form.Item name="transferDate" label={t('web.hr.employees.fields.transfer_date')} rules={required}>
            <DatePicker style={{ width: '100%' }} format="L" />
          </Form.Item>
        </Col>
      </Row>
      <Form.Item name="sgkExitCode" label={t('web.hr.employees.fields.sgk_exit_code')} rules={required}>
        <Select
          showSearch={{ optionFilterProp: 'label' }}
          loading={codes.isLoading}
          options={(codes.data ?? []).map((c) => ({ value: c.code, label: `${c.code} · ${c.name}` }))}
        />
      </Form.Item>
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item name="keepSeniority" label={t('web.hr.employees.fields.keep_seniority')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item
            name="transferLeaveBalance"
            label={t('web.hr.employees.fields.transfer_leave_balance')}
            valuePropName="checked"
          >
            <Switch />
          </Form.Item>
        </Col>
      </Row>
      <Divider titlePlacement="start">{t('web.hr.employees.hire_steps.employment')}</Divider>
      <TermsFields form={form} startField="transferDate" withSeniority={!keepSeniority} />
      <Divider titlePlacement="start">{t('web.hr.employees.hire_steps.assignment')}</Divider>
      <AssignmentFields form={form} companyId={targetCompanyId} enabled={open} withManager />
    </FormDrawer>
  );
};
