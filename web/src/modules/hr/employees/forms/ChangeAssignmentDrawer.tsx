import { Alert, Col, DatePicker, Form, Row, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useChangeEmployeeAssignment } from '@/shared/api/generated/endpoints/hr/hr';
import type { AssignmentReason, EmploymentItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { AssignmentFields, type AssignmentValues } from './AssignmentFields';
import { day } from './formValues';
import { changeReasons, toAssignment } from './lifecycle';
import { useEmployeeRefresh } from './useEmployeeRefresh';

interface ChangeAssignmentValues extends AssignmentValues {
  validFrom?: Dayjs | null;
  reason?: AssignmentReason;
}

interface ChangeAssignmentDrawerProps {
  employeeId: string;
  employment: EmploymentItem;
  open: boolean;
  onClose: () => void;
}

/**
 * Görev değişikliği (D38): a new assignment from a day, which may be in the future; the previous one ends the day
 * before. A change on the latest assignment's first day corrects it.
 */
export const ChangeAssignmentDrawer = ({ employeeId, employment, open, onClose }: ChangeAssignmentDrawerProps) => {
  const { t } = useTranslation();
  const [form] = Form.useForm<ChangeAssignmentValues>();
  const change = useChangeEmployeeAssignment();
  const refresh = useEmployeeRefresh(employeeId);
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: ChangeAssignmentValues) => {
    await change.mutateAsync({
      id: employeeId,
      data: {
        employmentId: employment.id,
        validFrom: day(values.validFrom) ?? '',
        reason: values.reason ?? null,
        ...toAssignment(values),
      },
    });
    await refresh();
  };

  return (
    <FormDrawer<ChangeAssignmentValues>
      title={t('web.hr.employees.lifecycle.change_assignment')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      form={form}
      width={640}
      initialValues={{ validFrom: dayjs() }}
    >
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        message={t('web.hr.employees.lifecycle.change_assignment_hint', { company: employment.companyName })}
      />
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item name="validFrom" label={t('web.hr.employees.fields.valid_from')} rules={required}>
            <DatePicker style={{ width: '100%' }} format="L" />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="reason" label={t('web.hr.employees.fields.reason')} rules={required}>
            <Select
              options={changeReasons.map((value) => ({ value, label: t(`web.hr.assignment_reason.${value}`) }))}
            />
          </Form.Item>
        </Col>
      </Row>
      <AssignmentFields form={form} companyId={employment.companyId} enabled={open} withManager />
    </FormDrawer>
  );
};
