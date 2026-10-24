import { Form, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import { ALL_COMPANIES } from './leaveForms';

/** Every company, or one company; chosen once, when the record is created (D41). */
export const CompanyField = ({ disabled }: { disabled?: boolean }) => {
  const { t } = useTranslation();
  const me = useMe();
  return (
    <Form.Item name="companyId" label={t('web.hr.leave.fields.company')} extra={t('web.hr.leave.fields.company_hint')}>
      <Select
        disabled={disabled}
        options={[
          { value: ALL_COMPANIES, label: t('web.hr.leave.all_companies') },
          ...me.companies.map((c) => ({ value: c.id, label: c.name })),
        ]}
      />
    </Form.Item>
  );
};
