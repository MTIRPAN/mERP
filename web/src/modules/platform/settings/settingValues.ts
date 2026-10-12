import type { TFunction } from 'i18next';
import type { SettingItem } from '@/shared/api/generated/model';

/** A setting's value as the API sends it (JSON). */
export type SettingValue = boolean | number | string;

/** A value as text, e.g. "Açık" for true or the label of a choice. */
export const settingText = (
  setting: Pick<SettingItem, 'type' | 'options'>,
  value: SettingValue,
  t: TFunction,
): string => {
  switch (setting.type) {
    case 'Boolean':
      return t(value === true ? 'web.settings.yes' : 'web.settings.no');
    case 'Choice':
      return setting.options.find((option) => option.value === value)?.label ?? String(value);
    default:
      return String(value);
  }
};
