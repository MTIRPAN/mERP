import { Form, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';

/** The value of "every company" in the select; the API takes null. */
export const ALL_COMPANIES = '';

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

/** The company's name, or "every company". */
export const useCompanyName = () => {
  const { t } = useTranslation();
  const me = useMe();
  return (companyId: string | null) =>
    companyId ? (me.companies.find((c) => c.id === companyId)?.name ?? '—') : t('web.hr.leave.all_companies');
};
