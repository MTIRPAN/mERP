import { Form, Input } from 'antd';
import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { useMe } from '@/app/me/meContext';

/** The name in every language of the tenant; the default one is required. */
export const NameFields = ({ maxLength = 100 }: { maxLength?: number }) => {
  const { t } = useTranslation();
  const me = useMe();
  return me.cultures.map((culture) => (
    <Form.Item
      key={culture.code}
      name={['name', culture.code]}
      label={
        me.cultures.length > 1
          ? t('web.hr.leave.fields.name_in', { language: culture.name })
          : t('web.hr.leave.fields.name')
      }
      rules={
        culture.code === DEFAULT_CULTURE ? [{ required: true, message: t('web.platform.fields.required') }] : undefined
      }
    >
      <Input maxLength={maxLength} />
    </Form.Item>
  ));
};

/** The names as the API takes them: blank languages left out. */
export const toNames = (name: Record<string, string | undefined> | undefined) =>
  Object.fromEntries(
    Object.entries(name ?? {})
      .map(([culture, value]) => [culture, value?.trim() ?? ''] as const)
      .filter(([, value]) => value.length > 0),
  );

/** API errors of the name land on the default language. */
export const nameAliases = { name: ['name', DEFAULT_CULTURE] };
