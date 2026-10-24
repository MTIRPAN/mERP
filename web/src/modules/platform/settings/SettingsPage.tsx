import { UndoOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import {
  App,
  Button,
  Card,
  DatePicker,
  Empty,
  Flex,
  Input,
  InputNumber,
  Select,
  Skeleton,
  Switch,
  Tag,
  Typography,
} from 'antd';
import dayjs from 'dayjs';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  getGetSettingsQueryKey,
  useGetSettings,
  useUpdateSettings,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { SettingItem } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { settingText, type SettingValue } from './settingValues';

/** Settings of all modules for the tenant or a company (PLAN P11a, D35). */
export const SettingsPage = () => {
  const { t } = useTranslation();
  const me = useMe();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const canManage = useHasPermission(platformPermissions.settingsManage);
  const [companyId, setCompanyId] = useState<string>();
  const settings = useGetSettings({ companyId });
  const update = useUpdateSettings();
  // Changed values; null takes the value away at this level
  const [drafts, setDrafts] = useState<Record<string, SettingValue | null>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});

  const changeLevel = (level: string) => {
    setCompanyId(level || undefined);
    setDrafts({});
    setErrors({});
  };

  const save = async () => {
    try {
      await update.mutateAsync({ data: { companyId: companyId ?? null, values: drafts } });
    } catch (error) {
      if (isApiError(error) && error.problem?.fieldErrors?.length) {
        setErrors(
          Object.fromEntries(error.problem.fieldErrors.map((e) => [e.field.replace(/^values\./, ''), e.message])),
        );
        return;
      }

      throw error;
    }

    setDrafts({});
    setErrors({});
    message.success(t('web.settings.saved'));
    await queryClient.invalidateQueries({ queryKey: [getGetSettingsQueryKey()[0]] });
  };

  const changed = Object.keys(drafts).length > 0;

  return (
    <>
      <PageHeader
        title={t('web.settings.title')}
        subtitle={t('web.settings.subtitle')}
        extra={
          canManage && (
            <Button type="primary" disabled={!changed} loading={update.isPending} onClick={() => void save()}>
              {t('web.settings.save')}
            </Button>
          )
        }
      />
      <Flex vertical gap={16}>
        <Card size="small">
          <Flex gap={12} align="center" wrap>
            <Typography.Text strong>{t('web.settings.level')}</Typography.Text>
            <Select
              style={{ minWidth: 280 }}
              value={companyId ?? ''}
              onChange={changeLevel}
              aria-label={t('web.settings.level')}
              options={[
                { value: '', label: t('web.settings.tenant') },
                ...me.companies.map((company) => ({ value: company.id, label: company.name })),
              ]}
            />
            {companyId && <Typography.Text type="secondary">{t('web.settings.company_hint')}</Typography.Text>}
          </Flex>
        </Card>
        {!settings.data ? (
          <Skeleton active />
        ) : settings.data.length === 0 ? (
          <Empty description={t('web.settings.empty')} />
        ) : (
          settings.data.map((group) => (
            <Card key={group.module} title={group.name}>
              <Flex vertical gap={20}>
                {group.settings.map((setting) => (
                  <SettingRow
                    key={setting.key}
                    setting={setting}
                    company={!!companyId}
                    draft={drafts[setting.key]}
                    error={errors[setting.key]}
                    disabled={!canManage}
                    onChange={(value) => setDrafts((current) => ({ ...current, [setting.key]: value }))}
                  />
                ))}
              </Flex>
            </Card>
          ))
        )}
      </Flex>
    </>
  );
};

interface SettingRowProps {
  setting: SettingItem;
  company: boolean;
  /** undefined: unchanged; null: removed at this level. */
  draft: SettingValue | null | undefined;
  error?: string;
  disabled: boolean;
  onChange: (value: SettingValue | null) => void;
}

const SettingRow = ({ setting, company, draft, error, disabled, onChange }: SettingRowProps) => {
  const { t } = useTranslation();
  const own = draft === undefined ? (setting.value as SettingValue | null) : draft;
  const inherited = setting.inheritedValue as SettingValue;
  const shown = own ?? inherited;

  return (
    <Flex justify="space-between" align="start" gap={24} wrap>
      <div style={{ flex: '1 1 320px', minWidth: 0 }}>
        <Typography.Text strong>{setting.label}</Typography.Text>
        {setting.description && (
          <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
            {setting.description}
          </Typography.Paragraph>
        )}
        {own === null && (
          <Tag style={{ marginTop: 4 }}>
            {company ? t('web.settings.inherited_from_tenant') : t('web.settings.default')}
          </Tag>
        )}
      </div>
      <Flex vertical align="end" gap={4} style={{ flex: '0 1 320px' }}>
        <Flex gap={8} align="center">
          <SettingInput setting={setting} value={shown} disabled={disabled} label={setting.label} onChange={onChange} />
          {own !== null && !disabled && (
            <Button
              type="text"
              size="small"
              icon={<UndoOutlined />}
              aria-label={t(company ? 'web.settings.reset_company' : 'web.settings.reset')}
              title={t(company ? 'web.settings.reset_company' : 'web.settings.reset')}
              onClick={() => onChange(null)}
            />
          )}
        </Flex>
        {own === null && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t('web.settings.inherited', { value: settingText(setting, inherited, t) })}
          </Typography.Text>
        )}
        {error && <Typography.Text type="danger">{error}</Typography.Text>}
      </Flex>
    </Flex>
  );
};

const SettingInput = ({
  setting,
  value,
  disabled,
  label,
  onChange,
}: {
  setting: SettingItem;
  value: SettingValue;
  disabled: boolean;
  label: string;
  onChange: (value: SettingValue) => void;
}) => {
  const { t } = useTranslation();

  switch (setting.type) {
    case 'Boolean':
      return (
        <Switch
          checked={value === true}
          disabled={disabled}
          aria-label={label}
          checkedChildren={t('web.settings.yes')}
          unCheckedChildren={t('web.settings.no')}
          onChange={onChange}
        />
      );
    case 'Number':
      return (
        <InputNumber
          value={typeof value === 'number' ? value : null}
          min={setting.min ?? undefined}
          max={setting.max ?? undefined}
          precision={setting.wholeNumber ? 0 : undefined}
          disabled={disabled}
          aria-label={label}
          onChange={(number) => number !== null && onChange(number)}
        />
      );
    case 'Date':
      return (
        <DatePicker
          value={typeof value === 'string' && value ? dayjs(value) : null}
          format="L"
          disabled={disabled}
          aria-label={label}
          onChange={(day) => day && onChange(day.format('YYYY-MM-DD'))}
        />
      );
    case 'Choice':
      return (
        <Select
          style={{ minWidth: 220 }}
          value={typeof value === 'string' ? value : undefined}
          disabled={disabled}
          aria-label={label}
          options={setting.options.map((option) => ({ value: option.value, label: option.label }))}
          onChange={onChange}
        />
      );
    default:
      return (
        <Input
          style={{ width: 280 }}
          value={typeof value === 'string' ? value : ''}
          maxLength={setting.maxLength ?? undefined}
          disabled={disabled}
          aria-label={label}
          onChange={(event) => onChange(event.target.value)}
        />
      );
  }
};
