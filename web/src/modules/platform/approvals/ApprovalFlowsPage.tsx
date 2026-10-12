import { EditOutlined, ExperimentOutlined, PlusOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  Card,
  Form,
  Input,
  InputNumber,
  Select,
  Skeleton,
  Space,
  Table,
  Typography,
  type TableColumnsType,
} from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { useMe } from '@/app/me/meContext';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  useListApprovalDefinitions,
  useListApprovalDocumentTypes,
  useSubmitTestApprovalDocument,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { ApprovalDefinitionListItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusTag } from '@/shared/components/StatusTag';
import { ApprovalFlowDrawer } from './ApprovalFlowDrawer';
import { UserPicker } from './UserPicker';

/** Document type of test documents (ApprovalTestDocumentTypes, D34); registered where test documents are enabled. */
export const TEST_DOCUMENT_TYPE = 'platform.approval-test';

/** Approval flows per document type (PLAN P10c, §2.2): a company's own flows win over the general ones. */
export const ApprovalFlowsPage = () => {
  const { t, i18n } = useTranslation();
  const canManage = useHasPermission(platformPermissions.approvalDefinitionsManage);
  const documentTypes = useListApprovalDocumentTypes();
  const [selected, setSelected] = useState<string>();
  const documentType = documentTypes.data?.find((type) => type.key === (selected ?? documentTypes.data?.[0]?.key));
  const definitions = useListApprovalDefinitions(
    { documentType: documentType?.key },
    { query: { enabled: !!documentType } },
  );
  const [drawer, setDrawer] = useState<{ open: boolean; id?: string }>({ open: false });
  const [testOpen, setTestOpen] = useState(false);

  if (!documentTypes.data) {
    return <Skeleton active />;
  }

  const text = (texts: Record<string, string>) => texts[i18n.language] ?? texts[DEFAULT_CULTURE] ?? '';

  const columns: TableColumnsType<ApprovalDefinitionListItem> = [
    { key: 'name', title: t('web.approvals.flows.name'), render: (_, row) => text(row.name) },
    {
      key: 'company',
      title: t('web.approvals.flows.company'),
      render: (_, row) => row.companyName ?? t('web.approvals.flows.all_companies'),
    },
    { key: 'priority', dataIndex: 'priority', title: t('web.approvals.flows.priority'), width: 90 },
    { key: 'steps', dataIndex: 'stepCount', title: t('web.approvals.flows.steps'), width: 90 },
    { key: 'revision', dataIndex: 'revision', title: t('web.approvals.flows.revision'), width: 90 },
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
            render: (_: unknown, row: ApprovalDefinitionListItem) => (
              <Button
                type="text"
                icon={<EditOutlined />}
                aria-label={t('web.common.edit')}
                onClick={() => setDrawer({ open: true, id: row.id })}
              />
            ),
          },
        ]
      : []),
  ];

  return (
    <>
      <PageHeader
        title={t('web.approvals.flows.title')}
        subtitle={t('web.approvals.flows.subtitle')}
        extra={
          canManage &&
          documentType && (
            <Space>
              {documentType.key === TEST_DOCUMENT_TYPE && (
                <Button icon={<ExperimentOutlined />} onClick={() => setTestOpen(true)}>
                  {t('web.approvals.test.open')}
                </Button>
              )}
              <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
                {t('web.approvals.flows.new_button')}
              </Button>
            </Space>
          )
        }
      />
      <Card
        title={
          <Select
            style={{ minWidth: 260 }}
            value={documentType?.key}
            onChange={setSelected}
            aria-label={t('web.approvals.flows.document_type')}
            options={documentTypes.data.map((type) => ({ value: type.key, label: type.name }))}
            notFoundContent={t('web.approvals.flows.no_document_types')}
          />
        }
      >
        {documentType && (
          <Typography.Paragraph type="secondary">
            {t('web.approvals.flows.facts', { facts: documentType.facts.map((fact) => fact.label).join(', ') })}
          </Typography.Paragraph>
        )}
        <Table<ApprovalDefinitionListItem>
          rowKey="id"
          columns={columns}
          dataSource={definitions.data ?? []}
          loading={definitions.isFetching}
          pagination={false}
          locale={{
            emptyText: documentType?.autoApproveWhenNoDefinition
              ? t('web.approvals.flows.empty_auto')
              : t('web.approvals.flows.empty'),
          }}
          scroll={{ x: 'max-content' }}
        />
      </Card>
      {documentType && (
        <ApprovalFlowDrawer
          documentType={documentType}
          definitionId={drawer.id}
          open={drawer.open}
          onClose={() => setDrawer({ open: false })}
        />
      )}
      <TestDocumentDrawer open={testOpen} onClose={() => setTestOpen(false)} />
    </>
  );
};

interface TestValues {
  companyId?: string;
  summary?: string;
  amount?: number | null;
  category?: string;
  approverUserId?: string;
}

/** Sends a test document through the flows, as a module would (PLAN P10 acceptance, D34). */
const TestDocumentDrawer = ({ open, onClose }: { open: boolean; onClose: () => void }) => {
  const { t } = useTranslation();
  const me = useMe();
  const { message } = App.useApp();
  const submitTest = useSubmitTestApprovalDocument();

  const submit = async (values: TestValues) => {
    await submitTest.mutateAsync({
      data: {
        companyId: values.companyId ?? null,
        summary: values.summary ?? null,
        amount: values.amount ?? null,
        category: values.category?.trim() || null,
        approverUserId: values.approverUserId ?? null,
      },
    });
    message.success(t('web.approvals.test.sent'));
  };

  return (
    <FormDrawer<TestValues>
      title={t('web.approvals.test.title')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      width={520}
    >
      <Typography.Paragraph type="secondary">{t('web.approvals.test.hint')}</Typography.Paragraph>
      <Form.Item
        name="summary"
        label={t('web.approvals.test.summary')}
        rules={[{ required: true, message: t('web.platform.fields.required') }]}
      >
        <Input maxLength={200} />
      </Form.Item>
      <Form.Item name="companyId" label={t('web.approvals.flows.company')}>
        <Select allowClear options={me.companies.map((c) => ({ value: c.id, label: c.name }))} />
      </Form.Item>
      <Form.Item name="amount" label={t('web.approvals.test.amount')}>
        <InputNumber style={{ width: '100%' }} />
      </Form.Item>
      <Form.Item name="category" label={t('web.approvals.test.category')}>
        <Input maxLength={50} />
      </Form.Item>
      <Form.Item
        name="approverUserId"
        label={t('web.approvals.test.approver')}
        tooltip={t('web.approvals.test.approver_hint')}
      >
        <UserPicker ariaLabel={t('web.approvals.test.approver')} />
      </Form.Item>
    </FormDrawer>
  );
};
