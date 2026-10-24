import { EditOutlined, PlusOutlined, WarningOutlined } from '@ant-design/icons';
import { Button, Flex, Segmented, Space, Table, Tag, Tooltip } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import { useListLeaveTypes } from '@/shared/api/generated/endpoints/hr/hr';
import type { LeaveTypeItem } from '@/shared/api/generated/model';
import { StatusTag } from '@/shared/components/StatusTag';
import { useCompanyName } from '../leaveForms';
import { versionLabel } from '../leaveVersions';
import { LeaveTypeDrawer } from './LeaveTypeDrawer';

type Drawer = { open: false } | { open: true; type?: LeaveTypeItem };

/** The leave types of the tenant (docs/domain/izin-kurallari.md §3, D41). */
export const LeaveTypesTab = ({ canManage }: { canManage: boolean }) => {
  const { t, i18n } = useTranslation();
  const [showInactive, setShowInactive] = useState(false);
  const [drawer, setDrawer] = useState<Drawer>({ open: false });
  const types = useListLeaveTypes({ includeInactive: showInactive });
  const companyName = useCompanyName();

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
            {t('web.hr.leave.types.new')}
          </Button>
        )}
      </Flex>
      <Table<LeaveTypeItem>
        rowKey="id"
        size="middle"
        loading={types.isLoading}
        dataSource={types.data}
        pagination={false}
        scroll={{ x: true }}
        columns={[
          { key: 'code', dataIndex: 'code', title: t('web.platform.fields.code'), width: 180 },
          {
            key: 'name',
            title: t('web.hr.leave.fields.name'),
            render: (_, type) => (
              <Space>
                {type.name}
                {type.warnings.length > 0 && (
                  <Tooltip title={type.warnings.map((w) => w.message).join(' ')}>
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
            key: 'category',
            title: t('web.hr.leave.fields.category'),
            render: (_, type) => t(`web.hr.leave.category.${type.category}`),
          },
          {
            key: 'payer',
            title: t('web.hr.leave.fields.payer'),
            render: (_, type) => t(`web.hr.leave.payer.${type.payer}`),
          },
          {
            key: 'basis',
            title: t('web.hr.leave.fields.duration_basis'),
            render: (_, type) => t(`web.hr.leave.duration_basis.${type.durationBasis}`),
          },
          {
            key: 'versions',
            title: t('web.hr.leave.versions.title'),
            render: (_, type) =>
              type.versions.length > 1 ? (
                <Space size={4} wrap>
                  {type.versions.map((v) => (
                    <Tag key={v.validFrom}>{versionLabel(v.validFrom, t, i18n.language)}</Tag>
                  ))}
                </Space>
              ) : null,
          },
          { key: 'company', title: t('web.hr.leave.fields.company'), render: (_, type) => companyName(type.companyId) },
          {
            key: 'status',
            title: t('web.platform.fields.status'),
            width: 100,
            render: (_, type) => <StatusTag status={type.isActive ? 'active' : 'inactive'} />,
          },
          {
            key: 'actions',
            width: 96,
            render: (_, type) => (
              <Space size={4}>
                {canManage && (
                  <Button
                    size="small"
                    type="text"
                    icon={<EditOutlined />}
                    onClick={() => setDrawer({ open: true, type })}
                    aria-label={t('web.common.edit')}
                  />
                )}
                <AuditHistoryButton module="hr" entityType="LeaveType" entityId={type.id} title={type.name} />
              </Space>
            ),
          },
        ]}
      />
      {drawer.open && (
        <LeaveTypeDrawer
          type={drawer.type}
          codes={(types.data ?? []).map((x) => x.code)}
          onClose={() => setDrawer({ open: false })}
        />
      )}
    </Flex>
  );
};
