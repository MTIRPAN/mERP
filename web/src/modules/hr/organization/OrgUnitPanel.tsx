import { DragOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import { Button, Card, Descriptions, Empty, Flex, Skeleton, Space, Table, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import { useGetOrgUnit, useListPositions } from '@/shared/api/generated/endpoints/hr/hr';
import type { OrgUnitTreeNode, PositionListItem } from '@/shared/api/generated/model';
import { StatusTag } from '@/shared/components/StatusTag';
import { formatDate } from '@/shared/format/date';
import { PositionStatusTag } from '../positions/PositionStatusTag';

interface OrgUnitPanelProps {
  unit: OrgUnitTreeNode;
  canManage: boolean;
  onEdit: () => void;
  onAddSubUnit: () => void;
  onMove: () => void;
  onAddPosition: () => void;
  onEditPosition: (positionId: string) => void;
}

/** Details of the selected unit with its positions and their seats (PLAN H1b). */
export const OrgUnitPanel = ({
  unit,
  canManage,
  onEdit,
  onAddSubUnit,
  onMove,
  onAddPosition,
  onEditPosition,
}: OrgUnitPanelProps) => {
  const { t, i18n } = useTranslation();
  const details = useGetOrgUnit(unit.id);
  const positions = useListPositions({ orgUnitId: unit.id, pageSize: 200, sort: 'code' });
  const date = (value: string | null) => (value ? formatDate(value, i18n.language) : '…');
  const data = details.data;

  return (
    <Flex vertical gap={16}>
      <Card
        title={
          <Space>
            <span>{unit.name}</span>
            <Typography.Text type="secondary">{unit.code}</Typography.Text>
            {!unit.isActive && <StatusTag status="inactive" />}
          </Space>
        }
        extra={
          <Space wrap>
            {canManage && (
              <>
                <Button icon={<EditOutlined />} onClick={onEdit}>
                  {t('web.common.edit')}
                </Button>
                <Button icon={<PlusOutlined />} onClick={onAddSubUnit}>
                  {t('web.hr.org.new_sub_unit')}
                </Button>
                <Button icon={<DragOutlined />} onClick={onMove}>
                  {t('web.hr.org.move')}
                </Button>
              </>
            )}
            <AuditHistoryButton compact module="hr" entityType="OrgUnit" entityId={unit.id} title={unit.name} />
          </Space>
        }
      >
        {!data ? (
          <Skeleton active />
        ) : (
          <Descriptions
            size="small"
            column={{ xs: 1, md: 2 }}
            items={[
              { key: 'type', label: t('web.hr.org.fields.type'), children: t(`web.hr.org_unit_type.${data.type}`) },
              {
                key: 'parent',
                label: t('web.hr.org.fields.parent'),
                children: data.parentName ?? t('web.hr.org.fields.parent_top'),
              },
              {
                key: 'manager',
                label: t('web.hr.org.fields.manager'),
                children: data.managerPositionId
                  ? `${data.managerPositionCode} · ${data.managerJobName}`
                  : t('web.hr.org.fields.no_manager'),
              },
              { key: 'costCenter', label: t('web.hr.org.fields.cost_center'), children: data.costCenterCode ?? '—' },
              { key: 'branch', label: t('web.hr.org.fields.branch'), children: data.branchName ?? '—' },
              {
                key: 'validity',
                label: t('web.hr.org.fields.validity'),
                children:
                  data.validFrom || data.validTo
                    ? `${date(data.validFrom)} – ${date(data.validTo)}`
                    : t('web.hr.org.fields.always'),
              },
              {
                key: 'staffing',
                label: t('web.hr.org.stats.norm'),
                children: t('web.hr.org.staffing', { norm: unit.norm, filled: unit.filled, vacant: unit.vacant }),
              },
            ]}
          />
        )}
      </Card>
      <Card
        title={t('web.hr.org.positions')}
        extra={
          canManage && (
            <Button type="primary" icon={<PlusOutlined />} onClick={onAddPosition}>
              {t('web.hr.positions.new')}
            </Button>
          )
        }
      >
        <Table<PositionListItem>
          size="small"
          rowKey="id"
          loading={positions.isFetching}
          dataSource={positions.data?.items ?? []}
          pagination={false}
          locale={{
            emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.hr.org.no_positions')} />,
          }}
          columns={[
            { key: 'code', dataIndex: 'code', title: t('web.platform.fields.code') },
            { key: 'job', dataIndex: 'jobName', title: t('web.hr.positions.fields.job') },
            {
              key: 'reportsTo',
              title: t('web.hr.positions.fields.reports_to'),
              render: (_, row) => row.reportsToPositionCode ?? '—',
            },
            { key: 'norm', dataIndex: 'norm', title: t('web.hr.positions.fields.norm'), align: 'right' },
            { key: 'filled', dataIndex: 'filled', title: t('web.hr.positions.fields.filled'), align: 'right' },
            { key: 'vacant', dataIndex: 'vacant', title: t('web.hr.positions.fields.vacant'), align: 'right' },
            {
              key: 'status',
              title: t('web.hr.positions.fields.status'),
              render: (_, row) => <PositionStatusTag status={row.status} />,
            },
            ...(canManage
              ? [
                  {
                    key: 'actions',
                    title: '',
                    width: 48,
                    render: (_: unknown, row: PositionListItem) => (
                      <Button
                        type="text"
                        icon={<EditOutlined />}
                        aria-label={t('web.common.edit')}
                        onClick={() => onEditPosition(row.id)}
                      />
                    ),
                  },
                ]
              : []),
          ]}
        />
      </Card>
    </Flex>
  );
};
