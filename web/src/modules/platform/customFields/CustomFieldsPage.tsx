import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { Button, Card, Select, Skeleton, Table, Typography, type TableColumnsType } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  useListCustomFieldDefinitions,
  useListCustomFieldEntityTypes,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { CustomFieldDefinitionItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusTag } from '@/shared/components/StatusTag';
import { CustomFieldDrawer } from './CustomFieldDrawer';

/** Custom field definitions per entity type (PLAN P6, D12). */
export const CustomFieldsPage = () => {
  const { t, i18n } = useTranslation();
  const canManage = useHasPermission(platformPermissions.customFieldsManage);
  const entityTypes = useListCustomFieldEntityTypes();
  const [selected, setSelected] = useState<string>();
  const entityType = selected ?? entityTypes.data?.[0]?.code;
  const definitions = useListCustomFieldDefinitions({ entityType }, { query: { enabled: !!entityType } });
  const [drawer, setDrawer] = useState<{ open: boolean; definition?: CustomFieldDefinitionItem }>({ open: false });

  if (!entityTypes.data) {
    return <Skeleton active />;
  }

  const text = (texts: Record<string, string> | null) => texts?.[i18n.language] ?? texts?.[DEFAULT_CULTURE] ?? '';

  const columns: TableColumnsType<CustomFieldDefinitionItem> = [
    { key: 'order', dataIndex: 'order', title: t('web.custom_fields.order'), width: 72 },
    { key: 'label', title: t('web.custom_fields.label'), render: (_, row) => text(row.label) },
    {
      key: 'code',
      title: t('web.custom_fields.code'),
      render: (_, row) => <Typography.Text code>{row.code}</Typography.Text>,
    },
    { key: 'type', title: t('web.custom_fields.type'), render: (_, row) => t(`web.custom_fields.types.${row.type}`) },
    {
      key: 'company',
      title: t('web.custom_fields.company'),
      render: (_, row) => row.companyName ?? t('web.custom_fields.all_companies'),
    },
    { key: 'group', title: t('web.custom_fields.group'), render: (_, row) => text(row.group) || '—' },
    {
      key: 'required',
      title: t('web.custom_fields.required'),
      render: (_, row) => t(row.required ? 'web.common.yes' : 'web.common.no'),
    },
    {
      key: 'status',
      title: t('web.platform.fields.status'),
      render: (_, row) => <StatusTag status={row.isActive ? 'active' : 'inactive'} />,
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            width: 56,
            render: (_: unknown, row: CustomFieldDefinitionItem) => (
              <Button
                type="text"
                icon={<EditOutlined />}
                aria-label={t('web.common.edit')}
                onClick={() => setDrawer({ open: true, definition: row })}
              />
            ),
          },
        ]
      : []),
  ];

  return (
    <>
      <PageHeader
        title={t('web.custom_fields.page_title')}
        subtitle={t('web.custom_fields.subtitle')}
        extra={
          canManage &&
          entityType && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
              {t('web.custom_fields.new')}
            </Button>
          )
        }
      />
      <Card
        title={
          <Select
            style={{ minWidth: 220 }}
            value={entityType}
            onChange={setSelected}
            aria-label={t('web.custom_fields.entity_type')}
            options={entityTypes.data.map((e) => ({ value: e.code, label: e.label }))}
          />
        }
      >
        <Table<CustomFieldDefinitionItem>
          rowKey="id"
          columns={columns}
          dataSource={definitions.data ?? []}
          loading={definitions.isFetching}
          pagination={false}
          locale={{ emptyText: t('web.custom_fields.empty') }}
          scroll={{ x: 'max-content' }}
        />
      </Card>
      {entityType && (
        <CustomFieldDrawer
          entityType={entityType}
          definition={drawer.definition}
          open={drawer.open}
          onClose={() => setDrawer({ open: false })}
        />
      )}
    </>
  );
};
