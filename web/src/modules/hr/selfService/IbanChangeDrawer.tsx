import { App, Form, Input } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSubmitIbanChange } from '@/shared/api/generated/endpoints/hr/hr';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { text } from '../employees/forms/formValues';
import { NoteField } from './NoteField';
import { useProfileChangesRefresh } from './useProfileChangesRefresh';

interface IbanChangeValues {
  iban?: string;
  note?: string;
}

interface IbanChangeDrawerProps {
  open: boolean;
  onClose: () => void;
  /** Corrects a returned request, starting from what it proposed. */
  requestId?: string;
  initial?: { iban?: string | null; note?: string | null };
}

/** Asks for a new IBAN, or none, for approval (D39); the server checks the number. */
export const IbanChangeDrawer = ({ open, onClose, requestId, initial }: IbanChangeDrawerProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const submitChange = useSubmitIbanChange();
  const refresh = useProfileChangesRefresh();

  const submit = async (values: IbanChangeValues) => {
    await submitChange.mutateAsync({
      data: { iban: text(values.iban), note: text(values.note), requestId: requestId ?? null },
    });
    await refresh();
    message.success(t('web.hr.profile_changes.request.sent'));
  };

  return (
    <FormDrawer<IbanChangeValues>
      title={t('web.hr.profile_changes.request.iban')}
      submitLabel={t('web.hr.profile_changes.request.send')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      initialValues={{ iban: initial?.iban ?? undefined, note: initial?.note ?? undefined }}
    >
      <Form.Item
        name="iban"
        label={t('web.hr.employees.fields.iban')}
        extra={t('web.hr.profile_changes.request.iban_hint')}
      >
        <Input maxLength={40} autoComplete="off" />
      </Form.Item>
      <NoteField />
    </FormDrawer>
  );
};
