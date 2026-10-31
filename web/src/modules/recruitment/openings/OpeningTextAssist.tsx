import { StarFilled } from '@ant-design/icons';
import { Alert, App, Button, Flex, Form, Input, List, Modal, Space, Tag, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { checkOpeningLanguage, draftOpeningText } from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type {
  DraftOpeningTextQuery,
  OpeningDraftResponse,
  OpeningLanguageCheck,
  OpeningLanguageWarning,
} from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { AiBadge } from '@/shared/components/AiBadge';
import { useOpeningTextAi } from './openings';

const prefix = 'web.recruitment.openings';

/** Exclusionary phrases of an opening with their category and a neutral alternative (D57). */
export const LanguageWarnings = ({ warnings }: { warnings: OpeningLanguageWarning[] }) => {
  const { t } = useTranslation();
  return (
    <List
      size="small"
      dataSource={warnings}
      renderItem={(w) => (
        <List.Item>
          <Flex vertical gap={2}>
            <Space size={4} wrap>
              <Tag color="warning">{t(`${prefix}.language.categories.${w.category}`)}</Tag>
              <Typography.Text type="secondary">{t(`${prefix}.language.fields.${w.field}`)}:</Typography.Text>
              <Typography.Text strong>“{w.phrase}”</Typography.Text>
              {w.source === 'Ai' && <Tag icon={<StarFilled />}>{t(`${prefix}.language.sources.Ai`)}</Tag>}
            </Space>
            <Typography.Text type="secondary">
              {w.suggestion ?? t(`${prefix}.language.hints.${w.category}`)}
            </Typography.Text>
          </Flex>
        </List.Item>
      )}
    />
  );
};

/** The warnings in an alert: none found, or the list with the hint that they never block. */
export const LanguageCheckResult = ({ check }: { check: OpeningLanguageCheck }) => {
  const { t } = useTranslation();
  const checkedWith = t(`${prefix}.language.${check.checkedWithAi ? 'checked_with_ai' : 'checked_with_rules'}`);
  return check.warnings.length === 0 ? (
    <Alert type="success" showIcon title={t(`${prefix}.language.none`)} description={checkedWith} />
  ) : (
    <Alert
      type="warning"
      showIcon
      title={t(`${prefix}.language.title`)}
      description={
        <>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 4 }}>
            {t(`${prefix}.language.hint`)} {checkedWith}
          </Typography.Paragraph>
          <LanguageWarnings warnings={check.warnings} />
        </>
      }
    />
  );
};

interface LanguageCheckButtonProps {
  texts: () => { title?: string; description?: string | null; qualifications?: string | null };
  onChecked: (check: OpeningLanguageCheck) => void;
}

/** Checks the form's texts: the rule list, and the AI review when it is on. */
export const LanguageCheckButton = ({ texts, onChecked }: LanguageCheckButtonProps) => {
  const { t } = useTranslation();
  const withAi = useOpeningTextAi();
  const [busy, setBusy] = useState(false);
  const check = async () => {
    setBusy(true);
    try {
      const { title, description, qualifications } = texts();
      onChecked(
        await checkOpeningLanguage({
          title: title ?? null,
          description: description ?? null,
          qualifications: qualifications ?? null,
          withAi,
        }),
      );
    } catch (error) {
      if (!isApiError(error)) {
        throw error;
      }
    } finally {
      setBusy(false);
    }
  };
  return (
    <Button size="small" loading={busy} onClick={() => void check()}>
      {t(`${prefix}.language.check`)}
    </Button>
  );
};

interface AiDraftModalProps {
  request: Omit<DraftOpeningTextQuery, 'brief'>;
  /** The texts are not empty: the draft replaces them only after a confirmation. */
  replaces: boolean;
  onDraft: (draft: OpeningDraftResponse) => void;
  onClose: () => void;
}

/** Asks for a short brief and drafts the description and qualifications with AI (PLAN A4, D57). */
export const AiDraftModal = ({ request, replaces, onDraft, onClose }: AiDraftModalProps) => {
  const { t } = useTranslation();
  const { modal } = App.useApp();
  const [brief, setBrief] = useState('');
  const [busy, setBusy] = useState(false);
  const generate = async () => {
    setBusy(true);
    try {
      const draft = await draftOpeningText({ ...request, brief: brief.trim() || null });
      if (replaces && !(await modal.confirm({ title: t(`${prefix}.ai_draft.replace_confirm`) }))) {
        return;
      }

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
          {t(`${prefix}.ai_draft.title`)}
          <AiBadge />
        </Space>
      }
      okText={t(`${prefix}.ai_draft.generate`)}
      okButtonProps={{ loading: busy }}
      onOk={() => void generate()}
      onCancel={onClose}
      destroyOnHidden
    >
      <Form layout="vertical">
        <Form.Item label={t(`${prefix}.ai_draft.brief`)} extra={t(`${prefix}.ai_draft.brief_hint`)}>
          <Input.TextArea
            aria-label={t(`${prefix}.ai_draft.brief`)}
            value={brief}
            onChange={(e) => setBrief(e.target.value)}
            maxLength={2000}
            showCount
            autoSize={{ minRows: 4, maxRows: 10 }}
          />
        </Form.Item>
      </Form>
      {request.requisitionId && (
        <Typography.Text type="secondary">{t(`${prefix}.ai_draft.requisition_used`)}</Typography.Text>
      )}
    </Modal>
  );
};
