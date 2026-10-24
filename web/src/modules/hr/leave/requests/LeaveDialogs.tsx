import { DatePicker, Form, Input, Modal, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';

interface ReasonDialogProps {
  title: ReactNode;
  /** The reason must be given, e.g. to cancel an approved leave. */
  required: boolean;
  okText: ReactNode;
  onSubmit: (reason: string | null) => Promise<unknown>;
  onClose: () => void;
}

/** Asks for the reason of a cancellation; API field errors are shown on the field. */
export const ReasonDialog = ({ title, required, okText, onSubmit, onClose }: ReasonDialogProps) => {
  const { t } = useTranslation();
  const [form] = Form.useForm<{ reason?: string }>();
  const [saving, setSaving] = useState(false);

  const submit = async ({ reason }: { reason?: string }) => {
    setSaving(true);
    try {
      await onSubmit(reason?.trim() || null);
      onClose();
    } catch (error) {
      if (!applyApiErrorsToForm(form, error)) {
        throw error;
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      title={title}
      open
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={okText}
      confirmLoading={saving}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" onFinish={submit}>
        <Form.Item
          name="reason"
          label={t('web.hr.leave_requests.fields.reason')}
          rules={required ? [{ required: true, message: t('web.platform.fields.required') }] : []}
        >
          <Input.TextArea maxLength={500} showCount autoSize={{ minRows: 3, maxRows: 6 }} />
        </Form.Item>
      </Form>
    </Modal>
  );
};

interface EarlyReturnValues {
  endDate?: Dayjs;
  endPart?: 'Full' | 'Morning';
}

interface EarlyReturnDialogProps {
  startDate: string;
  endDate: string;
  halfDay: boolean;
  onSubmit: (values: { endDate: string; endPart: 'Full' | 'Morning' }) => Promise<unknown>;
  onClose: () => void;
}

/** HR enters the last day of a leave ended early (erken dönüş); the days no longer taken come back. */
export const EarlyReturnDialog = ({ startDate, endDate, halfDay, onSubmit, onClose }: EarlyReturnDialogProps) => {
  const { t } = useTranslation();
  const [form] = Form.useForm<EarlyReturnValues>();
  const [saving, setSaving] = useState(false);

  const submit = async (values: EarlyReturnValues) => {
    setSaving(true);
    try {
      await onSubmit({ endDate: values.endDate!.format('YYYY-MM-DD'), endPart: values.endPart ?? 'Full' });
      onClose();
    } catch (error) {
      if (!applyApiErrorsToForm(form, error)) {
        throw error;
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      title={t('web.hr.leave_requests.early_return')}
      open
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('web.common.save')}
      confirmLoading={saving}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" onFinish={submit} initialValues={{ endPart: 'Full' }}>
        <Form.Item
          name="endDate"
          label={t('web.hr.leave_requests.fields.last_day')}
          extra={t('web.hr.leave_requests.early_return_hint')}
          rules={[{ required: true, message: t('web.platform.fields.required') }]}
        >
          <DatePicker
            format="L"
            style={{ width: '100%' }}
            disabledDate={(day) => day.isBefore(dayjs(startDate), 'day') || !day.isBefore(dayjs(endDate), 'day')}
          />
        </Form.Item>
        {halfDay && (
          <Form.Item name="endPart" label={t('web.hr.leave_requests.fields.last_day_part')}>
            <Select
              options={[
                { value: 'Full', label: t('web.hr.leave_requests.parts.full') },
                { value: 'Morning', label: t('web.hr.leave_requests.parts.morning') },
              ]}
            />
          </Form.Item>
        )}
      </Form>
    </Modal>
  );
};
