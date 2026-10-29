import { Card, Collapse, Descriptions, Empty, Flex, Space, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FeedbackItem } from '@/shared/api/generated/model';
import { formatDateTime } from '@/shared/format/date';
import { formatScore, recommendationColors } from './interviews';

export const RecommendationTag = ({ recommendation }: { recommendation: FeedbackItem['recommendation'] }) => {
  const { t } = useTranslation();
  return (
    <Tag color={recommendationColors[recommendation]}>
      {t(`web.recruitment.interviews.recommendations.${recommendation}`)}
    </Tag>
  );
};

interface FeedbackListProps {
  feedback: FeedbackItem[];
  /** The application's weighted score; shown to those who see every form. */
  score?: number | null;
  /** The blind rule keeps the others' forms until the reader sends theirs (D52). */
  hidden?: boolean;
}

/** The interviewers' forms of an application with the weighted score (PLAN R5, D52). */
export const FeedbackList = ({ feedback, score, hidden }: FeedbackListProps) => {
  const { t, i18n } = useTranslation();

  return (
    <Card
      size="small"
      title={t('web.recruitment.interviews.feedback.list')}
      extra={
        score != null && (
          <Typography.Text strong>
            {t('web.recruitment.interviews.feedback.score', { score: formatScore(score, i18n.language) })}
          </Typography.Text>
        )
      }
    >
      {hidden && (
        <Typography.Paragraph type="secondary">{t('web.recruitment.interviews.feedback.hidden')}</Typography.Paragraph>
      )}
      {feedback.length === 0 ? (
        !hidden && (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.recruitment.interviews.feedback.none')} />
        )
      ) : (
        <Collapse
          size="small"
          items={feedback.map((item) => ({
            key: item.id,
            label: (
              <Space wrap>
                {item.interviewerName ?? '—'}
                <RecommendationTag recommendation={item.recommendation} />
                <Typography.Text type="secondary">
                  {t('web.recruitment.interviews.feedback.weighted', {
                    score: formatScore(item.weightedScore, i18n.language),
                  })}
                </Typography.Text>
              </Space>
            ),
            children: (
              <Flex vertical gap={8}>
                <Descriptions
                  size="small"
                  column={1}
                  items={item.scores.map((s) => ({
                    key: s.criterionId,
                    label: s.criterion ?? '—',
                    children: (
                      <Space wrap>
                        <Typography.Text strong>{s.score}</Typography.Text>
                        {s.comment && <Typography.Text type="secondary">{s.comment}</Typography.Text>}
                      </Space>
                    ),
                  }))}
                />
                {item.answers.map((a) => (
                  <Flex key={a.questionId} vertical gap={2}>
                    <Typography.Text type="secondary">{a.question ?? '—'}</Typography.Text>
                    <Typography.Text style={{ whiteSpace: 'pre-wrap' }}>{a.text}</Typography.Text>
                  </Flex>
                ))}
                {item.summary && (
                  <Typography.Paragraph style={{ whiteSpace: 'pre-wrap', marginBottom: 0 }}>
                    {item.summary}
                  </Typography.Paragraph>
                )}
                <Typography.Text type="secondary">{formatDateTime(item.submittedAt, i18n.language)}</Typography.Text>
              </Flex>
            ),
          }))}
        />
      )}
    </Card>
  );
};
