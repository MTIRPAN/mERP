import { Alert, Card, Col, Flex, List, Row, Spin, Statistic, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { LeaveRequestPreview } from '@/shared/api/generated/model';
import { formatDate } from '@/shared/format/date';
import { lengthText } from './leaveRequests';

interface LeavePreviewPanelProps {
  preview: LeaveRequestPreview | undefined;
  loading: boolean;
  /** Whether to show who approves; a recorded leave has no flow. */
  showApprovers: boolean;
}

/**
 * The live check of a leave request (D44): its length, the balance before and after it, what stops it, the warnings
 * and who will approve it.
 */
export const LeavePreviewPanel = ({ preview, loading, showApprovers }: LeavePreviewPanelProps) => {
  const { t, i18n } = useTranslation();
  if (!preview) {
    return loading ? <Spin size="small" /> : null;
  }

  const days = (value: number) => t('web.hr.leave_requests.days_value', { days: value.toLocaleString(i18n.language) });
  return (
    <Card
      size="small"
      title={t('web.hr.leave_requests.preview.title')}
      extra={loading ? <Spin size="small" /> : undefined}
    >
      <Flex vertical gap={12}>
        {preview.problem && <Alert type="error" showIcon title={preview.problem.message} />}
        {preview.warnings.length > 0 && (
          <Alert
            type="warning"
            showIcon
            title={
              <ul style={{ margin: 0, paddingInlineStart: 20 }}>
                {preview.warnings.map((w) => (
                  <li key={w.code}>{w.message}</li>
                ))}
              </ul>
            }
          />
        )}
        <Row gutter={[16, 8]}>
          {preview.basis && preview.quantity != null && (
            <Col span={8}>
              <Statistic
                title={t('web.hr.leave_requests.preview.length')}
                value={lengthText(preview.basis, preview.quantity, t, i18n.language)}
              />
            </Col>
          )}
          {preview.available != null && (
            <Col span={8}>
              <Statistic title={t('web.hr.leave_requests.preview.available')} value={days(preview.available)} />
            </Col>
          )}
          {preview.availableAfter != null && (
            <Col span={8}>
              <Statistic
                title={t('web.hr.leave_requests.preview.available_after')}
                value={days(preview.availableAfter)}
              />
            </Col>
          )}
        </Row>
        {preview.endDate && preview.startDate && preview.endDate !== preview.startDate && (
          <Typography.Text type="secondary">
            {t('web.hr.leave_requests.preview.range', {
              start: formatDate(preview.startDate, i18n.language),
              end: formatDate(preview.endDate, i18n.language),
            })}
          </Typography.Text>
        )}
        {showApprovers && !preview.requiresApproval && preview.basis && (
          <Typography.Text type="secondary">{t('web.hr.leave_requests.preview.no_approval')}</Typography.Text>
        )}
        {showApprovers && preview.approval && (
          <List
            size="small"
            header={<Typography.Text strong>{t('web.hr.leave_requests.preview.approvers')}</Typography.Text>}
            dataSource={preview.approval.steps.filter((s) => !s.skipReason)}
            locale={{ emptyText: t('web.hr.leave_requests.preview.no_steps') }}
            renderItem={(step) => (
              <List.Item>
                <Flex gap={8} wrap align="center">
                  <Typography.Text>{step.name}:</Typography.Text>
                  {step.needsAttention ? (
                    <Tag color="warning">{t('web.hr.leave_requests.preview.needs_attention')}</Tag>
                  ) : (
                    step.approvers.map((a) => <Tag key={a.userId}>{a.name}</Tag>)
                  )}
                </Flex>
              </List.Item>
            )}
          />
        )}
      </Flex>
    </Card>
  );
};
