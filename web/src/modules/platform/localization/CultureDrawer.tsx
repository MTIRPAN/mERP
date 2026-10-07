import { useQueryClient } from '@tanstack/react-query';
import { AutoComplete, Form, Input, Select, Switch } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  getGetMeQueryKey,
  getListCulturesQueryKey,
  useAddCulture,
  useUpdateCulture,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { CultureItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { COMMON_CULTURES, cultureLabel } from './cultures';

interface CultureValues {
  code?: string;
  name?: string;
  isActive?: boolean;
  fallbackCode?: string;
}

const blank = (value?: string) => (value?.trim() ? value.trim() : null);

interface CultureDrawerProps {
  /** Culture to edit; undefined adds a new one. */
  culture?: CultureItem;
  cultures: CultureItem[];
  open: boolean;
  onClose: () => void;
}

/** Adds a language, or renames one, switches it on or off and sets its fallback. The code is fixed. */
export const CultureDrawer = ({ culture, cultures, open, onClose }: CultureDrawerProps) => {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const add = useAddCulture();
  const update = useUpdateCulture();
  const existing = new Set(cultures.map((c) => c.code));

  const submit = async (values: CultureValues) => {
    if (culture) {
      await update.mutateAsync({
        code: culture.code,
        data: {
          version: culture.version,
          name: blank(values.name),
          isActive: culture.isDefault || (values.isActive ?? true),
          fallbackCode: values.fallbackCode ?? null,
        },
      });
    } else {
      await add.mutateAsync({
        data: { code: blank(values.code), name: blank(values.name), fallbackCode: values.fallbackCode ?? null },
      });
    }

    await queryClient.invalidateQueries({ queryKey: getListCulturesQueryKey() });
    // The language selector lists the active cultures
    await queryClient.invalidateQueries({ queryKey: getGetMeQueryKey() });
  };

  const fallbacks = cultures
    .filter((c) => c.code !== culture?.code)
    .map((c) => ({ value: c.code, label: `${c.name} (${c.code})` }));

  return (
    <FormDrawer<CultureValues>
      key={culture ? `${culture.code}-${culture.version}` : 'new'}
      title={t(culture ? 'web.localization.cultures.edit' : 'web.localization.cultures.new')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      initialValues={
        culture
          ? { name: culture.name, isActive: culture.isActive, fallbackCode: culture.fallbackCode ?? undefined }
          : {}
      }
    >
      <Form.Item
        name="code"
        label={t('web.localization.cultures.code')}
        tooltip={t('web.localization.cultures.code_hint')}
        rules={culture ? undefined : [{ required: true, message: t('web.platform.fields.required') }]}
        initialValue={culture?.code}
      >
        <AutoComplete
          disabled={!!culture}
          options={COMMON_CULTURES.filter((code) => !existing.has(code)).map((code) => ({
            value: code,
            label: `${cultureLabel(code, i18n.language)} (${code})`,
          }))}
          filterOption={(input, option) =>
            `${option?.label ?? ''}`.toLocaleLowerCase(i18n.language).includes(input.toLocaleLowerCase(i18n.language))
          }
        />
      </Form.Item>
      <Form.Item
        name="name"
        label={t('web.localization.cultures.name')}
        tooltip={t('web.localization.cultures.name_hint')}
      >
        <Input maxLength={100} />
      </Form.Item>
      {!culture?.isDefault && (
        <Form.Item
          name="fallbackCode"
          label={t('web.localization.cultures.fallback')}
          tooltip={t('web.localization.cultures.fallback_hint')}
        >
          <Select allowClear options={fallbacks} />
        </Form.Item>
      )}
      {culture && !culture.isDefault && (
        <Form.Item name="isActive" label={t('web.platform.fields.active')} valuePropName="checked">
          <Switch />
        </Form.Item>
      )}
    </FormDrawer>
  );
};
