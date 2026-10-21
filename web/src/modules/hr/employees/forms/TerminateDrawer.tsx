import { Alert, DatePicker, Form, Input, Select, Switch } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useListSgkExitCodes, useTerminateEmployment } from '@/shared/api/generated/endpoints/hr/hr';
import type { EmploymentItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { day, text } from './formValues';
import { useEmployeeRefresh } from './useEmployeeRefresh';

interface TerminateValues {
  date?: Dayjs | null;
  sgkExitCode?: string;
  rehireable?: boolean;
  reason?: string;
  note?: string;
}

interface TerminateDrawerProps {
  employeeId: string;
  employment: EmploymentItem;
  open: boolean;
  onClose: () => void;
}

/**
 * İşten çıkış (D38): the employment ends on its last working day with an SGK leaving code; the assignment of that
 * day ends with it and later ones are dropped.
 */
export const TerminateDrawer = ({ employeeId, employment, open, onClose }: TerminateDrawerProps) => {
  const { t } = useTranslation();
  const terminate = useTerminateEmployment();
  const codes = useListSgkExitCodes(undefined, { query: { enabled: open } });
  const refresh = useEmployeeRefresh(employeeId);
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: TerminateValues) => {
    await terminate.mutateAsync({
      id: employeeId,
      data: {
        employmentId: employment.id,
        date: day(values.date) ?? '',
        sgkExitCode: values.sgkExitCode ?? null,
        rehireable: values.rehireable ?? true,
        reason: text(values.reason),
        note: text(values.note),
      },
    });
    await refresh();
  };

  return (
    <FormDrawer<TerminateValues>
      title={t('web.hr.employees.lifecycle.terminate')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      initialValues={{ date: dayjs(), rehireable: true }}
    >
      <Alert
        type="warning"
        showIcon
        style={{ marginBottom: 16 }}
        message={t('web.hr.employees.lifecycle.terminate_hint', {
          company: employment.companyName,
          registry: employment.registryNumber,
        })}
      />
      <Form.Item name="date" label={t('web.hr.employees.fields.termination_date')} rules={required}>
        <DatePicker style={{ width: '100%' }} format="L" />
      </Form.Item>
      <Form.Item name="sgkExitCode" label={t('web.hr.employees.fields.sgk_exit_code')} rules={required}>
        <Select
          showSearch={{ optionFilterProp: 'label' }}
          loading={codes.isLoading}
          options={(codes.data ?? []).map((c) => ({ value: c.code, label: `${c.code} · ${c.name}` }))}
        />
      </Form.Item>
      <Form.Item name="rehireable" label={t('web.hr.employees.fields.rehireable')} valuePropName="checked">
        <Switch />
      </Form.Item>
      <Form.Item name="reason" label={t('web.hr.employees.fields.termination_reason')}>
        <Input maxLength={500} />
      </Form.Item>
      <Form.Item name="note" label={t('web.hr.employees.fields.termination_note')}>
        <Input.TextArea rows={3} maxLength={500} />
      </Form.Item>
    </FormDrawer>
  );
};
