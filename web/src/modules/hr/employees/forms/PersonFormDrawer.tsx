import { Col, Form, Input, Row } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useUpdateEmployeePerson } from '@/shared/api/generated/endpoints/hr/hr';
import { useGetCustomFieldForm } from '@/shared/api/generated/endpoints/platform/platform';
import type { EmployeeDetails, Gender, MaritalStatus, MilitaryStatus } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { DynamicFields } from '@/shared/customFields/DynamicFields';
import { toApiValues, toFormValues, type CustomFieldValues } from '@/shared/customFields/customFieldValues';
import { EMPLOYEE_ENTITY } from '../employeeFilters';
import { day, text } from './formValues';
import { PersonFields } from './PersonFields';
import { useEmployeeRefresh } from './useEmployeeRefresh';

interface PersonFormValues {
  firstName?: string;
  lastName?: string;
  birthDate?: Dayjs | null;
  birthPlace?: string;
  gender?: Gender;
  nationality?: string;
  maritalStatus?: MaritalStatus;
  militaryStatus?: MilitaryStatus;
  customFields?: CustomFieldValues;
}

interface PersonFormDrawerProps {
  employee: EmployeeDetails;
  /** Company whose custom fields apply, the person's main one. */
  companyId?: string;
  open: boolean;
  onClose: () => void;
}

/** The person's identity details and custom fields; the TCKN never changes (D38). */
export const PersonFormDrawer = ({ employee, companyId, open, onClose }: PersonFormDrawerProps) => {
  const { t } = useTranslation();
  const update = useUpdateEmployeePerson();
  const refresh = useEmployeeRefresh(employee.id);
  const fields = useGetCustomFieldForm({ entityType: EMPLOYEE_ENTITY, companyId }, { query: { enabled: open } });
  const customFields = fields.data ?? [];
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: PersonFormValues) => {
    await update.mutateAsync({
      id: employee.id,
      data: {
        version: employee.version,
        firstName: text(values.firstName),
        lastName: text(values.lastName),
        birthDate: day(values.birthDate),
        birthPlace: text(values.birthPlace),
        gender: values.gender ?? null,
        nationality: text(values.nationality)?.toUpperCase() ?? null,
        maritalStatus: values.maritalStatus ?? null,
        militaryStatus: values.militaryStatus ?? null,
        customFields: toApiValues(customFields, values.customFields),
      },
    });
    await refresh();
  };

  return (
    <FormDrawer<PersonFormValues>
      key={`${employee.id}-${employee.version}-${customFields.length}`}
      title={t('web.hr.employees.edit.person')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      width={640}
      initialValues={{
        firstName: employee.firstName,
        lastName: employee.lastName,
        birthDate: employee.birthDate ? dayjs(employee.birthDate) : null,
        birthPlace: employee.birthPlace ?? undefined,
        gender: employee.gender ?? undefined,
        nationality: employee.nationality ?? undefined,
        maritalStatus: employee.maritalStatus ?? undefined,
        militaryStatus: employee.militaryStatus ?? undefined,
        customFields: toFormValues(customFields, employee.customFields),
      }}
    >
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item name="firstName" label={t('web.hr.employees.fields.first_name')} rules={required}>
            <Input maxLength={100} />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="lastName" label={t('web.hr.employees.fields.last_name')} rules={required}>
            <Input maxLength={100} />
          </Form.Item>
        </Col>
      </Row>
      <PersonFields />
      <DynamicFields fields={customFields} />
    </FormDrawer>
  );
};
