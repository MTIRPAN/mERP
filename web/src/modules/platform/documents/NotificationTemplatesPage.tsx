import { DeleteOutlined, EditOutlined, PlusOutlined, RollbackOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import {
  Button,
  Card,
  Col,
  Collapse,
  Flex,
  Form,
  Input,
  Popconfirm,
  Row,
  Select,
  Skeleton,
  Space,
  Table,
  Typography,
  type TableColumnsType,
} from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  getListNotificationTemplatesQueryKey,
  useCreateNotificationTemplate,
  useDeleteNotificationTemplate,
  useListNotificationTemplates,
  useListNotificationTypes,
  useUpdateNotificationTemplate,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { NotificationTemplateItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { CodeEditor } from '@/shared/components/code-editor/CodeEditor';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { PageHeader } from '@/shared/components/PageHeader';

interface TemplateValues {
  notificationType?: string | null;
  companyId?: string | null;
  culture?: string;
  subject?: string;
  body?: string;
}

/** Variables of e-mail templates (D32), shown as help next to the editor. */
const FIELDS = ['title', 'body', 'link', 'recipient.name', 'company.name', 'args.*', 'texts.open', 'texts.footer'];

/** E-mail templates of notifications (PLAN P12b, D32): the most specific one is used, else the built-in layout. */
export const NotificationTemplatesPage = () => {
  const { t } = useTranslation();
  const me = useMe();
  const queryClient = useQueryClient();
  const canManage = useHasPermission(platformPermissions.notificationTemplatesManage);
  const templates = useListNotificationTemplates();
  const types = useListNotificationTypes();
  const create = useCreateNotificationTemplate();
  const update = useUpdateNotificationTemplate();
  const remove = useDeleteNotificationTemplate();
  const [drawer, setDrawer] = useState<{ open: boolean; template?: NotificationTemplateItem }>({ open: false });
  const [form] = Form.useForm<TemplateValues>();

  if (!templates.data || !types.data) {
    return <Skeleton active />;
  }

  const list = templates.data;
  const typeLabel = (type: string | null) =>
    type === null
      ? t('web.notification_templates.all_types')
      : (types.data?.find((item) => item.type === type)?.label ?? type);
  const cultureName = (code: string) => me.cultures.find((c) => c.code === code)?.name ?? code;
  const refresh = () => queryClient.invalidateQueries({ queryKey: getListNotificationTemplatesQueryKey() });

  const submit = async (values: TemplateValues) => {
    const editing = drawer.template;
    if (editing) {
      await update.mutateAsync({
        id: editing.id,
        data: { version: editing.version, subject: values.subject ?? '', body: values.body ?? '' },
      });
    } else {
      await create.mutateAsync({
        data: {
          notificationType: values.notificationType ?? null,
          companyId: values.companyId ?? null,
          culture: values.culture ?? null,
          subject: values.subject ?? '',
          body: values.body ?? '',
        },
      });
    }

    await refresh();
  };

  const deleteTemplate = async (template: NotificationTemplateItem) => {
    await remove.mutateAsync({ id: template.id });
    await refresh();
  };

  const columns: TableColumnsType<NotificationTemplateItem> = [
    { key: 'type', title: t('web.notification_templates.type'), render: (_, row) => typeLabel(row.notificationType) },
    {
      key: 'company',
      title: t('web.notification_templates.company'),
      render: (_, row) => row.companyName ?? t('web.notification_templates.all_companies'),
    },
    { key: 'culture', title: t('web.notification_templates.culture'), render: (_, row) => cultureName(row.culture) },
    {
      key: 'subject',
      title: t('web.notification_templates.subject'),
      render: (_, row) => <Typography.Text code>{row.subject}</Typography.Text>,
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            width: 96,
            render: (_: unknown, row: NotificationTemplateItem) => (
              <Space size={0}>
                <Button
                  type="text"
                  icon={<EditOutlined />}
                  aria-label={t('web.common.edit')}
                  onClick={() => setDrawer({ open: true, template: row })}
                />
                <Popconfirm
                  title={t('web.notification_templates.delete_confirm')}
                  onConfirm={() => deleteTemplate(row)}
                >
                  <Button type="text" danger icon={<DeleteOutlined />} aria-label={t('web.common.delete')} />
                </Popconfirm>
              </Space>
            ),
          },
        ]
      : []),
  ];

  const editing = drawer.template;
  const initial: TemplateValues = editing
    ? {
        notificationType: editing.notificationType,
        companyId: editing.companyId,
        culture: editing.culture,
        subject: editing.subject,
        body: editing.body,
      }
    : {
        notificationType: null,
        companyId: null,
        culture: me.cultures[0]?.code,
        subject: list.defaultSubject,
        body: list.defaultBody,
      };

  return (
    <>
      <PageHeader
        title={t('web.notification_templates.title')}
        subtitle={t('web.notification_templates.subtitle')}
        extra={
          canManage && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
              {t('web.notification_templates.new')}
            </Button>
          )
        }
      />
      <Card>
        <Table<NotificationTemplateItem>
          rowKey="id"
          columns={columns}
          dataSource={list.items}
          loading={templates.isFetching}
          pagination={false}
          locale={{ emptyText: t('web.notification_templates.empty') }}
          scroll={{ x: 'max-content' }}
        />
      </Card>
      <FormDrawer<TemplateValues>
        key={editing ? `${editing.id}-${editing.version}` : 'new'}
        title={t(editing ? 'web.notification_templates.edit' : 'web.notification_templates.new')}
        open={drawer.open}
        onClose={() => setDrawer({ open: false })}
        onSubmit={submit}
        form={form}
        initialValues={initial}
        width={960}
      >
        <Row gutter={12}>
          <Col xs={24} md={8}>
            <Form.Item name="notificationType" label={t('web.notification_templates.type')}>
              <Select
                disabled={!!editing}
                options={[
                  { value: null, label: t('web.notification_templates.all_types') },
                  ...types.data.map((type) => ({ value: type.type, label: type.label })),
                ]}
              />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item name="companyId" label={t('web.notification_templates.company')}>
              <Select
                disabled={!!editing}
                showSearch={{ optionFilterProp: 'label' }}
                options={[
                  { value: null, label: t('web.notification_templates.all_companies') },
                  ...me.companies.map((c) => ({ value: c.id, label: c.name })),
                ]}
              />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item
              name="culture"
              label={t('web.notification_templates.culture')}
              rules={[{ required: true, message: t('web.platform.fields.required') }]}
            >
              <Select disabled={!!editing} options={me.cultures.map((c) => ({ value: c.code, label: c.name }))} />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item
          name="subject"
          label={t('web.notification_templates.subject')}
          rules={[{ required: true, message: t('web.platform.fields.required') }]}
        >
          <Input maxLength={500} />
        </Form.Item>
        <Form.Item
          name="body"
          label={
            <Space>
              {t('web.notification_templates.body')}
              <Popconfirm
                title={t('web.notification_templates.reset_confirm')}
                onConfirm={() => form.setFieldsValue({ subject: list.defaultSubject, body: list.defaultBody })}
              >
                <Button size="small" icon={<RollbackOutlined />}>
                  {t('web.notification_templates.reset')}
                </Button>
              </Popconfirm>
            </Space>
          }
          rules={[{ required: true, message: t('web.platform.fields.required') }]}
        >
          {/* Form.Item passes the value and onChange; the props here only satisfy the type */}
          <CodeEditor
            language="html"
            label={t('web.notification_templates.body')}
            value=""
            onChange={() => undefined}
            height={420}
          />
        </Form.Item>
        <Collapse
          items={[
            {
              key: 'fields',
              label: t('web.notification_templates.fields'),
              children: (
                <Flex vertical gap={6}>
                  {FIELDS.map((field) => (
                    <div key={field}>
                      <Typography.Text code>{`{{ ${field} }}`}</Typography.Text>{' '}
                      <Typography.Text type="secondary">
                        {t(`web.notification_templates.field.${field.replace('.*', '').replace('.', '_')}`)}
                      </Typography.Text>
                    </div>
                  ))}
                </Flex>
              ),
            },
          ]}
        />
      </FormDrawer>
    </>
  );
};
