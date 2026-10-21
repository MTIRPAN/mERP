import { Col, DatePicker, Divider, Form, Input, InputNumber, Row, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useSetEmployeeIban, useUpdateEmployeeSensitiveData } from '@/shared/api/generated/endpoints/hr/hr';
import type { EmployeeDetails } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { day, text } from './formValues';
import { useEmployeeRefresh } from './useEmployeeRefresh';

/** The blood types the server accepts (UpdateEmployeeSensitiveDataCommand.BloodTypes). */
const BLOOD_TYPES = ['0 Rh+', '0 Rh-', 'A Rh+', 'A Rh-', 'B Rh+', 'B Rh-', 'AB Rh+', 'AB Rh-'];

interface SensitiveFormValues {
  iban?: string;
  disabilityDegree?: number | null;
  bloodType?: string;
  healthNotes?: string;
  criminalRecordDate?: Dayjs | null;
}

interface SensitiveFormDrawerProps {
  employee: EmployeeDetails;
  open: boolean;
  onClose: () => void;
}

/**
 * The IBAN and the special category data (D13). Offered only to those who see the plain values: the data is
 * replaced as a whole, so editing masked values would erase them. The special category data is stored encrypted.
 */
export const SensitiveFormDrawer = ({ employee, open, onClose }: SensitiveFormDrawerProps) => {
  const { t } = useTranslation();
  const setIban = useSetEmployeeIban();
  const updateSensitive = useUpdateEmployeeSensitiveData();
  const refresh = useEmployeeRefresh(employee.id);
  const sensitive = employee.sensitive;

  const submit = async (values: SensitiveFormValues) => {
    const iban = text(values.iban)?.replaceAll(' ', '').toUpperCase() ?? null;
    if (iban !== (employee.iban ?? null)) {
      await setIban.mutateAsync({ id: employee.id, data: { version: employee.version, iban } });
    }

    await updateSensitive.mutateAsync({
      id: employee.id,
      data: {
        disabilityDegree: values.disabilityDegree ?? null,
        bloodType: values.bloodType ?? null,
        healthNotes: text(values.healthNotes),
        criminalRecordDate: day(values.criminalRecordDate),
      },
    });
    await refresh();
  };

  return (
    <FormDrawer<SensitiveFormValues>
      key={`${employee.id}-${employee.version}`}
      title={t('web.hr.employees.edit.sensitive')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      initialValues={{
        iban: employee.iban ?? undefined,
        disabilityDegree: sensitive.disabilityDegree,
        bloodType: sensitive.bloodType ?? undefined,
        healthNotes: sensitive.healthNotes ?? undefined,
        criminalRecordDate: sensitive.criminalRecordDate ? dayjs(sensitive.criminalRecordDate) : null,
      }}
    >
      <Form.Item name="iban" label={t('web.hr.employees.fields.iban')}>
        <Input maxLength={42} autoComplete="off" placeholder={t('web.hr.employees.iban_placeholder')} />
      </Form.Item>
      <Divider titlePlacement="start">{t('web.hr.employees.sensitive.title')}</Divider>
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item name="disabilityDegree" label={t('web.hr.employees.sensitive.disability_degree')}>
            <InputNumber min={0} max={100} precision={0} prefix="%" style={{ width: '100%' }} />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="bloodType" label={t('web.hr.employees.sensitive.blood_type')}>
            <Select allowClear options={BLOOD_TYPES.map((value) => ({ value, label: value }))} />
          </Form.Item>
        </Col>
      </Row>
      <Form.Item name="criminalRecordDate" label={t('web.hr.employees.sensitive.criminal_record_date')}>
        <DatePicker style={{ width: '100%' }} format="L" />
      </Form.Item>
      <Form.Item name="healthNotes" label={t('web.hr.employees.sensitive.health_notes')}>
        <Input.TextArea rows={4} maxLength={2000} showCount />
      </Form.Item>
    </FormDrawer>
  );
};
