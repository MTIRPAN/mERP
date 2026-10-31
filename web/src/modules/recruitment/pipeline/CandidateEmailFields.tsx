import { Alert, Button, Card, Checkbox, Flex, Form, Input, Space, Typography } from 'antd';
import type { FormInstance } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  draftInterviewInvitationEmail,
  draftRejectionEmail,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { InterviewType } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { AiBadge } from '@/shared/components/AiBadge';

const prefix = 'web.recruitment.candidate_email';

/** The placeholders each e-mail may use until a draft lists them (D58). */
const defaultPlaceholders = {
  rejection: ['candidate', 'position', 'company'],
  invitation: ['candidate', 'position', 'company', 'date', 'time', 'location'],
};

interface CandidateEmailFieldsProps {
  kind: 'rejection' | 'invitation';
  applicationId: string;
  /** The form the subject and body fields belong to. */
  form: FormInstance;
  subjectName: string;
  bodyName: string;
  /** The type of the interview being planned, for the invitation. */
  interviewType?: InterviewType;
}

/**
 * Personalizes the candidate's rejection or invitation e-mail (PLAN A5, D58): AI drafts it from the template without
 * the candidate's name, the user edits it, and it is sent only with the rejection or the interview. Unticked, the
 * template goes.
 */
export const CandidateEmailFields = ({
  kind,
  applicationId,
  form,
  subjectName,
  bodyName,
  interviewType,
}: CandidateEmailFieldsProps) => {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [instructions, setInstructions] = useState('');
  const [placeholders, setPlaceholders] = useState(defaultPlaceholders[kind]);
  const [busy, setBusy] = useState(false);

  const draft = async () => {
    setBusy(true);
    try {
      const body = { instructions: instructions.trim() || null, interviewType: interviewType ?? null };
      const result =
        kind === 'rejection'
          ? await draftRejectionEmail(applicationId, body)
          : await draftInterviewInvitationEmail(applicationId, body);
      setPlaceholders(result.placeholders);
      form.setFieldsValue({ [subjectName]: result.subject, [bodyName]: result.body });
    } catch (error) {
      if (!isApiError(error)) {
        throw error;
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <Flex vertical gap={8} style={{ marginBottom: 16 }}>
      <Checkbox checked={open} onChange={(e) => setOpen(e.target.checked)}>
        <Space size={4}>
          {t(`${prefix}.personalize`)}
          <AiBadge />
        </Space>
      </Checkbox>
      {open && (
        <Card size="small">
          <Alert type="info" showIcon title={t(`${prefix}.notice`)} style={{ marginBottom: 12 }} />
          <Flex gap={8} style={{ marginBottom: 12 }}>
            <Input
              aria-label={t(`${prefix}.instructions`)}
              placeholder={t(`${prefix}.instructions`)}
              value={instructions}
              maxLength={1000}
              onChange={(e) => setInstructions(e.target.value)}
            />
            <Button loading={busy} onClick={() => void draft()}>
              {t(`${prefix}.draft`)}
            </Button>
          </Flex>
          <Form.Item
            name={subjectName}
            label={t(`${prefix}.subject`)}
            preserve={false}
            rules={[{ required: true, whitespace: true, message: t('web.platform.fields.required') }]}
          >
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item
            name={bodyName}
            label={t(`${prefix}.body`)}
            preserve={false}
            rules={[{ required: true, whitespace: true, message: t('web.platform.fields.required') }]}
            style={{ marginBottom: 8 }}
          >
            <Input.TextArea maxLength={5000} showCount autoSize={{ minRows: 5, maxRows: 14 }} />
          </Form.Item>
          <Typography.Text type="secondary">
            {t(`${prefix}.placeholders`, { list: placeholders.map((p) => `{${p}}`).join(', ') })}
          </Typography.Text>
        </Card>
      )}
    </Flex>
  );
};
