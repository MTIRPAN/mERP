import { EditOutlined, PlusOutlined, WarningOutlined } from '@ant-design/icons';
import { Button, Flex, Segmented, Space, Table, Tag, Tooltip } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import { useListLeavePolicies } from '@/shared/api/generated/endpoints/hr/hr';
import type { LeavePolicyItem } from '@/shared/api/generated/model';
import { StatusTag } from '@/shared/components/StatusTag';
import { useCompanyName } from '../leaveForms';
import { versionLabel } from '../leaveVersions';
import { LeavePolicyDrawer } from './LeavePolicyDrawer';

type Drawer = { open: false } | { open: true; policy?: LeavePolicyItem };

/** The annual leave policies of the tenant (docs/domain/izin-kurallari.md §1–2, D41). */
export const LeavePoliciesTab = ({ canManage }: { canManage: boolean }) => {
  const { t, i18n } = useTranslation();
  const [showInactive, setShowInactive] = useState(false);
  const [drawer, setDrawer] = useState<Drawer>({ open: false });
  const policies = useListLeavePolicies({ includeInactive: showInactive });
  const companyName = useCompanyName();
  const template = policies.data?.find((p) => p.isDefault && !p.companyId)?.versions.at(-1)?.rules;

  return (
    <Flex vertical gap={12}>
      <Flex justify="space-between" gap={8} wrap>
        <Segmented<boolean>
          value={showInactive}
          onChange={setShowInactive}
          options={[
            { value: false, label: t('web.platform.filters.active') },
            { value: true, label: t('web.platform.filters.all') },
          ]}
        />
        {canManage && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
            {t('web.hr.leave.policies.new')}
          </Button>
        )}
      </Flex>
      <Table<LeavePolicyItem>
        rowKey="id"
        size="middle"
        loading={policies.isLoading}
        dataSource={policies.data}
        pagination={false}
        scroll={{ x: true }}
        columns={[
          { key: 'code', dataIndex: 'code', title: t('web.platform.fields.code'), width: 160 },
          {
            key: 'name',
            title: t('web.hr.leave.fields.name'),
            render: (_, policy) => (
              <Space wrap>
                {policy.name}
                {policy.isDefault && <Tag color="blue">{t('web.hr.leave.policies.default')}</Tag>}
                {policy.warnings.length > 0 && (
                  <Tooltip title={policy.warnings.map((w) => w.message).join(' ')}>
                    <WarningOutlined
                      style={{ color: 'var(--ant-color-warning)' }}
                      aria-label={t('web.hr.leave.legal.title')}
                    />
                  </Tooltip>
                )}
              </Space>
            ),
          },
          {
            key: 'tiers',
            title: t('web.hr.leave.rules.tiers'),
            render: (_, policy) =>
              (policy.versions.at(-1)?.rules.tiers ?? [])
                .map((tier) =>
                  t('web.hr.leave.policies.tier_summary', { years: tier.minServiceYears, days: tier.days }),
                )
                .join(' · '),
          },
          {
            key: 'versions',
            title: t('web.hr.leave.versions.title'),
            render: (_, policy) => (
              <Space size={4} wrap>
                {policy.versions.map((v) => (
                  <Tag key={v.validFrom}>{versionLabel(v.validFrom, t, i18n.language)}</Tag>
                ))}
              </Space>
            ),
          },
          {
            key: 'company',
            title: t('web.hr.leave.fields.company'),
            render: (_, policy) => companyName(policy.companyId),
          },
          {
            key: 'status',
            title: t('web.platform.fields.status'),
            width: 100,
            render: (_, policy) => <StatusTag status={policy.isActive ? 'active' : 'inactive'} />,
          },
          {
            key: 'actions',
            width: 96,
            render: (_, policy) => (
              <Space size={4}>
                {canManage && (
                  <Button
                    size="small"
                    type="text"
                    icon={<EditOutlined />}
                    onClick={() => setDrawer({ open: true, policy })}
                    aria-label={t('web.common.edit')}
                  />
                )}
                <AuditHistoryButton module="hr" entityType="LeavePolicy" entityId={policy.id} title={policy.name} />
              </Space>
            ),
          },
        ]}
      />
      {drawer.open && (
        <LeavePolicyDrawer policy={drawer.policy} template={template} onClose={() => setDrawer({ open: false })} />
      )}
    </Flex>
  );
};
