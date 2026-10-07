import { useQueryClient } from '@tanstack/react-query';
import { Form, Input, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import {
  getListNumberSequencesQueryKey,
  useAddCompanyNumberSequence,
  useUpdateNumberSequence,
} from '@/shared/api/generated/endpoints/platform/platform';
import { NumberResetPeriod, type NumberSequenceItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { formatPattern, validatePattern } from './numberPattern';

interface SequenceValues {
  documentType?: string;
  companyId?: string;
  pattern?: string;
  resetPeriod?: NumberResetPeriod;
}

interface NumberSequenceDrawerProps {
  /** Sequence to edit; undefined adds a company's own pattern. */
  sequence?: NumberSequenceItem;
  /** Defaults of the document types, for the new company pattern. */
  defaults: NumberSequenceItem[];
  open: boolean;
  onClose: () => void;
}

const Preview = () => {
  const { t } = useTranslation();
  const pattern = Form.useWatch<string | undefined>('pattern') ?? '';
  const reset = Form.useWatch<NumberResetPeriod | undefined>('resetPeriod') ?? 'Never';
  const valid = !validatePattern(pattern, reset);
  return (
    <Typography.Paragraph type="secondary">
      {t('web.numbering.preview')}{' '}
      <Typography.Text strong code>
        {valid ? formatPattern(pattern, new Date(), 1) : '—'}
      </Typography.Text>
    </Typography.Paragraph>
  );
};

/** Changes a pattern, or gives a company its own pattern for a document type (D29). */
export const NumberSequenceDrawer = ({ sequence, defaults, open, onClose }: NumberSequenceDrawerProps) => {
  const { t } = useTranslation();
  const me = useMe();
  const queryClient = useQueryClient();
  const add = useAddCompanyNumberSequence();
  const update = useUpdateNumberSequence();

  const submit = async (values: SequenceValues) => {
    const pattern = values.pattern?.trim() ?? '';
    const resetPeriod = values.resetPeriod ?? NumberResetPeriod.Never;
    if (sequence) {
      await update.mutateAsync({ id: sequence.id, data: { version: sequence.version, pattern, resetPeriod } });
    } else {
      await add.mutateAsync({
        data: { documentType: values.documentType ?? null, companyId: values.companyId ?? null, pattern, resetPeriod },
      });
    }

    await queryClient.invalidateQueries({ queryKey: getListNumberSequencesQueryKey() });
  };

  const required = [{ required: true, message: t('web.platform.fields.required') }];

  return (
    <FormDrawer<SequenceValues>
      key={sequence ? `${sequence.id}-${sequence.version}` : 'new'}
      title={t(sequence ? 'web.numbering.edit' : 'web.numbering.new_company')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      initialValues={
        sequence
          ? { pattern: sequence.pattern, resetPeriod: sequence.resetPeriod }
          : { resetPeriod: NumberResetPeriod.Yearly }
      }
    >
      {sequence ? (
        <Typography.Paragraph>
          <Typography.Text strong>{sequence.label}</Typography.Text>
          {' · '}
          {sequence.companyName ?? t('web.numbering.all_companies')}
        </Typography.Paragraph>
      ) : (
        <>
          <Form.Item name="documentType" label={t('web.numbering.document_type')} rules={required}>
            <Select
              showSearch={{ optionFilterProp: 'label' }}
              options={defaults.map((d) => ({ value: d.documentType, label: d.label }))}
            />
          </Form.Item>
          <Form.Item name="companyId" label={t('web.numbering.company')} rules={required}>
            <Select
              showSearch={{ optionFilterProp: 'label' }}
              options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
            />
          </Form.Item>
        </>
      )}
      <Form.Item
        name="pattern"
        label={t('web.numbering.pattern')}
        extra={t('web.numbering.pattern_hint')}
        rules={[
          ...required,
          {
            // The period rule is checked by the API, which names the missing part
            validator: (_, value?: string) =>
              value && validatePattern(value, 'Never')
                ? Promise.reject(new Error(t('web.numbering.pattern_invalid')))
                : Promise.resolve(),
          },
        ]}
      >
        <Input maxLength={50} style={{ fontFamily: 'monospace' }} />
      </Form.Item>
      <Form.Item
        name="resetPeriod"
        label={t('web.numbering.reset')}
        tooltip={t('web.numbering.reset_hint')}
        rules={required}
      >
        <Select
          options={Object.values(NumberResetPeriod).map((value) => ({
            value,
            label: t(`web.numbering.reset_period.${value}`),
          }))}
        />
      </Form.Item>
      <Preview />
    </FormDrawer>
  );
};
