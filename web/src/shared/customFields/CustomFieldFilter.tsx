import { DatePicker, Input, InputNumber, Select, Space } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { CustomFieldFormItem } from '@/shared/api/generated/model';
import { toApiValue } from './customFieldValues';

/**
 * Filter of a list by one custom field (D30): pick the field and a value; the list gets the JSON filter of the API
 * (`{"code": value}`), or undefined when cleared. Text and user fields match the exact value.
 */
export const CustomFieldFilter = ({
  fields,
  onChange,
}: {
  fields: CustomFieldFormItem[];
  onChange: (filter: string | undefined) => void;
}) => {
  const { t } = useTranslation();
  const [code, setCode] = useState<string>();
  const field = fields.find((f) => f.code === code);

  const apply = (value: unknown) => {
    const apiValue = field ? toApiValue(field, value) : null;
    if (!field || apiValue === null) {
      onChange(undefined);
      return;
    }

    // A multi-select record matches when it holds the chosen option
    onChange(JSON.stringify({ [field.code]: field.type === 'MultiSelect' ? [apiValue] : apiValue }));
  };

  if (fields.length === 0) {
    return null;
  }

  const options = field?.options.map((o) => ({ value: o.code, label: o.label })) ?? [];
  return (
    <Space.Compact>
      <Select
        allowClear
        style={{ minWidth: 180 }}
        placeholder={t('web.custom_fields.filter')}
        aria-label={t('web.custom_fields.filter')}
        value={code}
        onChange={(next?: string) => {
          setCode(next);
          onChange(undefined);
        }}
        options={fields.map((f) => ({ value: f.code, label: f.label }))}
      />
      {field?.type === 'Number' && (
        <InputNumber aria-label={field.label} style={{ width: 140 }} onChange={(v) => apply(v)} />
      )}
      {field?.type === 'Date' && <DatePicker aria-label={field.label} format="L" onChange={(v) => apply(v)} />}
      {field?.type === 'Bool' && (
        <Select
          allowClear
          aria-label={field.label}
          style={{ width: 120 }}
          onChange={(v?: boolean) => apply(v)}
          options={[
            { value: true, label: t('web.common.yes') },
            { value: false, label: t('web.common.no') },
          ]}
        />
      )}
      {(field?.type === 'Select' || field?.type === 'MultiSelect') && (
        <Select
          allowClear
          aria-label={field.label}
          style={{ minWidth: 160 }}
          onChange={(v?: string) => apply(v)}
          options={options}
        />
      )}
      {field && ['Text', 'User', 'Employee'].includes(field.type) && (
        <Input.Search allowClear aria-label={field.label} style={{ width: 200 }} onSearch={(v) => apply(v.trim())} />
      )}
    </Space.Compact>
  );
};
