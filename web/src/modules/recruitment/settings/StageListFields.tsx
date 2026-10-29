import { ArrowDownOutlined, ArrowUpOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { Button, Flex, Form, Input, InputNumber, Select, Space } from 'antd';
import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { stageTypes } from './stages';

/**
 * The stages of a pipeline in the form's <c>stages</c> list: name, type and SLA, in order (D47). A template's and an
 * opening's own pipeline (D50) are edited with it.
 */
export const StageListFields = () => {
  const { t } = useTranslation();
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  return (
    <Form.List name="stages">
      {(fields, { add, remove, move }, { errors }) => (
        <Flex vertical gap={8} style={{ marginBottom: 16 }}>
          {fields.map((field, index) => (
            <Flex key={field.key} gap={8} align="start">
              <Form.Item
                name={[field.name, 'name', DEFAULT_CULTURE]}
                rules={required}
                style={{ flex: 2, marginBottom: 0 }}
              >
                <Input aria-label={t('web.recruitment.settings.stage_templates.stage_name')} maxLength={100} />
              </Form.Item>
              <Form.Item name={[field.name, 'type']} rules={required} style={{ flex: 1, marginBottom: 0 }}>
                <Select
                  aria-label={t('web.recruitment.settings.stage_templates.stage_type')}
                  options={stageTypes.map((type) => ({
                    value: type,
                    label: t(`web.recruitment.settings.stage_types.${type}`),
                  }))}
                />
              </Form.Item>
              <Form.Item name={[field.name, 'slaDays']} style={{ width: 110, marginBottom: 0 }}>
                <InputNumber
                  aria-label={t('web.recruitment.settings.stage_templates.sla_days')}
                  placeholder={t('web.recruitment.settings.stage_templates.sla_days')}
                  min={1}
                  max={365}
                  precision={0}
                  style={{ width: '100%' }}
                />
              </Form.Item>
              <Space.Compact>
                <Button
                  icon={<ArrowUpOutlined />}
                  disabled={index === 0}
                  onClick={() => move(index, index - 1)}
                  aria-label={t('web.recruitment.settings.move_up')}
                />
                <Button
                  icon={<ArrowDownOutlined />}
                  disabled={index === fields.length - 1}
                  onClick={() => move(index, index + 1)}
                  aria-label={t('web.recruitment.settings.move_down')}
                />
                <Button
                  icon={<DeleteOutlined />}
                  danger
                  onClick={() => remove(field.name)}
                  aria-label={t('web.common.delete')}
                />
              </Space.Compact>
            </Flex>
          ))}
          <Form.ErrorList errors={errors} />
          <Button
            icon={<PlusOutlined />}
            onClick={() => add({ type: 'Interview', slaDays: 5 }, Math.max(fields.length - 3, 1))}
          >
            {t('web.recruitment.settings.stage_templates.add_stage')}
          </Button>
        </Flex>
      )}
    </Form.List>
  );
};
