import { Button, Drawer, Flex, Form, type FormInstance } from 'antd';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';

interface FormDrawerProps<TValues> {
  title: ReactNode;
  open: boolean;
  onClose: () => void;
  onSubmit: (values: TValues) => Promise<unknown>;
  initialValues?: Partial<TValues>;
  form?: FormInstance<TValues>;
  width?: number;
  /** Moves API field errors onto other form fields, e.g. label → ['label', 'tr-TR']. */
  fieldAliases?: Record<string, (string | number)[]>;
  /** The primary button's text; Save by default. */
  submitLabel?: ReactNode;
  children: ReactNode;
}

/** Side drawer with a vertical form; API validation errors are shown next to the fields. */
export const FormDrawer = <TValues extends object>({
  title,
  open,
  onClose,
  onSubmit,
  initialValues,
  form: externalForm,
  width = 560,
  fieldAliases,
  submitLabel,
  children,
}: FormDrawerProps<TValues>) => {
  const { t } = useTranslation();
  const [ownForm] = Form.useForm<TValues>();
  const form = externalForm ?? ownForm;
  const [saving, setSaving] = useState(false);

  const submit = async (values: TValues) => {
    setSaving(true);
    try {
      await onSubmit(values);
      onClose();
    } catch (error) {
      if (!applyApiErrorsToForm(form, error, fieldAliases)) {
        throw error;
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Drawer
      title={title}
      open={open}
      onClose={onClose}
      size={width}
      destroyOnHidden
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose}>{t('web.common.cancel')}</Button>
          <Button type="primary" loading={saving} onClick={() => form.submit()}>
            {submitLabel ?? t('web.common.save')}
          </Button>
        </Flex>
      }
    >
      <Form<TValues>
        form={form}
        layout="vertical"
        initialValues={initialValues}
        onFinish={submit}
        requiredMark="optional"
      >
        {children}
      </Form>
    </Drawer>
  );
};
