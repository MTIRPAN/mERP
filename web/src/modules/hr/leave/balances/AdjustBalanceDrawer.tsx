import { useQueryClient } from '@tanstack/react-query';
import { DatePicker, Form, Input, InputNumber, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { getListLeaveBalancesQueryKey, useAdjustLeaveBalance } from '@/shared/api/generated/endpoints/hr/hr';
import type { LeaveBalanceItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';

interface AdjustValues {
  leaveTypeCode?: string;
  date?: Dayjs;
  quantity?: number;
  note?: string;
}

interface AdjustBalanceDrawerProps {
  employeeId: string;
  employmentId: string;
  balances: LeaveBalanceItem[];
  onClose: () => void;
}

/** HR corrects a leave balance in (+) or out (−); the reason is kept with the movement (D43). */
export const AdjustBalanceDrawer = ({ employeeId, employmentId, balances, onClose }: AdjustBalanceDrawerProps) => {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const adjust = useAdjustLeaveBalance();
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: AdjustValues) => {
    await adjust.mutateAsync({
      employeeId,
      employmentId,
      data: {
        leaveTypeCode: values.leaveTypeCode ?? null,
        date: (values.date ?? dayjs()).format('YYYY-MM-DD'),
        quantity: values.quantity ?? 0,
        note: values.note?.trim() || null,
      },
    });
    await queryClient.invalidateQueries({ queryKey: getListLeaveBalancesQueryKey(employeeId, employmentId) });
  };

  return (
    <FormDrawer<AdjustValues>
      title={t('web.hr.leave_balances.adjust')}
      open
      onClose={onClose}
      onSubmit={submit}
      initialValues={{ leaveTypeCode: balances.find((b) => !b.closedOn)?.leaveTypeCode, date: dayjs() }}
    >
      <Form.Item name="leaveTypeCode" label={t('web.hr.leave_balances.leave_type')} rules={required}>
        <Select
          options={balances.filter((b) => !b.closedOn).map((b) => ({ value: b.leaveTypeCode, label: b.leaveTypeName }))}
        />
      </Form.Item>
      <Form.Item name="date" label={t('web.hr.leave_balances.date')} rules={required}>
        <DatePicker format="L" style={{ width: '100%' }} />
      </Form.Item>
      <Form.Item
        name="quantity"
        label={t('web.hr.leave_balances.quantity')}
        extra={t('web.hr.leave_balances.quantity_hint')}
        rules={required}
      >
        <InputNumber min={-999} max={999} step={0.5} style={{ width: '100%' }} />
      </Form.Item>
      <Form.Item name="note" label={t('web.hr.leave_balances.note')} rules={required}>
        <Input.TextArea maxLength={500} showCount autoSize={{ minRows: 2, maxRows: 5 }} />
      </Form.Item>
    </FormDrawer>
  );
};
