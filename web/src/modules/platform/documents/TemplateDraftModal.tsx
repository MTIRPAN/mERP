import { Checkbox, Form, Input, Modal, Space, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { draftDocumentTemplate } from '@/shared/api/generated/endpoints/platform/platform';
import type { DocumentTemplateDraft } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { AiBadge } from '@/shared/components/AiBadge';

const prefix = 'web.document_templates.draft';

interface TemplateDraftModalProps {
  documentType: string;
  culture: string;
  /** The content in the editor: the draft may change it instead of starting over. */
  current: { body: string; css: string };
  onDraft: (draft: DocumentTemplateDraft) => void;
  onClose: () => void;
}

/**
 * Drafts a template with AI from a description (PLAN A10, D63). The draft only fills the editor; the administrator
 * previews it and saves it.
 */
export const TemplateDraftModal = ({ documentType, culture, current, onDraft, onClose }: TemplateDraftModalProps) => {
  const { t } = useTranslation();
  const [description, setDescription] = useState('');
  const [useCurrent, setUseCurrent] = useState(false);
  const [busy, setBusy] = useState(false);

  const generate = async () => {
    setBusy(true);
    try {
      const draft = await draftDocumentTemplate({
        documentType,
        culture,
        description: description.trim(),
        currentBody: useCurrent ? current.body : null,
        currentCss: useCurrent ? current.css : null,
      });
      onDraft(draft);
      onClose();
    } catch (error) {
      if (!isApiError(error)) {
        throw error;
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      title={
        <Space>
          {t(`${prefix}.title`)}
          <AiBadge />
        </Space>
      }
      okText={t(`${prefix}.generate`)}
      okButtonProps={{ loading: busy, disabled: !description.trim() }}
      onOk={() => void generate()}
      onCancel={onClose}
      destroyOnHidden
    >
      <Form layout="vertical">
        <Form.Item label={t(`${prefix}.description`)} extra={t(`${prefix}.description_hint`)}>
          <Input.TextArea
            aria-label={t(`${prefix}.description`)}
            value={description}
            maxLength={2000}
            showCount
            autoSize={{ minRows: 4, maxRows: 10 }}
            onChange={(e) => setDescription(e.target.value)}
          />
        </Form.Item>
        {current.body.trim() && (
          <Checkbox checked={useCurrent} onChange={(e) => setUseCurrent(e.target.checked)}>
            {t(`${prefix}.use_current`)}
          </Checkbox>
        )}
      </Form>
      <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
        {t(`${prefix}.replace_confirm`)}
      </Typography.Paragraph>
    </Modal>
  );
};
