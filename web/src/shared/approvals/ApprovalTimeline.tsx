import { Empty, Skeleton, Timeline, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { useGetApprovalTimeline } from '@/shared/api/generated/endpoints/platform/platform';
import type { ApprovalRequestDetails } from '@/shared/api/generated/model';
import { formatDateTime } from '@/shared/format/date';
import { timelineComment, timelineText, timelineTone, type TimelineTone } from './timeline';

const colors: Record<TimelineTone, string> = {
  success: 'green',
  error: 'red',
  warning: 'orange',
  info: 'blue',
  neutral: 'gray',
};

/** The timeline of a request, oldest first; later rounds follow the earlier ones (§2.6). */
export const ApprovalTimelineView = ({ details }: { details: ApprovalRequestDetails }) => {
  const { t, i18n } = useTranslation();
  const { token } = theme.useToken();

  return (
    <Timeline
      items={details.history.map((item) => {
        const comment = timelineComment(item);
        return {
          key: item.sequence,
          color: colors[timelineTone(item)],
          content: (
            <>
              <Typography.Text>{timelineText(item, details.steps, t)}</Typography.Text>
              {comment && (
                <Typography.Paragraph
                  italic
                  style={{
                    margin: `${token.marginXXS}px 0 0`,
                    padding: `0 ${token.paddingXS}px`,
                    borderLeft: `2px solid ${token.colorBorder}`,
                  }}
                >
                  {comment}
                </Typography.Paragraph>
              )}
              <Typography.Text type="secondary" style={{ display: 'block', fontSize: token.fontSizeSM }}>
                {formatDateTime(item.at, i18n.language)}
                {details.round > 1 && ` · ${t('web.approvals.timeline.round', { round: item.round })}`}
              </Typography.Text>
            </>
          ),
        };
      })}
    />
  );
};

interface ApprovalTimelineProps {
  documentType: string;
  documentId: string;
}

/**
 * The approval timeline of a document for the module's detail screen (§2.7). Shows nothing to say when the document
 * was never sent or the user may not see its approval.
 */
export const ApprovalTimeline = ({ documentType, documentId }: ApprovalTimelineProps) => {
  const { t } = useTranslation();
  const timeline = useGetApprovalTimeline(
    { documentType, documentId },
    { query: { retry: false, meta: { silent: true } } },
  );

  if (timeline.isLoading) {
    return <Skeleton active paragraph={{ rows: 3 }} />;
  }

  if (!timeline.data) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.approvals.timeline.empty')} />;
  }

  return <ApprovalTimelineView details={timeline.data} />;
};
