import { Card, Drawer, Table, Tag, Typography, type TableColumnsType } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useListMyApprovalTasks } from '@/shared/api/generated/endpoints/platform/platform';
import type { MyApprovalTaskItem } from '@/shared/api/generated/model';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDateTime, formatRelativeTime } from '@/shared/format/date';
import { ApprovalRequestView } from './ApprovalRequestView';

/** "Onay Kutum": the caller's open approval tasks and those they act for (§2.7); a row opens the request. */
export const ApprovalInboxPage = () => {
  const { t, i18n } = useTranslation();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [openRequest, setOpenRequest] = useState<string>();
  const tasks = useListMyApprovalTasks({ page, pageSize });
  const now = Date.now();

  const columns: TableColumnsType<MyApprovalTaskItem> = [
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
      key: 'step',
      title: t('web.approvals.inbox.step'),
      render: (_, row) => (
        <>
          {t('web.approvals.request.step_of', { index: row.step + 1, count: row.stepCount, name: row.stepName })}
          {row.onBehalfOfName && (
            <Tag color="purple" style={{ marginInlineStart: 8 }}>
              {t('web.approvals.inbox.on_behalf', { name: row.onBehalfOfName })}
            </Tag>
          )}
        </>
      ),
    },
    {
      key: 'assigned',
      title: t('web.approvals.inbox.assigned_at'),
      render: (_, row) => formatRelativeTime(row.assignedAt, i18n.language),
    },
    {
      key: 'due',
      title: t('web.approvals.inbox.due_at'),
      render: (_, row) =>
        row.dueAt ? (
          <Typography.Text type={Date.parse(row.dueAt) < now ? 'danger' : undefined}>
            {formatDateTime(row.dueAt, i18n.language)}
          </Typography.Text>
        ) : (
          '—'
        ),
    },
  ];

  return (
    <>
      <PageHeader title={t('web.approvals.inbox.title')} subtitle={t('web.approvals.inbox.subtitle')} />
      <Card>
        <Table<MyApprovalTaskItem>
          rowKey="taskId"
          columns={columns}
          dataSource={tasks.data?.items ?? []}
          loading={tasks.isFetching}
          locale={{ emptyText: t('web.approvals.inbox.empty') }}
          onRow={(row) => ({ onClick: () => setOpenRequest(row.requestId), style: { cursor: 'pointer' } })}
          pagination={{
            current: page,
            pageSize,
            total: tasks.data?.totalCount ?? 0,
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
