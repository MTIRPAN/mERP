import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Card, Checkbox, Empty, Flex, Form, Skeleton, Space, Tag, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import {
  getListStageTemplatesQueryKey,
  useCreateStageTemplate,
  useListStageTemplates,
  useUpdateStageTemplate,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { StageTemplateItem, StageType } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { LocalizedNameFields } from './LocalizedNameFields';
import { StageListFields } from './StageListFields';
import { finalTypes, stageTypeColors, toStageInputs, type StageValues } from './stages';

interface TemplateValues {
  name?: Record<string, string>;
  stages?: StageValues[];
  isDefault?: boolean;
  isActive?: boolean;
}

/** A new template starts with the stages every pipeline needs. */
const newStages = (t: (key: string) => string): StageValues[] =>
  (['Applied', 'Screening', 'Interview', 'Offer', 'Hired', 'Rejected', 'Withdrawn'] as StageType[]).map((type) => ({
    type,
    name: { [DEFAULT_CULTURE]: t(`web.recruitment.settings.stage_types.${type}`) },
    slaDays: finalTypes.includes(type) ? null : 5,
  }));

const StageTemplateDrawer = ({ template, onClose }: { template?: StageTemplateItem; onClose: () => void }) => {
  const { t } = useTranslation();
  const [form] = Form.useForm<TemplateValues>();
  const queryClient = useQueryClient();
  const create = useCreateStageTemplate();
  const update = useUpdateStageTemplate();

  const submit = async () => {
    // Every value of the store: other languages of the stage names are kept though only the default one is edited
    const values = form.getFieldsValue(true) as TemplateValues;
    const stages = toStageInputs(values.stages);
    if (template) {
      await update.mutateAsync({
        id: template.id,
        data: {
          version: template.version,
          name: values.name ?? {},
          stages,
          isDefault: !!values.isDefault,
          isActive: values.isActive ?? true,
        },
      });
    } else {
      await create.mutateAsync({ data: { name: values.name ?? {}, stages, isDefault: !!values.isDefault } });
    }

    await queryClient.invalidateQueries({ queryKey: getListStageTemplatesQueryKey() });
  };

  return (
    <FormDrawer<TemplateValues>
      title={template ? template.name : t('web.recruitment.settings.stage_templates.new')}
      open
      width={720}
      form={form}
      onClose={onClose}
      onSubmit={submit}
      initialValues={
        template
          ? {
              name: template.names,
              stages: template.stages.map((s) => ({ id: s.id, name: s.names, type: s.type, slaDays: s.slaDays })),
              isDefault: template.isDefault,
              isActive: template.isActive,
            }
          : { stages: newStages(t), isActive: true }
      }
    >
      <LocalizedNameFields />
      <Typography.Paragraph type="secondary">
        {t('web.recruitment.settings.stage_templates.rules')}
      </Typography.Paragraph>
      <StageListFields />
      <Flex gap={16}>
        <Form.Item name="isDefault" valuePropName="checked">
          <Checkbox>{t('web.recruitment.settings.stage_templates.is_default')}</Checkbox>
        </Form.Item>
        {template && (
          <Form.Item name="isActive" valuePropName="checked">
            <Checkbox>{t('web.recruitment.settings.active')}</Checkbox>
          </Form.Item>
        )}
      </Flex>
    </FormDrawer>
  );
};

/** The hiring pipelines job openings copy (PLAN R1, D47): one is the default; the tenant's admins change them. */
export const StageTemplatesTab = ({ canManage }: { canManage: boolean }) => {
  const { t } = useTranslation();
  const templates = useListStageTemplates({ includeInactive: true });
  const [editing, setEditing] = useState<{ template?: StageTemplateItem }>();

  if (templates.isLoading) {
    return <Skeleton active />;
  }

  return (
    <Flex vertical gap={12}>
      {canManage && (
        <Flex justify="flex-end">
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing({})}>
            {t('web.recruitment.settings.stage_templates.new')}
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
              {template.isDefault && <Tag color="blue">{t('web.recruitment.settings.stage_templates.default')}</Tag>}
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
          <Flex gap={6} wrap>
            {template.stages.map((stage) => (
              <Tag key={stage.id} color={stageTypeColors[stage.type]}>
                {stage.name}
                {stage.slaDays
                  ? ` · ${t('web.recruitment.settings.stage_templates.days', { days: stage.slaDays })}`
                  : ''}
              </Tag>
            ))}
          </Flex>
        </Card>
      ))}
      {editing && <StageTemplateDrawer template={editing.template} onClose={() => setEditing(undefined)} />}
    </Flex>
  );
};
