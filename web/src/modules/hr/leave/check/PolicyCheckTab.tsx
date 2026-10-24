import { useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App,
  Button,
  Card,
  DatePicker,
  Descriptions,
  Empty,
  Flex,
  Form,
  Select,
  Space,
  Statistic,
  Tag,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useDeferredValue, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getGetEmployeeQueryKey,
  getResolveLeavePolicyQueryKey,
  useGetEmployee,
  useListEmployees,
  useListLeavePolicies,
  useResolveLeavePolicy,
  useSetEmploymentLeavePolicy,
} from '@/shared/api/generated/endpoints/hr/hr';
import { LeavePolicySource, type EmployeeListItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { hrPermissions } from '../../permissions';
import { versionLabel } from '../leaveVersions';

const SEARCH_SIZE = 20;

/**
 * "Which policy applies to this employee?" (PLAN L1, D41): an employment's policy on a day, why, its version and the
 * annual leave it gives. Those who manage leave settings may give the employment a policy by hand here.
 */
export const PolicyCheckTab = ({ canManage }: { canManage: boolean }) => {
  const { t, i18n } = useTranslation();
  const canReadEmployees = useHasPermission(hrPermissions.employeesRead);
  const [search, setSearch] = useState('');
  const deferred = useDeferredValue(search);
  const [picked, setPicked] = useState<EmployeeListItem>();
  const [day, setDay] = useState<Dayjs>(dayjs());
  const employees = useListEmployees(
    { search: deferred || undefined, pageSize: SEARCH_SIZE },
    { query: { enabled: canReadEmployees } },
  );
  const params = { employmentId: picked?.employmentId ?? '', date: day.format('YYYY-MM-DD') };
  const resolution = useResolveLeavePolicy(params, { query: { enabled: !!picked } });

  if (!canReadEmployees) {
    return <Alert type="info" showIcon title={t('web.hr.leave.check.needs_employees')} />;
  }

  const result = resolution.data;
  return (
    <Flex vertical gap={16}>
      <Flex gap={12} wrap>
        <Select
          aria-label={t('web.hr.leave.check.employee')}
          showSearch={{ filterOption: false, onSearch: setSearch }}
          placeholder={t('web.hr.leave.check.employee_placeholder')}
          loading={employees.isFetching}
          value={picked?.employmentId}
          onChange={(employmentId: string) =>
            setPicked(employees.data?.items.find((e) => e.employmentId === employmentId))
          }
          options={(employees.data?.items ?? []).map((e) => ({
            value: e.employmentId,
            label: `${e.fullName} · ${e.companyName} · ${e.registryNumber}`,
          }))}
          style={{ minWidth: 360, flex: 1 }}
        />
        <DatePicker
          aria-label={t('web.hr.leave.check.date')}
          value={day}
          onChange={(value) => value && setDay(value)}
          format="L"
          allowClear={false}
        />
      </Flex>
      {!picked ? (
        <Empty description={t('web.hr.leave.check.pick')} />
      ) : !result ? (
        <Card loading />
      ) : (
        <Card>
          <Flex gap={32} wrap align="flex-start">
            <Statistic
              title={t('web.hr.leave.check.entitlement')}
              value={result.entitlementDays}
              suffix={t('web.hr.leave.check.days')}
            />
            <Descriptions
              column={1}
              size="small"
              style={{ flex: 1, minWidth: 280 }}
              items={[
                {
                  key: 'policy',
                  label: t('web.hr.leave.check.policy'),
                  children: result.policyName
                    ? `${result.policyName} (${result.policyCode})`
                    : t('web.hr.leave.check.no_policy'),
                },
                {
                  key: 'source',
                  label: t('web.hr.leave.check.source'),
                  children: (
                    <Space wrap>
                      <Tag color={result.source === LeavePolicySource.Override ? 'orange' : 'blue'}>
                        {t(`web.hr.leave.source.${result.source}`)}
                      </Tag>
                      {result.ruleName &&
                        t('web.hr.leave.check.rule', { position: result.rulePosition, name: result.ruleName })}
                    </Space>
                  ),
                },
                {
                  key: 'version',
                  label: t('web.hr.leave.check.version'),
                  children: versionLabel(result.versionValidFrom ?? '0001-01-01', t, i18n.language),
                },
                { key: 'years', label: t('web.hr.leave.check.completed_years'), children: result.completedYears },
                { key: 'age', label: t('web.hr.leave.check.age'), children: result.age ?? '—' },
              ]}
            />
          </Flex>
          {canManage && (
            <OverrideForm
              key={`${picked.employmentId}-${result.policyId ?? ''}-${result.source}`}
              employeeId={picked.id}
              employmentId={picked.employmentId}
              companyId={picked.companyId}
              current={result.source === LeavePolicySource.Override ? (result.policyId ?? undefined) : undefined}
              onSaved={() => resolution.refetch()}
            />
          )}
        </Card>
      )}
    </Flex>
  );
};

interface OverrideFormProps {
  employeeId: string;
  employmentId: string;
  companyId: string;
  /** The policy set by hand, if any. */
  current?: string;
  onSaved: () => void;
}

/** Gives the employment a policy by hand, or lets the company's rules decide again (D41). */
const OverrideForm = ({ employeeId, employmentId, companyId, current, onSaved }: OverrideFormProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const employee = useGetEmployee(employeeId);
  const policies = useListLeavePolicies({ companyId });
  const set = useSetEmploymentLeavePolicy();
  const [policyId, setPolicyId] = useState<string | undefined>(current);

  const save = async (value: string | null) => {
    if (!employee.data) {
      return;
    }

    await set.mutateAsync({ employeeId, employmentId, data: { version: employee.data.version, policyId: value } });
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: getGetEmployeeQueryKey(employeeId) }),
      queryClient.invalidateQueries({ queryKey: getResolveLeavePolicyQueryKey() }),
    ]);
    message.success(t(value ? 'web.hr.leave.check.override_saved' : 'web.hr.leave.check.override_cleared'));
    onSaved();
  };

  return (
    <Form layout="vertical" style={{ marginTop: 16 }}>
      <Form.Item
        label={t('web.hr.leave.check.override')}
        extra={t('web.hr.leave.check.override_hint')}
        style={{ marginBottom: 0 }}
      >
        <Space wrap>
          <Select
            aria-label={t('web.hr.leave.check.override')}
            allowClear
            value={policyId}
            onChange={setPolicyId}
            options={(policies.data ?? []).map((p) => ({ value: p.id, label: `${p.name} (${p.code})` }))}
            style={{ minWidth: 280 }}
          />
          <Button
            type="primary"
            disabled={!policyId || policyId === current}
            loading={set.isPending}
            onClick={() => void save(policyId ?? null)}
          >
            {t('web.hr.leave.check.override_save')}
          </Button>
          {current && (
            <Button loading={set.isPending} onClick={() => void save(null)}>
              {t('web.hr.leave.check.override_clear')}
            </Button>
          )}
        </Space>
      </Form.Item>
    </Form>
  );
};
