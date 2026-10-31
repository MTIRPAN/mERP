import { Alert, Button, Card, Flex, List, Space, Tag, Tooltip, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { suggestInterviewQuestions } from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type {
  FeedbackSummaryPoint,
  FeedbackSummaryResponse,
  InterviewQuestionItem,
} from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { AiBadge } from '@/shared/components/AiBadge';

const prefix = 'web.recruitment.interviews.ai';

/** Runs an AI call; API errors are shown by the client, anything else is thrown. */
const useAiCall = <T,>(call: () => Promise<T>) => {
  const [result, setResult] = useState<T>();
  const [busy, setBusy] = useState(false);
  const run = async () => {
    setBusy(true);
    try {
      setResult(await call());
    } catch (error) {
      if (!isApiError(error)) {
        throw error;
      }
    } finally {
      setBusy(false);
    }
  };
  return { result, busy, run };
};

const QuestionList = ({ questions }: { questions: InterviewQuestionItem[] }) => {
  const { t } = useTranslation();
  return (
    <List
      size="small"
      dataSource={questions}
      renderItem={(q, index) => (
        <List.Item key={index}>
          <Flex vertical gap={4}>
            <Space size={4} wrap>
              <Tag color={q.criterion ? 'blue' : 'default'}>{q.criterion ?? t(`${prefix}.questions.general`)}</Tag>
              <Typography.Text strong>{q.question}</Typography.Text>
            </Space>
            {q.lookFor && (
              <Typography.Text type="secondary">
                {t(`${prefix}.questions.look_for`)}: {q.lookFor}
              </Typography.Text>
            )}
            {q.followUps.length > 0 && (
              <Typography.Text type="secondary">
                {t(`${prefix}.questions.follow_ups`)}: {q.followUps.join(' · ')}
              </Typography.Text>
            )}
          </Flex>
        </List.Item>
      )}
    />
  );
};

/**
 * Questions suggested for an interview from its opening and its form's criteria (PLAN A5, D58); nothing about the
 * candidate is sent and nothing is stored.
 */
export const InterviewQuestionsPanel = ({ interviewId }: { interviewId: string }) => {
  const { t } = useTranslation();
  const { result, busy, run } = useAiCall(() => suggestInterviewQuestions(interviewId));

  return (
    <Card
      size="small"
      title={
        <Space>
          {t(`${prefix}.questions.title`)}
          <AiBadge />
        </Space>
      }
      extra={
        <Button size="small" loading={busy} onClick={() => void run()}>
          {t(result ? `${prefix}.questions.again` : `${prefix}.questions.suggest`)}
        </Button>
      }
    >
      {result ? (
        <Flex vertical gap={8}>
          <Alert type="info" showIcon title={t(`${prefix}.questions.notice`)} />
          {result.questions.length > 0 ? (
            <QuestionList questions={result.questions} />
          ) : (
            <Typography.Text type="secondary">{t(`${prefix}.questions.none`)}</Typography.Text>
          )}
        </Flex>
      ) : (
        <Typography.Text type="secondary">{t(`${prefix}.questions.hint`)}</Typography.Text>
      )}
    </Card>
  );
};

const Points = ({ title, points }: { title: string; points: FeedbackSummaryPoint[] }) =>
  points.length > 0 && (
    <Flex vertical gap={4}>
      <Typography.Text strong>{title}</Typography.Text>
      <ul style={{ margin: 0, paddingInlineStart: 20 }}>
        {points.map((point, index) => (
          <li key={index}>
            {point.text}{' '}
            {point.interviewers.map((name) => (
              <Tag key={name} style={{ marginInlineStart: 4 }}>
                {name}
              </Tag>
            ))}
          </li>
        ))}
      </ul>
    </Flex>
  );

const Texts = ({ title, texts }: { title: string; texts: string[] }) =>
  texts.length > 0 && (
    <Flex vertical gap={4}>
      <Typography.Text strong>{title}</Typography.Text>
      <ul style={{ margin: 0, paddingInlineStart: 20 }}>
        {texts.map((text, index) => (
          <li key={index}>{text}</li>
        ))}
      </ul>
    </Flex>
  );

interface FeedbackSummaryPanelProps {
  /** Forms still missing; the summary waits for every interviewer (the blind rule, D52). */
  pending: number;
  summarize: () => Promise<FeedbackSummaryResponse>;
}

/** A common summary of an application's forms (PLAN A5, D58): a suggestion for the decision, never the decision. */
export const FeedbackSummaryPanel = ({ pending, summarize }: FeedbackSummaryPanelProps) => {
  const { t } = useTranslation();
  const { result, busy, run } = useAiCall(summarize);
  const button = (
    <Button size="small" loading={busy} disabled={pending > 0} onClick={() => void run()}>
      {t(result ? `${prefix}.summary.again` : `${prefix}.summary.create`)}
    </Button>
  );

  return (
    <Card
      size="small"
      title={
        <Space>
          {t(`${prefix}.summary.title`)}
          <AiBadge />
        </Space>
      }
      extra={
        pending > 0 ? <Tooltip title={t(`${prefix}.summary.pending`, { count: pending })}>{button}</Tooltip> : button
      }
    >
      {result ? (
        <Flex vertical gap={12}>
          <Alert type="info" showIcon title={t(`${prefix}.summary.notice`, { count: result.formCount })} />
          <Typography.Paragraph style={{ margin: 0 }}>{result.overview}</Typography.Paragraph>
          <Points title={t(`${prefix}.summary.strengths`)} points={result.strengths} />
          <Points title={t(`${prefix}.summary.concerns`)} points={result.concerns} />
          <Texts title={t(`${prefix}.summary.disagreements`)} texts={result.disagreements} />
          <Texts title={t(`${prefix}.summary.open_questions`)} texts={result.openQuestions} />
        </Flex>
      ) : (
        <Typography.Text type="secondary">
          {pending > 0 ? t(`${prefix}.summary.pending`, { count: pending }) : t(`${prefix}.summary.hint`)}
        </Typography.Text>
      )}
    </Card>
  );
};
