import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Card, Flex, Form, Input, Switch, Table } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { useMe } from '@/app/me/meContext';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import {
  getListEmployeeDocumentTypesQueryKey,
  useCreateEmployeeDocumentType,
  useListEmployeeDocumentTypes,
  useUpdateEmployeeDocumentType,
} from '@/shared/api/generated/endpoints/hr/hr';
import type { EmployeeDocumentTypeItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusTag } from '@/shared/components/StatusTag';
import { hrPermissions } from '../permissions';

interface DocumentTypeFormValues {
  code?: string;
  name?: Record<string, string | undefined>;
  isActive?: boolean;
}

type Drawer = { open: false } | { open: true; type?: EmployeeDocumentTypeItem };

/**
 * Kinds of employee documents (D38). The common ones are seeded; the tenant adds its own and retires those it no
 * longer uses. They are shared by every company, so changing them needs the Tenant scope.
 */
export const DocumentTypesPage = () => {
  const { t } = useTranslation();
  const canManage = useHasPermission(hrPermissions.documentsManage);
  const types = useListEmployeeDocumentTypes({ includeInactive: true });
  const [drawer, setDrawer] = useState<Drawer>({ open: false });

  return (
    <>
      <PageHeader
        title={t('web.hr.document_types.title')}
        subtitle={t('web.hr.document_types.subtitle')}
        extra={
          canManage && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
              {t('web.hr.document_types.new')}
            </Button>
          )
        }
      />
      <Card>
        <Table<EmployeeDocumentTypeItem>
          rowKey="id"
          loading={types.isLoading}
          dataSource={types.data ?? []}
          pagination={false}
          columns={[
            { key: 'code', dataIndex: 'code', title: t('web.platform.fields.code'), width: 200 },
            { key: 'name', dataIndex: 'name', title: t('web.hr.document_types.name') },
            {
              key: 'isActive',
              title: t('web.platform.fields.status'),
              width: 120,
              render: (_, row) => <StatusTag status={row.isActive ? 'active' : 'inactive'} />,
            },
            {
              key: 'actions',
              width: 96,
              render: (_, row) => (
                <Flex gap={4}>
                  {canManage && (
                    <Button
                      type="text"
                      icon={<EditOutlined />}
                      aria-label={t('web.common.edit')}
                      onClick={() => setDrawer({ open: true, type: row })}
                    />
                  )}
                  <AuditHistoryButton
                    compact
                    module="hr"
                    entityType="DocumentType"
                    entityId={row.id}
                    title={row.name}
                  />
                </Flex>
              ),
            },
          ]}
        />
      </Card>
      {drawer.open && <DocumentTypeDrawer type={drawer.type} onClose={() => setDrawer({ open: false })} />}
    </>
  );
};

const DocumentTypeDrawer = ({ type, onClose }: { type?: EmployeeDocumentTypeItem; onClose: () => void }) => {
  const { t } = useTranslation();
  const me = useMe();
  const queryClient = useQueryClient();
  const create = useCreateEmployeeDocumentType();
  const update = useUpdateEmployeeDocumentType();
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: DocumentTypeFormValues) => {
    // Blank cultures are left out; the server needs the default one
    const name = Object.fromEntries(
      Object.entries(values.name ?? {})
        .map(([culture, value]) => [culture, value?.trim() ?? ''] as const)
        .filter(([, value]) => value.length > 0),
    );
    if (type) {
      await update.mutateAsync({
        id: type.id,
        data: { version: type.version, name, isActive: values.isActive ?? true },
      });
    } else {
      await create.mutateAsync({ data: { code: values.code?.trim().toUpperCase() ?? null, name } });
    }

    await queryClient.invalidateQueries({ queryKey: getListEmployeeDocumentTypesQueryKey() });
  };

  return (
    <FormDrawer<DocumentTypeFormValues>
      title={t(type ? 'web.hr.document_types.edit' : 'web.hr.document_types.new')}
      open
      onClose={onClose}
      onSubmit={submit}
      fieldAliases={{ name: ['name', DEFAULT_CULTURE] }}
      initialValues={type ? { code: type.code, name: { ...type.names }, isActive: type.isActive } : { isActive: true }}
    >
      <Form.Item
        name="code"
        label={t('web.platform.fields.code')}
        tooltip={t('web.platform.fields.code_hint')}
        rules={required}
      >
        <Input maxLength={30} disabled={!!type} style={{ textTransform: 'uppercase' }} />
      </Form.Item>
      {me.cultures.map((culture) => (
        <Form.Item
          key={culture.code}
          name={['name', culture.code]}
          label={
            me.cultures.length > 1
              ? t('web.hr.document_types.name_in', { language: culture.name })
              : t('web.hr.document_types.name')
          }
          rules={culture.code === DEFAULT_CULTURE ? required : undefined}
        >
          <Input maxLength={100} />
        </Form.Item>
      ))}
      {type && (
        <Form.Item
          name="isActive"
          label={t('web.platform.fields.status')}
          extra={t('web.hr.document_types.inactive_hint')}
          valuePropName="checked"
        >
          <Switch />
        </Form.Item>
      )}
    </FormDrawer>
  );
};
