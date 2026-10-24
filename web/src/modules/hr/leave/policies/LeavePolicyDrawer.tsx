import { DeleteOutlined, LockOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Col, DatePicker, Flex, Form, Input, Row, Switch, Tabs, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  checkLeavePolicyRules,
  getListLeavePoliciesQueryKey,
  useCreateLeavePolicy,
  useUpdateLeavePolicy,
} from '@/shared/api/generated/endpoints/hr/hr';
import type {
  AnnualLeaveRules,
  LeaveLegalWarning,
  LeavePolicyItem,
  LeavePolicyVersion,
} from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { CompanyField } from '../CompanyField';
import { isBeginning, isStarted, toValidFrom, tomorrow, versionLabel } from '../leaveVersions';
import { LegalWarnings } from '../LegalWarnings';
import { ALL_COMPANIES, nameAliases, toNames, toRules } from '../leaveForms';
import { NameFields } from '../NameFields';
import { AnnualRulesFields } from './AnnualRulesFields';

interface VersionValues {
  validFrom?: Dayjs | string | null;
  rules?: AnnualLeaveRules;
}

interface LeavePolicyFormValues {
  code?: string;
  name?: Record<string, string>;
  companyId?: string;
  isDefault?: boolean;
  isActive?: boolean;
  versions?: VersionValues[];
}

const CHECK_DELAY_MS = 600;

interface LeavePolicyDrawerProps {
  policy?: LeavePolicyItem;
  /** Rules a new policy starts from: the default policy's latest. */
  template?: AnnualLeaveRules;
  onClose: () => void;
}

/**
 * A leave policy and its versions (D41). Each version holds the annual leave rules; versions that have started are
 * locked and a new one starts tomorrow at the earliest. The open version's settings are checked against the law while
 * they are edited.
 */
export const LeavePolicyDrawer = ({ policy, template, onClose }: LeavePolicyDrawerProps) => {
  const { t, i18n } = useTranslation();
  const [form] = Form.useForm<LeavePolicyFormValues>();
  const queryClient = useQueryClient();
  const create = useCreateLeavePolicy();
  const update = useUpdateLeavePolicy();
  const required = [{ required: true, message: t('web.platform.fields.required') }];
  // The lists have no Form.Item of their own, so they are watched with preserve
  const versions = Form.useWatch<VersionValues[] | undefined>('versions', { form, preserve: true }) ?? [];
  const companyId = Form.useWatch('companyId', form);
  const [active, setActive] = useState(0);
  const [warnings, setWarnings] = useState<LeaveLegalWarning[]>([]);
  const saved = new Set((policy?.versions ?? []).filter((v) => isStarted(v.validFrom)).map((v) => v.validFrom));
  const isLocked = (version?: VersionValues) => typeof version?.validFrom === 'string' && saved.has(version.validFrom);

  // The open version against the law, a moment after the last change; incomplete rules are skipped
  const watched = Form.useWatch(['versions', active, 'rules'], { form, preserve: true }) as
    AnnualLeaveRules | undefined;
  useEffect(() => {
    if (!watched) {
      return;
    }

    const timer = setTimeout(() => {
      checkLeavePolicyRules({ rules: toRules(watched) })
        .then(setWarnings)
        .catch(() => setWarnings([]));
    }, CHECK_DELAY_MS);
    return () => clearTimeout(timer);
  }, [watched]);

  const addVersion = () => {
    const current = form.getFieldValue('versions') as VersionValues[];
    const last = current[current.length - 1];
    form.setFieldValue('versions', [...current, { validFrom: tomorrow(), rules: structuredClone(last?.rules) }]);
    setActive(current.length);
  };

  const removeVersion = (index: number) => {
    const current = form.getFieldValue('versions') as VersionValues[];
    form.setFieldValue(
      'versions',
      current.filter((_, i) => i !== index),
    );
    setActive(Math.max(0, index - 1));
  };

  const submit = async (values: LeavePolicyFormValues) => {
    const all = (form.getFieldValue('versions') as VersionValues[]).map((v): LeavePolicyVersion => ({
      validFrom: toValidFrom(v.validFrom),
      rules: toRules(v.rules!),
    }));
    if (policy) {
      await update.mutateAsync({
        id: policy.id,
        data: {
          version: policy.version,
          name: toNames(values.name),
          isDefault: !!values.isDefault,
          isActive: !!values.isActive,
          versions: all,
        },
      });
    } else {
      await create.mutateAsync({
        data: {
          code: values.code?.trim().toUpperCase() ?? null,
          name: toNames(values.name),
          companyId: values.companyId || null,
          isDefault: !!values.isDefault,
          versions: all,
        },
      });
    }

    await queryClient.invalidateQueries({ queryKey: getListLeavePoliciesQueryKey() });
  };

  return (
    <FormDrawer<LeavePolicyFormValues>
      title={t(policy ? 'web.hr.leave.policies.edit' : 'web.hr.leave.policies.new')}
      open
      onClose={onClose}
      onSubmit={submit}
      form={form}
      width={960}
      fieldAliases={nameAliases}
      initialValues={
        policy
          ? {
              code: policy.code,
              name: { ...policy.names },
              companyId: policy.companyId ?? ALL_COMPANIES,
              isDefault: policy.isDefault,
              isActive: policy.isActive,
              versions: policy.versions,
            }
          : { companyId: ALL_COMPANIES, versions: [{ validFrom: null, rules: template }] }
      }
    >
      <Row gutter={12}>
        <Col span={8}>
          <Form.Item
            name="code"
            label={t('web.platform.fields.code')}
            tooltip={t('web.platform.fields.code_hint')}
            rules={required}
          >
            <Input maxLength={30} disabled={!!policy} style={{ textTransform: 'uppercase' }} />
          </Form.Item>
        </Col>
        <Col span={16}>
          <CompanyField disabled={!!policy} />
        </Col>
      </Row>
      <NameFields />
      <Flex gap={24} wrap>
        <Form.Item
          name="isDefault"
          label={t('web.hr.leave.policies.is_default')}
          extra={t('web.hr.leave.policies.is_default_hint')}
          valuePropName="checked"
        >
          <Switch />
        </Form.Item>
        {policy && (
          <Form.Item name="isActive" label={t('web.platform.fields.status')} valuePropName="checked">
            <Switch />
          </Form.Item>
        )}
      </Flex>
      <Typography.Title level={5}>{t('web.hr.leave.versions.title')}</Typography.Title>
      <Typography.Paragraph type="secondary">{t('web.hr.leave.versions.hint')}</Typography.Paragraph>
      <LegalWarnings warnings={warnings} />
      <Tabs
        activeKey={String(active)}
        onChange={(key) => setActive(Number(key))}
        tabBarExtraContent={
          <Button size="small" icon={<PlusOutlined />} onClick={addVersion}>
            {t('web.hr.leave.versions.add')}
          </Button>
        }
        items={versions.map((version, index) => {
          const locked = isLocked(version);
          const label =
            typeof version.validFrom === 'string'
              ? versionLabel(version.validFrom, t, i18n.language)
              : version.validFrom
                ? versionLabel(version.validFrom.format('YYYY-MM-DD'), t, i18n.language)
                : t('web.hr.leave.versions.from_beginning');
          return {
            key: String(index),
            forceRender: true,
            label: (
              <span>
                {locked && <LockOutlined />} {label}
              </span>
            ),
            children: (
              <Flex vertical gap={12}>
                {locked ? (
                  <Typography.Text type="secondary">{t('web.hr.leave.versions.locked')}</Typography.Text>
                ) : (
                  <Flex gap={8} align="center" wrap>
                    <Form.Item
                      name={['versions', index, 'validFrom']}
                      label={t('web.hr.leave.versions.valid_from')}
                      style={{ marginBottom: 0 }}
                      getValueProps={(value: Dayjs | string | null | undefined) => ({
                        value: typeof value === 'string' ? (isBeginning(value) ? null : dayjs(value)) : value,
                      })}
                      rules={index === 0 && !policy ? undefined : required}
                    >
                      <DatePicker
                        format="L"
                        allowClear={index === 0 && !policy}
                        placeholder={t('web.hr.leave.versions.from_beginning')}
                        disabledDate={index === 0 && !policy ? undefined : (day) => day.isBefore(tomorrow(), 'day')}
                      />
                    </Form.Item>
                    {versions.length > 1 && (
                      <Button danger icon={<DeleteOutlined />} onClick={() => removeVersion(index)}>
                        {t('web.hr.leave.versions.remove')}
                      </Button>
                    )}
                  </Flex>
                )}
                <AnnualRulesFields
                  name={['versions', index, 'rules']}
                  locked={locked}
                  companyId={companyId || undefined}
                />
              </Flex>
            ),
          };
        })}
      />
    </FormDrawer>
  );
};
