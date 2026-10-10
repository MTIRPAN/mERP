import { DatePicker, Divider, Form, Input, InputNumber, Select, Switch } from 'antd';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useListUsers } from '@/shared/api/generated/endpoints/platform/platform';
import type { CustomFieldFormItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';

/** Users can be picked by name with platform.users.read; otherwise the id is typed. */
const UserInput = ({ value, onChange }: { value?: string; onChange?: (value?: string) => void }) => {
  const { t } = useTranslation();
  const canList = useHasPermission('platform.users.read');
  const [search, setSearch] = useState<string>();
  const users = useListUsers({ search, pageSize: 20 }, { query: { enabled: canList } });
  if (!canList) {
    return (
      <Input
        value={value}
        onChange={(e) => onChange?.(e.target.value || undefined)}
        placeholder={t('web.custom_fields.id_placeholder')}
      />
    );
  }

  return (
    <Select
      allowClear
      showSearch={{ filterOption: false, onSearch: (text) => setSearch(text.trim() || undefined) }}
      value={value}
      onChange={onChange}
      loading={users.isFetching}
      options={users.data?.items.map((user) => ({ value: user.id, label: `${user.displayName} (${user.userName})` }))}
    />
  );
};

const editor = (field: CustomFieldFormItem, placeholder: string): ReactNode => {
  switch (field.type) {
    case 'Number':
      return (
        <InputNumber
          style={{ width: '100%' }}
          min={field.min ?? undefined}
          max={field.max ?? undefined}
          precision={field.wholeNumber ? 0 : undefined}
        />
      );
    case 'Date':
      return <DatePicker style={{ width: '100%' }} format="L" />;
    case 'Bool':
      return <Switch />;
    case 'Select':
      return <Select allowClear options={field.options.map((o) => ({ value: o.code, label: o.label }))} />;
    case 'MultiSelect':
      return (
        <Select mode="multiple" allowClear options={field.options.map((o) => ({ value: o.code, label: o.label }))} />
      );
    case 'User':
      return <UserInput />;
    case 'Employee':
      // Picked from the personnel list once the HR module exists (H2)
      return <Input placeholder={placeholder} />;
    default:
      return <Input maxLength={field.maxLength ?? 500} />;
  }
};

/**
 * The tenant's custom fields of a record in any form (D12): one Form.Item per field under ['customFields', code],
 * grouped as defined. Convert values with toFormValues/toApiValues; the API validates again.
 */
export const DynamicFields = ({ fields, title }: { fields: CustomFieldFormItem[]; title?: ReactNode }) => {
  const { t } = useTranslation();
  if (fields.length === 0) {
    return null;
  }

  const groups = [...new Set(fields.map((f) => f.group ?? ''))];
  return (
    <>
      {groups.map((group) => (
        <div key={group}>
          <Divider titlePlacement="start">{group || title || t('web.custom_fields.title')}</Divider>
          {fields
            .filter((f) => (f.group ?? '') === group)
            .map((field) => (
              <Form.Item
                key={field.code}
                name={['customFields', field.code]}
                label={field.label}
                valuePropName={field.type === 'Bool' ? 'checked' : 'value'}
                rules={
                  field.required && field.type !== 'Bool'
                    ? [{ required: true, message: t('web.platform.fields.required') }]
                    : undefined
                }
              >
                {editor(field, t('web.custom_fields.id_placeholder'))}
              </Form.Item>
            ))}
        </div>
      ))}
    </>
  );
};
