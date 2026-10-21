import { Col, DatePicker, Divider, Form, Row, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import { useRehireEmployee } from '@/shared/api/generated/endpoints/hr/hr';
import { EmploymentType } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { AssignmentFields, type AssignmentValues } from './AssignmentFields';
import { day } from './formValues';
import { toAssignment, toTerms } from './lifecycle';
import { TermsFields, type TermsValues } from './TermsFields';
import { useEmployeeRefresh } from './useEmployeeRefresh';

interface RehireValues extends AssignmentValues, TermsValues {
  companyId?: string;
  hireDate?: Dayjs | null;
}

interface RehireDrawerProps {
  employeeId: string;
  /** Companies where the person still works; a new employment there is refused anyway. */
  openCompanyIds: string[];
  open: boolean;
  onClose: () => void;
}

/** Tekrar işe giriş (D38): a new employment of the person, in the same or another company. */
export const RehireDrawer = ({ employeeId, openCompanyIds, open, onClose }: RehireDrawerProps) => {
  const { t } = useTranslation();
  const me = useMe();
  const [form] = Form.useForm<RehireValues>();
  const rehire = useRehireEmployee();
  const refresh = useEmployeeRefresh(employeeId);
  const companyId = Form.useWatch('companyId', form);
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: RehireValues) => {
    const hireDate = day(values.hireDate);
    await rehire.mutateAsync({
      id: employeeId,
      data: {
        companyId: values.companyId ?? '',
        hireDate: hireDate ?? '',
        ...toTerms(values, hireDate),
        ...toAssignment(values),
      },
    });
    await refresh();
  };

  return (
    <FormDrawer<RehireValues>
      title={t('web.hr.employees.lifecycle.rehire')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      form={form}
      width={680}
      initialValues={{ hireDate: dayjs(), employmentType: EmploymentType.Indefinite }}
    >
      <Row gutter={12}>
        <Col span={14}>
          <Form.Item name="companyId" label={t('web.hr.employees.fields.company')} rules={required}>
            <Select
              showSearch={{ optionFilterProp: 'label' }}
              options={me.companies
                .filter((c) => !openCompanyIds.includes(c.id))
                .map((c) => ({ value: c.id, label: c.name }))}
              onChange={() => form.setFieldsValue({ orgUnitId: undefined, positionId: undefined, branchId: undefined })}
            />
          </Form.Item>
        </Col>
        <Col span={10}>
          <Form.Item name="hireDate" label={t('web.hr.employees.fields.hire_date')} rules={required}>
            <DatePicker style={{ width: '100%' }} format="L" />
          </Form.Item>
        </Col>
      </Row>
      <TermsFields form={form} startField="hireDate" />
      <Divider titlePlacement="start">{t('web.hr.employees.hire_steps.assignment')}</Divider>
      <AssignmentFields form={form} companyId={companyId} enabled={open} />
    </FormDrawer>
  );
};
