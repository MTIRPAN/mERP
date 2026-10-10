import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Col, Form, Input, InputNumber, Row, Select, Space, Switch } from 'antd';
import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { useMe } from '@/app/me/meContext';
import {
  getGetCustomFieldFormQueryKey,
  getListCustomFieldDefinitionsQueryKey,
  useCreateCustomField,
  useUpdateCustomField,
} from '@/shared/api/generated/endpoints/platform/platform';
import { CustomFieldType, type CustomFieldDefinitionItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';

interface FieldValues {
  code?: string;
  type?: CustomFieldType;
  companyId?: string;
  label?: Record<string, string | undefined>;
  required?: boolean;
  options?: { code?: string; label?: string }[];
  maxLength?: number | null;
  min?: number | null;
  max?: number | null;
  wholeNumber?: boolean;
  group?: string;
  order?: number;
  isActive?: boolean;
}

const blank = (value?: string) => (value?.trim() ? value.trim() : undefined);

interface CustomFieldDrawerProps {
  entityType: string;
  /** Definition to edit; undefined defines a new field. */
  definition?: CustomFieldDefinitionItem;
  open: boolean;
  onClose: () => void;
}

/**
 * Defines or changes a custom field (D12, D30). The code, data type and company are fixed after creation. Option
 * and group texts are edited in the default language; other languages keep their texts.
 */
export const CustomFieldDrawer = ({ entityType, definition, open, onClose }: CustomFieldDrawerProps) => {
  const { t } = useTranslation();
  const me = useMe();
  const [form] = Form.useForm<FieldValues>();
  const queryClient = useQueryClient();
  const create = useCreateCustomField();
  const update = useUpdateCustomField();
  const type = Form.useWatch('type', form) ?? definition?.type;

  const submit = async (values: FieldValues) => {
    const label = Object.fromEntries(
      Object.entries(values.label ?? {}).flatMap(([culture, text]) => (blank(text) ? [[culture, blank(text)!]] : [])),
    );
    const options = (values.options ?? []).map((option) => {
      const previous = definition?.options.find((o) => o.code === option.code)?.label ?? {};
      return {
        code: blank(option.code) ?? null,
        label: { ...previous, [DEFAULT_CULTURE]: option.label?.trim() ?? '' },
      };
    });
    const group = blank(values.group)
      ? { ...(definition?.group ?? {}), [DEFAULT_CULTURE]: blank(values.group)! }
      : null;
    const settings = {
      label,
      required: values.required ?? false,
      options: type === 'Select' || type === 'MultiSelect' ? options : [],
      maxLength: type === 'Text' ? (values.maxLength ?? null) : null,
      min: type === 'Number' ? (values.min ?? null) : null,
      max: type === 'Number' ? (values.max ?? null) : null,
      wholeNumber: type === 'Number' && !!values.wholeNumber,
      group,
      order: values.order ?? 0,
    };

    if (definition) {
      await update.mutateAsync({
        id: definition.id,
        data: { ...settings, version: definition.version, isActive: values.isActive ?? true },
      });
    } else {
      await create.mutateAsync({
        data: {
          ...settings,
          entityType,
          code: blank(values.code) ?? null,
          type: values.type ?? CustomFieldType.Text,
          companyId: values.companyId ?? null,
        },
      });
    }

    await queryClient.invalidateQueries({ queryKey: getListCustomFieldDefinitionsQueryKey() });
    // Forms and lists read the active fields
    await queryClient.invalidateQueries({ queryKey: [getGetCustomFieldFormQueryKey({ entityType })[0]] });
  };

  const initialValues: FieldValues = definition
    ? {
        label: definition.label,
        required: definition.required,
        options: definition.options.map((o) => ({ code: o.code, label: o.label[DEFAULT_CULTURE] })),
        maxLength: definition.maxLength,
        min: definition.min,
        max: definition.max,
        wholeNumber: definition.wholeNumber,
        group: definition.group?.[DEFAULT_CULTURE],
        order: definition.order,
        isActive: definition.isActive,
      }
    : { type: CustomFieldType.Text, required: false, order: 0, options: [] };

  const required = [{ required: true, message: t('web.platform.fields.required') }];

  return (
    <FormDrawer<FieldValues>
      key={definition ? `${definition.id}-${definition.version}` : 'new'}
      form={form}
      title={t(definition ? 'web.custom_fields.edit' : 'web.custom_fields.new')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      initialValues={initialValues}
      width={600}
      fieldAliases={{ label: ['label', DEFAULT_CULTURE], group: ['group'] }}
    >
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item
            name="code"
            label={t('web.custom_fields.code')}
            tooltip={t('web.custom_fields.code_hint')}
            rules={definition ? undefined : required}
            initialValue={definition?.code}
          >
            <Input maxLength={50} disabled={!!definition} style={{ fontFamily: 'monospace' }} />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="type" label={t('web.custom_fields.type')} rules={required} initialValue={definition?.type}>
            <Select
              disabled={!!definition}
              options={Object.values(CustomFieldType).map((value) => ({
                value,
                label: t(`web.custom_fields.types.${value}`),
              }))}
            />
          </Form.Item>
        </Col>
      </Row>
      {me.cultures.map((culture) => (
        <Form.Item
          key={culture.code}
          name={['label', culture.code]}
          label={
            me.cultures.length > 1
              ? t('web.custom_fields.label_in', { language: culture.name })
              : t('web.custom_fields.label')
          }
          rules={culture.code === DEFAULT_CULTURE ? required : undefined}
        >
          <Input maxLength={100} />
        </Form.Item>
      ))}
      <Form.Item
        name="companyId"
        label={t('web.custom_fields.company')}
        tooltip={t('web.custom_fields.company_hint')}
        initialValue={definition?.companyId ?? undefined}
      >
        <Select
          allowClear
          disabled={!!definition}
          placeholder={t('web.custom_fields.all_companies')}
          options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
        />
      </Form.Item>
      {(type === 'Select' || type === 'MultiSelect') && (
        <Form.List name="options">
          {(items, { add, remove }, { errors }) => (
            <Form.Item label={t('web.custom_fields.options')} required>
              {items.map(({ key, name }) => (
                <Space key={key} align="start" style={{ display: 'flex' }}>
                  <Form.Item name={[name, 'code']} rules={required} style={{ marginBottom: 8 }}>
                    <Input
                      placeholder={t('web.custom_fields.option_code')}
                      maxLength={50}
                      style={{ fontFamily: 'monospace' }}
                    />
                  </Form.Item>
                  <Form.Item name={[name, 'label']} rules={required} style={{ marginBottom: 8 }}>
                    <Input placeholder={t('web.custom_fields.option_label')} maxLength={100} />
                  </Form.Item>
                  <Button
                    type="text"
                    icon={<DeleteOutlined />}
                    aria-label={t('web.custom_fields.remove_option')}
                    onClick={() => remove(name)}
                  />
                </Space>
              ))}
              <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({})}>
                {t('web.custom_fields.add_option')}
              </Button>
              <Form.ErrorList errors={errors} />
            </Form.Item>
          )}
        </Form.List>
      )}
      {type === 'Text' && (
        <Form.Item name="maxLength" label={t('web.custom_fields.max_length')}>
          <InputNumber min={1} max={4000} style={{ width: '100%' }} />
        </Form.Item>
      )}
      {type === 'Number' && (
        <Row gutter={12}>
          <Col span={8}>
            <Form.Item name="min" label={t('web.custom_fields.min')}>
              <InputNumber style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={8}>
            <Form.Item name="max" label={t('web.custom_fields.max')}>
              <InputNumber style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={8}>
            <Form.Item name="wholeNumber" label={t('web.custom_fields.whole_number')} valuePropName="checked">
              <Switch />
            </Form.Item>
          </Col>
        </Row>
      )}
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item name="group" label={t('web.custom_fields.group')} tooltip={t('web.custom_fields.group_hint')}>
            <Input maxLength={100} />
          </Form.Item>
        </Col>
        <Col span={6}>
          <Form.Item name="order" label={t('web.custom_fields.order')}>
            <InputNumber style={{ width: '100%' }} />
          </Form.Item>
        </Col>
        <Col span={6}>
          <Form.Item name="required" label={t('web.custom_fields.required')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </Col>
      </Row>
      {definition && (
        <Form.Item
          name="isActive"
          label={t('web.platform.fields.active')}
          valuePropName="checked"
          extra={t('web.custom_fields.inactive_hint')}
        >
          <Switch />
        </Form.Item>
      )}
    </FormDrawer>
  );
};
