import { Alert, Button, Card, Flex, Input, Space, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { draftMyLeaveRequest } from '@/shared/api/generated/endpoints/hr/hr';
import type { LeaveRequestDraft } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { AiBadge } from '@/shared/components/AiBadge';

const prefix = 'web.hr.leave_requests.ai';

/**
 * Fills the employee's leave request form from what they write, e.g. "next Friday and Monday annual leave" (PLAN A11,
 * D64). Nothing is saved: the form is checked and sent as usual; what the text left open comes back as questions.
 */
export const LeaveRequestAiFill = ({ onDraft }: { onDraft: (draft: LeaveRequestDraft) => void }) => {
  const { t } = useTranslation();
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);
  const [questions, setQuestions] = useState<string[]>();

  const fill = async () => {
    setBusy(true);
    try {
      const draft = await draftMyLeaveRequest({ employmentId: null, text: text.trim() });
      onDraft(draft);
      setQuestions(draft.questions);
    } catch (error) {
      if (!isApiError(error)) {
        throw error;
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card
      size="small"
      style={{ marginBottom: 16 }}
      title={
        <Space>
          {t(`${prefix}.title`)}
          <AiBadge />
        </Space>
      }
    >
      <Flex vertical gap={8}>
        <Flex gap={8} align="flex-start">
          <Input.TextArea
            aria-label={t(`${prefix}.title`)}
            placeholder={t(`${prefix}.placeholder`)}
            value={text}
            maxLength={500}
            autoSize={{ minRows: 1, maxRows: 4 }}
            onChange={(e) => setText(e.target.value)}
          />
          <Button loading={busy} disabled={!text.trim()} onClick={() => void fill()}>
            {t(`${prefix}.fill`)}
          </Button>
        </Flex>
        {questions && (
          <Alert
            type="info"
            showIcon
            title={t(`${prefix}.notice`)}
            description={
              questions.length > 0 && (
                <ul style={{ margin: 0, paddingInlineStart: 20 }}>
                  {questions.map((question, index) => (
                    <li key={index}>
                      <Typography.Text>{question}</Typography.Text>
                    </li>
                  ))}
                </ul>
              )
            }
          />
        )}
      </Flex>
    </Card>
  );
};
