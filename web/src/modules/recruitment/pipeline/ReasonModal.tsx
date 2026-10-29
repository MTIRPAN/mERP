import { Checkbox, Form, Input, Modal, Select, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { ReasonOption } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { emailDelays, type LeaveValues } from './pipeline';

interface ReasonModalProps {
  /** Reject the application, or record that the candidate withdrew. */
  kind: 'reject' | 'withdraw';
  candidateName: string;
  reasons: ReasonOption[];
  onSubmit: (values: LeaveValues) => Promise<unknown>;
  onClose: () => void;
}

/**
 * Asks why an application leaves the pipeline (D50); a rejection also asks whether and when the candidate gets the
 * rejection e-mail. API field errors are shown on the fields.
 */
export const ReasonModal = ({ kind, candidateName, reasons, onSubmit, onClose }: ReasonModalProps) => {
  const { t } = useTranslation();
  const [form] = Form.useForm<LeaveValues>();
  const [saving, setSaving] = useState(false);
  const sendEmail = Form.useWatch('sendEmail', form);

  const submit = async (values: LeaveValues) => {
    setSaving(true);
    try {
      await onSubmit({ ...values, note: values.note?.trim() || undefined });
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
      title={t(`web.recruitment.pipeline.${kind}.title`, { name: candidateName })}
      open
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t(`web.recruitment.pipeline.${kind}.ok`)}
      okButtonProps={{ danger: kind === 'reject' }}
      confirmLoading={saving}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        onFinish={submit}
        initialValues={{ sendEmail: kind === 'reject', emailDelayDays: 0 }}
      >
        <Form.Item
          name="reasonCode"
          label={t('web.recruitment.pipeline.fields.reason')}
          rules={[{ required: true, message: t('web.platform.fields.required') }]}
        >
          <Select options={reasons.map((r) => ({ value: r.code, label: r.name }))} />
        </Form.Item>
        <Form.Item name="note" label={t('web.recruitment.pipeline.fields.note')}>
          <Input.TextArea maxLength={1000} showCount autoSize={{ minRows: 2, maxRows: 6 }} />
        </Form.Item>
        {kind === 'reject' && (
          <>
            <Form.Item name="sendEmail" valuePropName="checked" style={{ marginBottom: 8 }}>
              <Checkbox>{t('web.recruitment.pipeline.reject.send_email')}</Checkbox>
            </Form.Item>
            {sendEmail && (
              <Form.Item name="emailDelayDays" label={t('web.recruitment.pipeline.reject.when')}>
                <Select
                  options={emailDelays.map((days) => ({
                    value: days,
                    label:
                      days === 0
                        ? t('web.recruitment.pipeline.reject.now')
                        : t('web.recruitment.pipeline.reject.after_days', { days }),
                  }))}
                />
              </Form.Item>
            )}
            <Typography.Paragraph type="secondary">{t('web.recruitment.pipeline.reject.hint')}</Typography.Paragraph>
          </>
        )}
      </Form>
    </Modal>
  );
};
