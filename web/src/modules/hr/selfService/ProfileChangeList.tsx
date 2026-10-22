import { Alert, Descriptions, Empty, Flex, Table, Tag, Typography } from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { FamilyMember, ProfileChangeItem, ProfileChangeStatus } from '@/shared/api/generated/model';
import { ApprovalTimeline } from '@/shared/approvals/ApprovalTimeline';
import { formatDateTime } from '@/shared/format/date';
import { Contacts, dash, Family, type ContactLists } from '../employees/EmployeeSections';
import { PROFILE_CHANGE_DOCUMENT } from './profileChanges';

const statusColors: Record<ProfileChangeStatus, string> = {
  Pending: 'processing',
  Returned: 'warning',
  Applied: 'success',
  NotApplied: 'default',
  Rejected: 'error',
  Withdrawn: 'default',
};

export const ProfileChangeStatusTag = ({ status }: { status: ProfileChangeStatus }) => {
  const { t } = useTranslation();
  return <Tag color={statusColors[status]}>{t(`web.hr.profile_changes.status.${status}`)}</Tag>;
};

/** What a request proposes, in the shape of the card's section. */
export const ProposedValues = ({ item }: { item: ProfileChangeItem }) => {
  const { t } = useTranslation();
  if (item.kind === 'Iban') {
    return (
      <Descriptions
        size="small"
        items={[
          {
            key: 'iban',
            label: t('web.hr.profile_changes.proposed_iban'),
            children: item.proposedIban ?? t('web.hr.profile_changes.no_iban'),
          },
        ]}
      />
    );
  }

  if (item.kind === 'Family') {
    return <Family members={(item.proposed ?? []) as FamilyMember[]} />;
  }

  return <Contacts lists={item.proposed as ContactLists} />;
};

interface ProfileChangeListProps {
  items: ProfileChangeItem[] | undefined;
  loading?: boolean;
  /** Buttons of a request, e.g. withdraw or correct for its requester. */
  actions?: (item: ProfileChangeItem) => ReactNode;
}

/**
 * Change requests, newest first (D39): a row opens the proposed values, the requester's note, an approver's reason to
 * return it and the approval timeline.
 */
export const ProfileChangeList = ({ items, loading, actions }: ProfileChangeListProps) => {
  const { t, i18n } = useTranslation();
  return (
    <Table<ProfileChangeItem>
      size="small"
      rowKey="id"
      loading={loading}
      pagination={false}
      dataSource={items}
      locale={{
        emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.hr.profile_changes.empty')} />,
      }}
      expandable={{
        expandedRowRender: (item) => (
          <Flex vertical gap={12}>
            {item.status === 'Returned' && item.returnComment && (
              <Alert
                type="warning"
                showIcon
                message={t('web.hr.profile_changes.returned_reason')}
                description={item.returnComment}
              />
            )}
            {item.status === 'NotApplied' && (
              <Alert type="info" showIcon message={t('web.hr.profile_changes.not_applied_hint')} />
            )}
            {item.note && (
              <Typography.Paragraph style={{ margin: 0 }}>
                <Typography.Text type="secondary">{t('web.hr.profile_changes.note')}: </Typography.Text>
                {item.note}
              </Typography.Paragraph>
            )}
            <Typography.Title level={5} style={{ margin: 0 }}>
              {t('web.hr.profile_changes.proposed')}
            </Typography.Title>
            <ProposedValues item={item} />
            <Typography.Title level={5} style={{ margin: 0 }}>
              {t('web.hr.profile_changes.approval')}
            </Typography.Title>
            <ApprovalTimeline documentType={PROFILE_CHANGE_DOCUMENT} documentId={item.id} />
          </Flex>
        ),
      }}
      columns={[
        { key: 'number', dataIndex: 'number', title: t('web.hr.profile_changes.number'), width: 150 },
        {
          key: 'kind',
          title: t('web.hr.profile_changes.kind_label'),
          render: (_, item) => t(`web.hr.profile_changes.kind.${item.kind}`),
        },
        {
          key: 'status',
          title: t('web.hr.profile_changes.status_label'),
          render: (_, item) => <ProfileChangeStatusTag status={item.status} />,
        },
        {
          key: 'createdAt',
          title: t('web.hr.profile_changes.created_at'),
          render: (_, item) => formatDateTime(item.createdAt, i18n.language),
        },
        {
          key: 'closedAt',
          title: t('web.hr.profile_changes.closed_at'),
          render: (_, item) => (item.closedAt ? formatDateTime(item.closedAt, i18n.language) : dash),
        },
        ...(actions ? [{ key: 'actions', render: (_: unknown, item: ProfileChangeItem) => actions(item) }] : []),
      ]}
    />
  );
};
