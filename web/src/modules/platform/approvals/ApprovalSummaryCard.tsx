import { Alert, Button, Card, Flex, Space, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { summarizeApprovalRequest } from '@/shared/api/generated/endpoints/platform/platform';
import type { ApprovalSummaryResponse } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { AiBadge } from '@/shared/components/AiBadge';

const prefix = 'web.approvals.summary';

/**
 * A one-paragraph summary of a request with its attention points for the approver (PLAN A8, D61), made on request
 * and never stored: a suggestion, the decision stays with the approver.
 */
export const ApprovalSummaryCard = ({ requestId }: { requestId: string }) => {
  const { t } = useTranslation();
  const [summary, setSummary] = useState<ApprovalSummaryResponse>();
  const [busy, setBusy] = useState(false);

  const create = async () => {
    setBusy(true);
    try {
      setSummary(await summarizeApprovalRequest(requestId));
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
      title={
        <Space>
          {t(`${prefix}.title`)}
          <AiBadge />
        </Space>
      }
      extra={
        <Button size="small" loading={busy} onClick={() => void create()}>
          {t(summary ? `${prefix}.again` : `${prefix}.create`)}
        </Button>
      }
    >
      {summary ? (
        <Flex vertical gap={8}>
          <Alert type="info" showIcon title={t(`${prefix}.notice`)} />
          <Typography.Paragraph style={{ margin: 0 }}>{summary.summary}</Typography.Paragraph>
          {summary.attentionPoints.length > 0 && (
            <Flex vertical gap={4}>
              <Typography.Text strong>{t(`${prefix}.attention`)}</Typography.Text>
              <ul style={{ margin: 0, paddingInlineStart: 20 }}>
                {summary.attentionPoints.map((point, index) => (
                  <li key={index}>{point}</li>
                ))}
              </ul>
            </Flex>
          )}
        </Flex>
      ) : (
        <Typography.Text type="secondary">{t(`${prefix}.hint`)}</Typography.Text>
      )}
    </Card>
  );
};
