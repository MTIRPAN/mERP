import { Form, Input } from 'antd';
import { useTranslation } from 'react-i18next';

/** The requester's explanation on a change request form (D39). */
export const NoteField = () => {
  const { t } = useTranslation();
  return (
    <Form.Item name="note" label={t('web.hr.profile_changes.note')}>
      <Input.TextArea maxLength={500} showCount autoSize={{ minRows: 2, maxRows: 5 }} />
    </Form.Item>
  );
};
