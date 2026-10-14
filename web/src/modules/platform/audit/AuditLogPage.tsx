import { DatePicker, Input, Select, Space, Tag, Typography } from 'antd';
import type { Dayjs } from 'dayjs';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  getSearchAuditQueryKey,
  searchAudit,
  useListAuditEntityTypes,
  useListUsers,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { AuditItem } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDateTime } from '@/shared/format/date';
import { actionColors, displayValue, exportSummary, type AuditAction } from './auditView';

/** Entity filter value: "module:EntityType". */
const SEPARATOR = ':';

/** Users for the filter; needs platform.users.read, without it the filter is hidden. */
const UserFilter = ({ value, onChange }: { value?: string; onChange: (value?: string) => void }) => {
  const { t } = useTranslation();
  const [search, setSearch] = useState<string>();
  const users = useListUsers({ search, pageSize: 20 });
  return (
    <Select
      allowClear
      showSearch={{ filterOption: false, onSearch: (text) => setSearch(text.trim() || undefined) }}
      placeholder={t('web.audit.user')}
      aria-label={t('web.audit.user')}
      style={{ minWidth: 220 }}
      value={value}
      onChange={onChange}
      loading={users.isFetching}
      options={users.data?.items.map((user) => ({ value: user.id, label: `${user.displayName} (${user.userName})` }))}
    />
  );
};

/** Search in the audit trail of all modules (PLAN P3); tenant administrators only (D27). */
export const AuditLogPage = () => {
  const { t, i18n } = useTranslation();
  const canListUsers = useHasPermission(platformPermissions.usersRead);
  const entityTypes = useListAuditEntityTypes();
  const [entity, setEntity] = useState<string>();
  const [entityId, setEntityId] = useState<string>();
  const [userId, setUserId] = useState<string>();
  const [period, setPeriod] = useState<[Dayjs | null, Dayjs | null] | null>(null);

  const columns = useMemo<DataTableColumn<AuditItem>[]>(
    () => [
      {
        key: 'occurredAt',
        title: t('web.audit.occurred_at'),
        render: (_, row) => formatDateTime(row.occurredAt, i18n.language),
      },
      {
        key: 'user',
        title: t('web.audit.user'),
        render: (_, row) =>
          row.userName ?? <Typography.Text type="secondary">{t('web.audit.system_user')}</Typography.Text>,
      },
      {
        key: 'entity',
        title: t('web.audit.entity'),
        render: (_, row) => (
          <Space size={4} wrap>
            {row.entityLabel}
            <Typography.Text type="secondary" copyable={{ text: row.entityId }} style={{ fontSize: 12 }}>
              {row.entityId.length > 13 ? `${row.entityId.slice(0, 8)}…` : row.entityId}
            </Typography.Text>
          </Space>
        ),
      },
      {
        key: 'action',
        title: t('web.audit.action_column'),
        render: (_, row) => (
          <Tag color={actionColors[row.action as AuditAction]} variant="filled">
            {t(`web.audit.action.${row.action}`)}
          </Tag>
        ),
      },
      { key: 'field', title: t('web.audit.field'), render: (_, row) => row.fieldLabel ?? row.field ?? '—' },
      {
        key: 'oldValue',
        title: t('web.audit.old_value'),
        ellipsis: true,
        render: (_, row) => displayValue(row.oldValue, t),
      },
      {
        key: 'newValue',
        title: t('web.audit.new_value'),
        ellipsis: true,
        render: (_, row) =>
          (row.action === 'Export' ? exportSummary(row.newValue, t) : undefined) ?? displayValue(row.newValue, t),
      },
      { key: 'module', title: t('web.audit.module'), hiddenByDefault: true, render: (_, row) => row.module },
    ],
    [t, i18n.language],
  );

  const fetchPage = (params: ListParams) => {
    const [module, entityType] = params.filter?.entity?.split(SEPARATOR) ?? [];
    return searchAudit({
      page: params.page,
      pageSize: params.pageSize,
      module,
      entityType,
      entityId: params.filter?.entityId,
      userId: params.filter?.userId,
      from: params.filter?.from,
      to: params.filter?.to,
    });
  };

  // Whole days in the browser's time zone
  const from = period?.[0]?.startOf('day').toISOString();
  const to = period?.[1]?.endOf('day').toISOString();

  return (
    <>
      <PageHeader title={t('web.audit.title')} subtitle={t('web.audit.subtitle')} />
      <DataTable<AuditItem>
        queryKey={getSearchAuditQueryKey()}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{ entity, entityId, userId, from, to }}
        toolbar={
          <Space wrap>
            <Select
              allowClear
              showSearch={{ optionFilterProp: 'label' }}
              placeholder={t('web.audit.entity')}
              aria-label={t('web.audit.entity')}
              style={{ minWidth: 200 }}
              value={entity}
              onChange={(value?: string) => setEntity(value)}
              loading={entityTypes.isFetching}
              options={entityTypes.data?.map((type) => ({
                value: `${type.module}${SEPARATOR}${type.entityType}`,
                label: type.label,
              }))}
            />
            <Input.Search
              allowClear
              placeholder={t('web.audit.entity_id')}
              aria-label={t('web.audit.entity_id')}
              onSearch={(value) => setEntityId(value.trim() || undefined)}
              style={{ width: 280 }}
            />
            {canListUsers && <UserFilter value={userId} onChange={setUserId} />}
            <DatePicker.RangePicker
              allowEmpty={[true, true]}
              format="L"
              value={period}
              onChange={(value) => setPeriod(value)}
              aria-label={t('web.audit.period')}
            />
          </Space>
        }
      />
    </>
  );
};
