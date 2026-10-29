import { App, Button, Card, Flex, Form, Input, Radio, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { submitInterviewFeedback } from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { EvaluationForm, FeedbackItem, Recommendation } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import { SCORES, criteriaOf, recommendations } from './interviews';

interface FeedbackValues {
  scores?: Record<string, { score?: number; comment?: string }>;
  answers?: Record<string, string>;
  recommendation?: Recommendation;
  summary?: string;
}

const initialValues = (mine: FeedbackItem | null): FeedbackValues =>
  mine
    ? {
        scores: Object.fromEntries(
          mine.scores.map((s) => [s.criterionId, { score: s.score, comment: s.comment ?? undefined }]),
        ),
        answers: Object.fromEntries(mine.answers.map((a) => [a.questionId, a.text])),
        recommendation: mine.recommendation,
        summary: mine.summary ?? undefined,
      }
    : {};

interface FeedbackFormProps {
  interviewId: string;
  form: EvaluationForm;
  mine: FeedbackItem | null;
  onSent: () => Promise<unknown>;
}

/**
 * The interviewer's evaluation form (PLAN R5, D52): a score of 1–5 for every criterion with the meaning of each score,
 * the open questions and the overall recommendation. It may be corrected after it is sent.
 */
export const FeedbackForm = ({ interviewId, form: evaluation, mine, onSent }: FeedbackFormProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FeedbackValues>();
  const [saving, setSaving] = useState(false);
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: FeedbackValues) => {
    setSaving(true);
    try {
      await submitInterviewFeedback(interviewId, {
        scores: criteriaOf(evaluation).map((c) => ({
          criterionId: c.id,
          score: values.scores?.[c.id]?.score ?? 0,
          comment: values.scores?.[c.id]?.comment?.trim() || null,
        })),
        answers: evaluation.questions
          .filter((q) => values.answers?.[q.id]?.trim())
          .map((q) => ({ questionId: q.id, text: values.answers![q.id]!.trim() })),
        recommendation: values.recommendation!,
        summary: values.summary?.trim() || null,
      });
      void message.success(t('web.recruitment.interviews.feedback.sent'));
      await onSent();
    } catch (error) {
      if (!applyApiErrorsToForm(form, error)) {
        void message.error(isApiError(error) ? error.message : String(error));
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Card
      size="small"
      title={t(mine ? 'web.recruitment.interviews.feedback.correct' : 'web.recruitment.interviews.feedback.title')}
    >
      <Form<FeedbackValues>
        form={form}
        layout="vertical"
        initialValues={initialValues(mine)}
        onFinish={(v) => void submit(v)}
      >
        {evaluation.sections.map((section) => (
          <Flex key={section.title} vertical gap={4} style={{ marginBottom: 8 }}>
            <Typography.Title level={5} style={{ marginTop: 0 }}>
              {section.title}
            </Typography.Title>
            {section.criteria.map((criterion) => (
              <Flex key={criterion.id} vertical>
                <Form.Item
                  name={['scores', criterion.id, 'score']}
                  label={t('web.recruitment.interviews.feedback.criterion', {
                    name: criterion.name,
                    weight: criterion.weight,
                  })}
                  rules={required}
                  style={{ marginBottom: 4 }}
                >
                  <Radio.Group
                    optionType="button"
                    aria-label={criterion.name}
                    options={SCORES.map((score) => ({
                      value: score,
                      label: String(score),
                      title: criterion.scoreDescriptions[score - 1] || undefined,
                    }))}
                  />
                </Form.Item>
                <Form.Item noStyle shouldUpdate>
                  {() => {
                    const score = form.getFieldValue(['scores', criterion.id, 'score']) as number | undefined;
                    const meaning = score ? criterion.scoreDescriptions[score - 1] : undefined;
                    return meaning ? (
                      <Typography.Text type="secondary" style={{ marginBottom: 4 }}>
                        {meaning}
                      </Typography.Text>
                    ) : null;
                  }}
                </Form.Item>
                <Form.Item name={['scores', criterion.id, 'comment']} style={{ marginBottom: 12 }}>
                  <Input
                    aria-label={t('web.recruitment.interviews.feedback.comment', { name: criterion.name })}
                    placeholder={t('web.recruitment.interviews.feedback.comment_placeholder')}
                    maxLength={1000}
                  />
                </Form.Item>
              </Flex>
            ))}
          </Flex>
        ))}
        {evaluation.questions.map((question) => (
          <Form.Item key={question.id} name={['answers', question.id]} label={question.text}>
            <Input.TextArea rows={2} maxLength={4000} />
          </Form.Item>
        ))}
        <Form.Item
          name="recommendation"
          label={t('web.recruitment.interviews.feedback.recommendation')}
          rules={required}
        >
          <Radio.Group
            optionType="button"
            options={recommendations.map((r) => ({
              value: r,
              label: t(`web.recruitment.interviews.recommendations.${r}`),
            }))}
          />
        </Form.Item>
        <Form.Item name="summary" label={t('web.recruitment.interviews.feedback.summary')}>
          <Input.TextArea rows={3} maxLength={4000} />
        </Form.Item>
        <Button type="primary" htmlType="submit" loading={saving}>
          {t(mine ? 'web.recruitment.interviews.feedback.resend' : 'web.recruitment.interviews.feedback.send')}
        </Button>
      </Form>
    </Card>
  );
};
