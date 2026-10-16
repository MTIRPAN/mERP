import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { Button, Card, Form, Modal, Select, Skeleton, Table, Tag, type TableColumnsType } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { useMe } from '@/app/me/meContext';
import { platformPermissions } from '@/modules/platform/permissions';
import { useListDocumentTemplates, useListDocumentTypes } from '@/shared/api/generated/endpoints/platform/platform';
import type { DocumentTemplateItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusTag } from '@/shared/components/StatusTag';

interface NewTemplateValues {
  documentType?: string;
  companyId?: string;
  culture?: string;
}

/** The tenant's document templates (PLAN P12, D36); the editor opens on its own page. */
export const DocumentTemplatesPage = () => {
  const { t } = useTranslation();
  const me = useMe();
  const navigate = useNavigate();
  const canManage = useHasPermission(platformPermissions.documentTemplatesManage);
  const types = useListDocumentTypes();
  const [documentType, setDocumentType] = useState<string>();
  const templates = useListDocumentTemplates({ documentType });
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm<NewTemplateValues>();

  if (!types.data) {
    return <Skeleton active />;
  }

  const cultureName = (code: string) => me.cultures.find((c) => c.code === code)?.name ?? code;

  const columns: TableColumnsType<DocumentTemplateItem> = [
    { key: 'type', title: t('web.document_templates.type'), render: (_, row) => row.documentTypeName },
    {
      key: 'company',
      title: t('web.document_templates.company'),
      render: (_, row) => row.companyName ?? t('web.document_templates.all_companies'),
    },
    { key: 'culture', title: t('web.document_templates.culture'), render: (_, row) => cultureName(row.culture) },
    {
      key: 'revision',
      title: t('web.document_templates.revision'),
      render: (_, row) => <Tag>{t('web.document_templates.revision_value', { revision: row.revision })}</Tag>,
    },
    {
      key: 'status',
      title: t('web.platform.fields.status'),
      render: (_, row) => <StatusTag status={row.isActive ? 'active' : 'inactive'} />,
    },
    {
      key: 'actions',
      width: 56,
      render: (_, row) => (
        <Button
          type="text"
          icon={<EditOutlined />}
          aria-label={t('web.common.edit')}
          onClick={() => void navigate(`/system/document-templates/${row.id}`)}
        />
      ),
    },
  ];

  const create = (values: NewTemplateValues) => {
    const query = new URLSearchParams({ type: values.documentType ?? '', culture: values.culture ?? '' });
    if (values.companyId) {
      query.set('company', values.companyId);
    }

    void navigate(`/system/document-templates/new?${query.toString()}`);
  };

  return (
    <>
      <PageHeader
        title={t('web.document_templates.title')}
        subtitle={t('web.document_templates.subtitle')}
        extra={
          canManage && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
              {t('web.document_templates.new')}
            </Button>
          )
        }
      />
      <Card
        title={
          <Select
            allowClear
            style={{ minWidth: 240 }}
            value={documentType}
            onChange={setDocumentType}
            placeholder={t('web.document_templates.all_types')}
            aria-label={t('web.document_templates.type')}
            options={types.data.map((type) => ({ value: type.key, label: type.name }))}
          />
        }
      >
        <Table<DocumentTemplateItem>
          rowKey="id"
          columns={columns}
          dataSource={templates.data ?? []}
          loading={templates.isFetching}
          pagination={false}
          locale={{ emptyText: t('web.document_templates.empty') }}
          scroll={{ x: 'max-content' }}
        />
      </Card>
      <Modal
        title={t('web.document_templates.new')}
        open={creating}
        onCancel={() => setCreating(false)}
        onOk={() => form.submit()}
        okText={t('web.document_templates.open_editor')}
        cancelText={t('web.common.cancel')}
        destroyOnHidden
      >
        <Form<NewTemplateValues>
          form={form}
          layout="vertical"
          initialValues={{ documentType: documentType ?? types.data[0]?.key, culture: me.cultures[0]?.code }}
          onFinish={create}
          requiredMark="optional"
          preserve={false}
        >
          <Form.Item
            name="documentType"
            label={t('web.document_templates.type')}
            rules={[{ required: true, message: t('web.platform.fields.required') }]}
          >
            <Select options={types.data.map((type) => ({ value: type.key, label: type.name }))} />
          </Form.Item>
          <Form.Item
            name="companyId"
            label={t('web.document_templates.company')}
            extra={t('web.document_templates.company_hint')}
          >
            <Select
              allowClear
              placeholder={t('web.document_templates.all_companies')}
              showSearch={{ optionFilterProp: 'label' }}
              options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
            />
          </Form.Item>
          <Form.Item
            name="culture"
            label={t('web.document_templates.culture')}
            rules={[{ required: true, message: t('web.platform.fields.required') }]}
          >
            <Select options={me.cultures.map((c) => ({ value: c.code, label: c.name }))} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};
