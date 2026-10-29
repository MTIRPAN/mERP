import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Card, Checkbox, Empty, Flex, Form, Input, InputNumber, Skeleton, Space, Tag, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getListEvaluationTemplatesQueryKey,
  getListInterviewTemplatesQueryKey,
  useCreateEvaluationTemplate,
  useListEvaluationTemplates,
  useUpdateEvaluationTemplate,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { EvaluationForm, EvaluationTemplateItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { SCORES } from '../interviews/interviews';

/** The server gives new criteria and questions their ids. */
const NEW_ID = '00000000-0000-0000-0000-000000000000';

interface TemplateValues {
  name?: string;
  form?: EvaluationForm;
  isActive?: boolean;
}

const newCriterion = () => ({ id: NEW_ID, name: '', scoreDescriptions: SCORES.map(() => ''), weight: 1 });

/** A form of the template's sections, criteria with the meaning of each score and a weight, and open questions. */
const FormFields = () => {
  const { t } = useTranslation();
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  return (
    <>
      <Form.List name={['form', 'sections']}>
        {(sections, { add, remove }) => (
          <Flex vertical gap={12} style={{ marginBottom: 16 }}>
            {sections.map((section) => (
              <Card
                key={section.key}
                size="small"
                title={
                  <Form.Item name={[section.name, 'title']} rules={required} style={{ marginBottom: 0 }}>
                    <Input
                      aria-label={t('web.recruitment.settings.evaluation.section_title')}
                      placeholder={t('web.recruitment.settings.evaluation.section_title')}
                      maxLength={150}
                    />
                  </Form.Item>
                }
                extra={
                  <Button
                    size="small"
                    danger
                    icon={<DeleteOutlined />}
                    onClick={() => remove(section.name)}
                    aria-label={t('web.recruitment.settings.evaluation.remove_section')}
                  />
                }
              >
                <Form.List name={[section.name, 'criteria']}>
                  {(criteria, { add: addCriterion, remove: removeCriterion }) => (
                    <Flex vertical gap={12}>
                      {criteria.map((criterion) => (
                        <Flex key={criterion.key} vertical gap={4}>
                          <Flex gap={8} align="start">
                            <Form.Item
                              name={[criterion.name, 'name']}
                              rules={required}
                              style={{ flex: 1, marginBottom: 0 }}
                            >
                              <Input
                                aria-label={t('web.recruitment.settings.evaluation.criterion')}
                                placeholder={t('web.recruitment.settings.evaluation.criterion')}
                                maxLength={150}
                              />
                            </Form.Item>
                            <Form.Item
                              name={[criterion.name, 'weight']}
                              rules={required}
                              style={{ width: 120, marginBottom: 0 }}
                            >
                              <InputNumber
                                aria-label={t('web.recruitment.settings.evaluation.weight')}
                                prefix={t('web.recruitment.settings.evaluation.weight_prefix')}
                                min={1}
                                max={100}
                                precision={0}
                                style={{ width: '100%' }}
                              />
                            </Form.Item>
                            <Button
                              danger
                              icon={<DeleteOutlined />}
                              onClick={() => removeCriterion(criterion.name)}
                              aria-label={t('web.recruitment.settings.evaluation.remove_criterion')}
                            />
                          </Flex>
                          <Flex gap={4} wrap>
                            {SCORES.map((score, index) => (
                              <Form.Item
                                key={score}
                                name={[criterion.name, 'scoreDescriptions', index]}
                                style={{ flex: '1 1 120px', marginBottom: 0 }}
                              >
                                <Input
                                  size="small"
                                  prefix={`${score}`}
                                  aria-label={t('web.recruitment.settings.evaluation.score_description', { score })}
                                  placeholder={t('web.recruitment.settings.evaluation.score_description', { score })}
                                  maxLength={300}
                                />
                              </Form.Item>
                            ))}
                          </Flex>
                        </Flex>
                      ))}
                      <Button size="small" icon={<PlusOutlined />} onClick={() => addCriterion(newCriterion())}>
                        {t('web.recruitment.settings.evaluation.add_criterion')}
                      </Button>
                    </Flex>
                  )}
                </Form.List>
              </Card>
            ))}
            <Button icon={<PlusOutlined />} onClick={() => add({ title: '', criteria: [newCriterion()] })}>
              {t('web.recruitment.settings.evaluation.add_section')}
            </Button>
          </Flex>
        )}
      </Form.List>
      <Typography.Title level={5}>{t('web.recruitment.settings.evaluation.questions')}</Typography.Title>
      <Form.List name={['form', 'questions']}>
        {(questions, { add, remove }) => (
          <Flex vertical gap={8} style={{ marginBottom: 16 }}>
            {questions.map((question) => (
              <Flex key={question.key} gap={8} align="start">
                <Form.Item name={[question.name, 'text']} rules={required} style={{ flex: 1, marginBottom: 0 }}>
                  <Input aria-label={t('web.recruitment.settings.evaluation.question')} maxLength={300} />
                </Form.Item>
                <Button
                  danger
                  icon={<DeleteOutlined />}
                  onClick={() => remove(question.name)}
                  aria-label={t('web.common.delete')}
                />
              </Flex>
            ))}
            <Button icon={<PlusOutlined />} onClick={() => add({ id: NEW_ID, text: '' })}>
              {t('web.recruitment.settings.evaluation.add_question')}
            </Button>
          </Flex>
        )}
      </Form.List>
    </>
  );
};

/** Empty descriptions are kept as empty strings: every criterion has one for each score. */
const toForm = (form: EvaluationForm | undefined): EvaluationForm => ({
  sections: (form?.sections ?? []).map((s) => ({
    title: s.title?.trim() ?? '',
    criteria: (s.criteria ?? []).map((c) => ({
      id: c.id ?? NEW_ID,
      name: c.name?.trim() ?? '',
      weight: c.weight ?? 1,
      scoreDescriptions: SCORES.map((_, i) => c.scoreDescriptions?.[i]?.trim() ?? ''),
    })),
  })),
  questions: (form?.questions ?? []).map((q) => ({ id: q.id ?? NEW_ID, text: q.text?.trim() ?? '' })),
});

const TemplateDrawer = ({ template, onClose }: { template?: EvaluationTemplateItem; onClose: () => void }) => {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const create = useCreateEvaluationTemplate();
  const [form] = Form.useForm<TemplateValues>();
  const update = useUpdateEvaluationTemplate();

  const submit = async () => {
    // Every value of the store: the ids of the criteria and questions have no field of their own
    const values = form.getFieldsValue(true) as TemplateValues;
    const evaluation = toForm(values.form);
    const name = values.name?.trim() ?? '';
    if (template) {
      await update.mutateAsync({
        id: template.id,
        data: { version: template.version, name, form: evaluation, isActive: values.isActive ?? true },
      });
    } else {
      await create.mutateAsync({ data: { name, form: evaluation } });
    }

    await Promise.all([
      queryClient.invalidateQueries({ queryKey: getListEvaluationTemplatesQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getListInterviewTemplatesQueryKey() }),
    ]);
  };

  return (
    <FormDrawer<TemplateValues>
      title={template ? template.name : t('web.recruitment.settings.evaluation.new')}
      open
      width={760}
      form={form}
      onClose={onClose}
      onSubmit={submit}
      initialValues={
        template
          ? { name: template.name, form: template.form, isActive: template.isActive }
          : { form: { sections: [{ title: '', criteria: [newCriterion()] }], questions: [] }, isActive: true }
      }
    >
      <Form.Item
        name="name"
        label={t('web.recruitment.settings.evaluation.name')}
        rules={[{ required: true, message: t('web.platform.fields.required') }]}
      >
        <Input maxLength={150} />
      </Form.Item>
      <Typography.Paragraph type="secondary">{t('web.recruitment.settings.evaluation.rules')}</Typography.Paragraph>
      <FormFields />
      {template && (
        <Form.Item name="isActive" valuePropName="checked">
          <Checkbox>{t('web.recruitment.settings.active')}</Checkbox>
        </Form.Item>
      )}
    </FormDrawer>
  );
};

/**
 * The evaluation templates interviews are planned with (PLAN R5, D52): sections of criteria scored 1–5 with a weight,
 * and open questions. An interview keeps its copy of the form, so a change applies to interviews planned later.
 */
export const EvaluationTemplatesTab = ({ canManage }: { canManage: boolean }) => {
  const { t } = useTranslation();
  const templates = useListEvaluationTemplates({ includeInactive: true });
  const [editing, setEditing] = useState<{ template?: EvaluationTemplateItem }>();

  if (templates.isLoading) {
    return <Skeleton active />;
  }

  return (
    <Flex vertical gap={12}>
      {canManage && (
        <Flex justify="flex-end">
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing({})}>
            {t('web.recruitment.settings.evaluation.new')}
          </Button>
        </Flex>
      )}
      {(templates.data ?? []).length === 0 && <Empty />}
      {(templates.data ?? []).map((template) => (
        <Card
          key={template.id}
          size="small"
          title={
            <Space>
              {template.name}
              {!template.isActive && <Tag>{t('web.recruitment.settings.inactive')}</Tag>}
            </Space>
          }
          extra={
            canManage && (
              <Button size="small" icon={<EditOutlined />} onClick={() => setEditing({ template })}>
                {t('web.common.edit')}
              </Button>
            )
          }
        >
          <Flex vertical gap={4}>
            {template.form.sections.map((section) => (
              <Flex key={section.title} gap={6} wrap align="center">
                <Typography.Text strong>{section.title}:</Typography.Text>
                {section.criteria.map((c) => (
                  <Tag key={c.id}>
                    {c.name} · {t('web.recruitment.settings.evaluation.weight_short', { weight: c.weight })}
                  </Tag>
                ))}
              </Flex>
            ))}
            {template.form.questions.length > 0 && (
              <Typography.Text type="secondary">
                {t('web.recruitment.settings.evaluation.question_count', { count: template.form.questions.length })}
              </Typography.Text>
            )}
          </Flex>
        </Card>
      ))}
      {editing && <TemplateDrawer template={editing.template} onClose={() => setEditing(undefined)} />}
    </Flex>
  );
};
