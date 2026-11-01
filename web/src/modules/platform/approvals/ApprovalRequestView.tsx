import { CheckOutlined, CloseOutlined, LinkOutlined, RollbackOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Empty,
  Flex,
  Input,
  Result,
  Select,
  Skeleton,
  Tag,
  Typography,
} from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useMe } from '@/app/me/meContext';
import {
  useAssignApprovers,
  useDecideApprovalTask,
  useForceApproveApprovalRequest,
  useGetApprovalRequest,
  useReassignApprovalTask,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { ApprovalDecision, ApprovalRequestDetails } from '@/shared/api/generated/model';
import { useAiFeature } from '@/shared/ai/useAiFeature';
import { ApprovalTimelineView } from '@/shared/approvals/ApprovalTimeline';
import { formatDateTime } from '@/shared/format/date';
import { platformAiFeatures } from '../permissions';
import { ApprovalSummaryCard } from './ApprovalSummaryCard';
import { refreshApprovals, statusColors } from './approvalQueries';
import { UserPicker } from './UserPicker';

/** A request with the caller's decision, the administrator's actions and the timeline (§2.6). */
export const ApprovalRequestView = ({ requestId }: { requestId: string }) => {
  const { t } = useTranslation();
  const request = useGetApprovalRequest(requestId, { query: { retry: false, meta: { silent: true } } });

  if (request.isLoading) {
    return <Skeleton active />;
  }

  if (!request.data) {
    return <Result status="404" title={t('web.approvals.request.not_found')} />;
  }

  return <RequestDetails details={request.data} />;
};

const RequestDetails = ({ details }: { details: ApprovalRequestDetails }) => {
  const { t, i18n } = useTranslation();
  const step = details.currentStep !== null ? details.steps[details.currentStep] : undefined;
  const openTasks = details.tasks.filter((task) => task.status === 'Pending');
  const summaryAi = useAiFeature(platformAiFeatures.approvalSummary);
  const open = details.status === 'Pending' || details.status === 'NeedsAttention';

  return (
    <Flex vertical gap={16}>
      <Flex justify="space-between" align="start" gap={12} wrap>
        <div>
          <Typography.Title level={4} style={{ margin: 0 }}>
            {details.title}
          </Typography.Title>
          {details.summary && <Typography.Text type="secondary">{details.summary}</Typography.Text>}
        </div>
        <Tag color={statusColors[details.status]}>{t(`web.approvals.status.${details.status}`)}</Tag>
      </Flex>
      <Descriptions
        size="small"
        column={1}
        items={[
          { key: 'type', label: t('web.approvals.request.document_type'), children: details.documentTypeName },
          { key: 'requester', label: t('web.approvals.request.requester'), children: details.requesterName },
          {
            key: 'submitted',
            label: t('web.approvals.request.submitted_at'),
            children: formatDateTime(details.submittedAt, i18n.language),
          },
          ...(step
            ? [
                {
                  key: 'step',
                  label: t('web.approvals.request.current_step'),
                  children: t('web.approvals.request.step_of', {
                    index: details.currentStep! + 1,
                    count: details.steps.length,
                    name: step.name,
                  }),
                },
              ]
            : []),
          ...(openTasks.length > 0
            ? [
                {
                  key: 'waiting',
                  label: t('web.approvals.request.waiting_for'),
                  children: openTasks.map((task) => task.assigneeName).join(', '),
                },
              ]
            : []),
          ...(details.link
            ? [
                {
                  key: 'link',
                  label: t('web.approvals.request.document'),
                  children: (
                    <Link to={details.link}>
                      <LinkOutlined /> {t('web.approvals.request.open_document')}
                    </Link>
                  ),
                },
              ]
            : []),
        ]}
      />
      {summaryAi && open && (details.myTaskId || details.canAdminister) && (
        <ApprovalSummaryCard requestId={details.id} />
      )}
      {details.myTaskId && (
        <DecisionPanel details={details} taskId={details.myTaskId} allowReturn={!!step?.allowReturn} />
      )}
      {details.canAdminister && open && <AdminPanel details={details} />}
      <Card size="small" title={t('web.approvals.request.timeline')}>
        {details.history.length > 0 ? <ApprovalTimelineView details={details} /> : <Empty />}
      </Card>
    </Flex>
  );
};

const DecisionPanel = ({
  details,
  taskId,
  allowReturn,
}: {
  details: ApprovalRequestDetails;
  taskId: string;
  allowReturn: boolean;
}) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const decide = useDecideApprovalTask();
  const [comment, setComment] = useState('');
  const [missing, setMissing] = useState(false);
  const me = useMe();
  const task = details.tasks.find((item) => item.id === taskId);

  const send = async (decision: ApprovalDecision) => {
    if (decision !== 'Approve' && !comment.trim()) {
      setMissing(true);
      return;
    }

    await decide.mutateAsync({ id: taskId, data: { decision, comment: comment.trim() || null } });
    message.success(t(`web.approvals.decision.done.${decision}`));
    setComment('');
    await refreshApprovals(queryClient, details.id);
  };

  return (
    <Card size="small" title={t('web.approvals.decision.title')}>
      {task && task.assigneeUserId !== me.user.id && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          title={t('web.approvals.decision.on_behalf', { name: task.assigneeName })}
        />
      )}
      <Input.TextArea
        rows={3}
        maxLength={2000}
        value={comment}
        status={missing ? 'error' : undefined}
        placeholder={t('web.approvals.decision.comment_placeholder')}
        aria-label={t('web.approvals.decision.comment')}
        onChange={(event) => {
          setComment(event.target.value);
          setMissing(false);
        }}
      />
      {missing && <Typography.Text type="danger">{t('web.approvals.decision.comment_required')}</Typography.Text>}
      <Flex gap={8} justify="flex-end" style={{ marginTop: 12 }} wrap>
        {allowReturn && (
          <Button icon={<RollbackOutlined />} loading={decide.isPending} onClick={() => void send('Return')}>
            {t('web.approvals.decision.return')}
          </Button>
        )}
        <Button danger icon={<CloseOutlined />} loading={decide.isPending} onClick={() => void send('Reject')}>
          {t('web.approvals.decision.reject')}
        </Button>
        <Button type="primary" icon={<CheckOutlined />} loading={decide.isPending} onClick={() => void send('Approve')}>
          {t('web.approvals.decision.approve')}
        </Button>
      </Flex>
    </Card>
  );
};

const AdminPanel = ({ details }: { details: ApprovalRequestDetails }) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const forceApprove = useForceApproveApprovalRequest();
  const reassign = useReassignApprovalTask();
  const assign = useAssignApprovers();
  const openTasks = details.tasks.filter((task) => task.status === 'Pending');
  const [taskId, setTaskId] = useState<string | undefined>(openTasks[0]?.id);
  const [newAssignee, setNewAssignee] = useState<string>();
  const [approvers, setApprovers] = useState<string[]>([]);
  const [comment, setComment] = useState('');

  const done = async (key: string) => {
    message.success(t(key));
    await refreshApprovals(queryClient, details.id);
  };

  return (
    <Card size="small" title={t('web.approvals.admin.title')}>
      <Flex vertical gap={16}>
        {details.status === 'NeedsAttention' && (
          <Alert type="warning" showIcon title={t('web.approvals.admin.needs_attention')} />
        )}
        {details.status === 'NeedsAttention' && details.currentStep !== null && (
          <Flex vertical gap={8}>
            <Typography.Text strong>{t('web.approvals.admin.assign')}</Typography.Text>
            <UserPicker
              mode="multiple"
              value={approvers}
              onChange={setApprovers}
              ariaLabel={t('web.approvals.admin.assign')}
            />
            <Button
              disabled={approvers.length === 0}
              loading={assign.isPending}
              onClick={() =>
                void assign
                  .mutateAsync({ id: details.id, data: { userIds: approvers } })
                  .then(() => done('web.approvals.admin.assigned'))
              }
            >
              {t('web.approvals.admin.assign_button')}
            </Button>
          </Flex>
        )}
        {openTasks.length > 0 && (
          <Flex vertical gap={8}>
            <Typography.Text strong>{t('web.approvals.admin.reassign')}</Typography.Text>
            <Select
              value={taskId}
              onChange={setTaskId}
              aria-label={t('web.approvals.admin.task')}
              options={openTasks.map((task) => ({ value: task.id, label: task.assigneeName }))}
            />
            <UserPicker
              value={newAssignee}
              onChange={setNewAssignee}
              ariaLabel={t('web.approvals.admin.new_assignee')}
              exclude={openTasks.map((task) => task.assigneeUserId)}
            />
            <Button
              disabled={!taskId || !newAssignee}
              loading={reassign.isPending}
              onClick={() =>
                void reassign
                  .mutateAsync({ id: taskId!, data: { userId: newAssignee! } })
                  .then(() => done('web.approvals.admin.reassigned'))
              }
            >
              {t('web.approvals.admin.reassign_button')}
            </Button>
          </Flex>
        )}
        <Flex vertical gap={8}>
          <Typography.Text strong>{t('web.approvals.admin.force_approve')}</Typography.Text>
          <Input
            value={comment}
            maxLength={2000}
            placeholder={t('web.approvals.admin.force_comment')}
            aria-label={t('web.approvals.admin.force_comment')}
            onChange={(event) => setComment(event.target.value)}
          />
          <Button
            danger
            loading={forceApprove.isPending}
            onClick={() =>
              void forceApprove
                .mutateAsync({ id: details.id, data: { comment: comment.trim() || null } })
                .then(() => done('web.approvals.admin.force_approved'))
            }
          >
            {t('web.approvals.admin.force_button')}
          </Button>
        </Flex>
      </Flex>
    </Card>
  );
};
