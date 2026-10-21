import { Col, DatePicker, Form, Input, InputNumber, Row, Select, type FormInstance } from 'antd';
import type { Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { EmploymentType } from '@/shared/api/generated/model';
import { enumOptions } from './formValues';

/** Values of the employment terms; the start day field is named by the form (hire or transfer date). */
export interface TermsValues {
  employmentType?: EmploymentType;
  contractEndDate?: Dayjs | null;
  weeklyHours?: number | null;
  probationEndDate?: Dayjs | null;
  seniorityDate?: Dayjs | null;
  seniorityReason?: string;
}

interface TermsFieldsProps {
  form: FormInstance;
  /** Field holding the employment's first day; a seniority date other than it needs a reason. */
  startField: string;
  /** Hide the seniority fields, e.g. when a transfer keeps the seniority. */
  withSeniority?: boolean;
}

/** Employment type with its contract end or weekly hours, probation and seniority (D38). */
export const TermsFields = ({ form, startField, withSeniority = true }: TermsFieldsProps) => {
  const { t } = useTranslation();
  const employmentType = Form.useWatch<EmploymentType | undefined>('employmentType', form);
  const seniorityDate = Form.useWatch<Dayjs | null | undefined>('seniorityDate', form);
  const start = Form.useWatch<Dayjs | null | undefined>(startField, form);
  const differentSeniority = !!seniorityDate && !!start && !seniorityDate.isSame(start, 'day');
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  return (
    <>
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item name="employmentType" label={t('web.hr.employees.fields.employment_type')} rules={required}>
            <Select options={enumOptions(EmploymentType, 'web.hr.employment_type.', t)} />
          </Form.Item>
        </Col>
        {employmentType === EmploymentType.FixedTerm && (
          <Col span={12}>
            <Form.Item name="contractEndDate" label={t('web.hr.employees.fields.contract_end_date')} rules={required}>
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
          <Form.Item name="probationEndDate" label={t('web.hr.employees.fields.probation_end_date')}>
            <DatePicker style={{ width: '100%' }} format="L" />
          </Form.Item>
        </Col>
        {withSeniority && (
          <Col span={12}>
            <Form.Item
              name="seniorityDate"
              label={t('web.hr.employees.fields.seniority_date')}
              extra={t('web.hr.employees.seniority_hint')}
            >
              <DatePicker style={{ width: '100%' }} format="L" />
            </Form.Item>
          </Col>
        )}
      </Row>
      {withSeniority && differentSeniority && (
        <Form.Item name="seniorityReason" label={t('web.hr.employees.fields.seniority_reason')} rules={required}>
          <Input maxLength={500} />
        </Form.Item>
      )}
    </>
  );
};
