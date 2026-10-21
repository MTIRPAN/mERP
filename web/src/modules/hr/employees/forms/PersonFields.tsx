import { Col, DatePicker, Form, Input, Row, Select } from 'antd';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { Gender, MaritalStatus, MilitaryStatus } from '@/shared/api/generated/model';
import { enumOptions } from './formValues';

/** Birth, nationality and status fields of a person, shared by the hire wizard and the person form. */
export const PersonFields = () => {
  const { t } = useTranslation();
  return (
    <>
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
            <Select allowClear options={enumOptions(Gender, 'web.hr.gender.', t)} />
          </Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="maritalStatus" label={t('web.hr.employees.fields.marital_status')}>
            <Select allowClear options={enumOptions(MaritalStatus, 'web.hr.marital_status.', t)} />
          </Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="militaryStatus" label={t('web.hr.employees.fields.military_status')}>
            <Select allowClear options={enumOptions(MilitaryStatus, 'web.hr.military_status.', t)} />
          </Form.Item>
        </Col>
      </Row>
    </>
  );
};
