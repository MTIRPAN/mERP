import { Card, Drawer, Select, Table, Tag, Typography, type TableColumnsType } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useListApprovalDocumentTypes,
  useListApprovalRequests,
} from '@/shared/api/generated/endpoints/platform/platform';
import { ApprovalStatus, type ApprovalRequestListItem } from '@/shared/api/generated/model';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDateTime } from '@/shared/format/date';
import { statusColors } from './approvalQueries';
import { ApprovalRequestView } from './ApprovalRequestView';

/** Requests of all modules for approval administrators; those that need attention first (§2.6). */
export const ApprovalRequestsPage = () => {
  const { t, i18n } = useTranslation();
  const [status, setStatus] = useState<ApprovalStatus | undefined>(ApprovalStatus.NeedsAttention);
  const [documentType, setDocumentType] = useState<string>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [openRequest, setOpenRequest] = useState<string>();
  const documentTypes = useListApprovalDocumentTypes();
  const requests = useListApprovalRequests({ page, pageSize, status, documentType });

  const columns: TableColumnsType<ApprovalRequestListItem> = [
    {
      key: 'title',
      title: t('web.approvals.inbox.document'),
      render: (_, row) => (
        <>
          <Typography.Text strong>{row.title}</Typography.Text>
          {row.summary && (
            <Typography.Text type="secondary" style={{ display: 'block' }}>
              {row.summary}
            </Typography.Text>
          )}
        </>
      ),
    },
    { key: 'type', title: t('web.approvals.inbox.document_type'), render: (_, row) => row.documentTypeName },
    { key: 'requester', title: t('web.approvals.inbox.requester'), render: (_, row) => row.requesterName },
    {
      key: 'status',
      title: t('web.platform.fields.status'),
      render: (_, row) => <Tag color={statusColors[row.status]}>{t(`web.approvals.status.${row.status}`)}</Tag>,
    },
    {
      key: 'submitted',
      title: t('web.approvals.request.submitted_at'),
      render: (_, row) => formatDateTime(row.submittedAt, i18n.language),
    },
  ];

  return (
    <>
      <PageHeader title={t('web.approvals.requests.title')} subtitle={t('web.approvals.requests.subtitle')} />
      <Card
        title={
          <span style={{ display: 'inline-flex', gap: 8, flexWrap: 'wrap' }}>
            <Select
              allowClear
              style={{ minWidth: 200 }}
              value={status}
              placeholder={t('web.approvals.requests.all_statuses')}
              aria-label={t('web.platform.fields.status')}
              onChange={(value?: ApprovalStatus) => {
                setStatus(value);
                setPage(1);
              }}
              options={Object.values(ApprovalStatus).map((value) => ({
                value,
                label: t(`web.approvals.status.${value}`),
              }))}
            />
            <Select
              allowClear
              style={{ minWidth: 220 }}
              value={documentType}
              placeholder={t('web.approvals.requests.all_types')}
              aria-label={t('web.approvals.inbox.document_type')}
              onChange={(value?: string) => {
                setDocumentType(value);
                setPage(1);
              }}
              options={(documentTypes.data ?? []).map((type) => ({ value: type.key, label: type.name }))}
            />
          </span>
        }
      >
        <Table<ApprovalRequestListItem>
          rowKey="id"
          columns={columns}
          dataSource={requests.data?.items ?? []}
          loading={requests.isFetching}
          locale={{ emptyText: t('web.approvals.requests.empty') }}
          onRow={(row) => ({ onClick: () => setOpenRequest(row.id), style: { cursor: 'pointer' } })}
          pagination={{
            current: page,
            pageSize,
            total: requests.data?.totalCount ?? 0,
            showSizeChanger: true,
            onChange: (next, size) => {
              setPage(next);
              setPageSize(size);
            },
          }}
          scroll={{ x: 'max-content' }}
        />
      </Card>
      <Drawer
        open={!!openRequest}
        onClose={() => setOpenRequest(undefined)}
        size={640}
        title={t('web.approvals.request.title')}
        destroyOnHidden
      >
        {openRequest && <ApprovalRequestView requestId={openRequest} />}
      </Drawer>
    </>
  );
};
