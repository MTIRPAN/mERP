import { Form, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { updateOpeningStages } from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { OpeningDetails } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { StageListFields } from '../settings/StageListFields';
import { toStageInputs, type StageValues } from '../settings/stages';
import { useOpeningRefresh } from './openings';

/**
 * The opening's own pipeline (D50): stages are added, renamed, reordered or removed under the rules of a template;
 * a stage with applications stays.
 */
export const OpeningStagesDrawer = ({ details, onClose }: { details: OpeningDetails; onClose: () => void }) => {
  const { t } = useTranslation();
  const [form] = Form.useForm<{ stages?: StageValues[] }>();
  const refresh = useOpeningRefresh();
  const id = details.opening.id;

  const submit = async () => {
    // Every value of the store: other languages of the stage names are kept though only the default one is edited
    const values = form.getFieldsValue(true) as { stages?: StageValues[] };
    await updateOpeningStages(id, { version: details.version, stages: toStageInputs(values.stages) });
    await refresh(id);
  };

  return (
    <FormDrawer<{ stages?: StageValues[] }>
      title={t('web.recruitment.openings.stages_title', { title: details.opening.title })}
      open
      width={720}
      form={form}
      onClose={onClose}
      onSubmit={submit}
      initialValues={{
        stages: details.stages.map((s) => ({ id: s.id, name: s.names, type: s.type, slaDays: s.slaDays })),
      }}
    >
      <Typography.Paragraph type="secondary">{t('web.recruitment.openings.stages_hint')}</Typography.Paragraph>
      <StageListFields />
    </FormDrawer>
  );
};
