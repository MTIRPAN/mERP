import { Alert, App, Button, Card, Descriptions, Drawer, Flex, Form, Input, Modal, Skeleton, Tag } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import {
  cancelRequisition,
  submitRequisition,
  useGetRequisition,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { RequisitionStatus } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import { ApprovalTimeline } from '@/shared/approvals/ApprovalTimeline';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { formatDate, formatDateTime } from '@/shared/format/date';
import { recruitmentPermissions } from '../permissions';
import { RequisitionDrawer } from './RequisitionDrawer';
import { REQUISITION_DOCUMENT, statusColors, useRequisitionRefresh } from './requisitions';

export const RequisitionStatusTag = ({ status }: { status: RequisitionStatus }) => {
  const { t } = useTranslation();
  return <Tag color={statusColors[status]}>{t(`web.recruitment.requisitions.status.${status}`)}</Tag>;
};

/** Asks for the reason of a cancellation; API field errors are shown on the field. */
const CancelDialog = ({
  onSubmit,
  onClose,
}: {
  onSubmit: (reason: string | null) => Promise<unknown>;
  onClose: () => void;
}) => {
  const { t } = useTranslation();
  const [form] = Form.useForm<{ reason?: string }>();
  const [saving, setSaving] = useState(false);

  const submit = async ({ reason }: { reason?: string }) => {
    setSaving(true);
    try {
      await onSubmit(reason?.trim() || null);
      onClose();
    } catch (error) {
      if (!applyApiErrorsToForm(form, error)) {
        throw error;
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      title={t('web.recruitment.requisitions.cancel_title')}
      open
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('web.recruitment.requisitions.cancel')}
      okButtonProps={{ danger: true }}
      confirmLoading={saving}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" onFinish={submit}>
        <Form.Item name="reason" label={t('web.recruitment.requisitions.fields.cancellation_reason')}>
          <Input.TextArea maxLength={500} showCount autoSize={{ minRows: 3, maxRows: 6 }} />
        </Form.Item>
      </Form>
    </Modal>
  );
};

interface RequisitionDetailDrawerProps {
  id: string;
  onClose: () => void;
}

/**
 * A requisition (PLAN R2, D48): what it asks for, the salary range for those who may see it, its approval timeline
 * and what the caller may do now: correct and send it, or cancel it.
 */
export const RequisitionDetailDrawer = ({ id, onClose }: RequisitionDetailDrawerProps) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const refresh = useRequisitionRefresh();
  const navigate = useNavigate();
  const canOpen = useHasPermission(recruitmentPermissions.openingsManage);
  const { data: details } = useGetRequisition(id);
  const [dialog, setDialog] = useState<'edit' | 'cancel'>();
  const [busy, setBusy] = useState(false);
  const money = (value: number) =>
    new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 2, minimumFractionDigits: 0 }).format(value);

  const send = async () => {
    setBusy(true);
    try {
      await submitRequisition(id);
      void message.success(t('web.recruitment.requisitions.saved.sent'));
      await refresh(id);
    } catch (error) {
      const fields = isApiError(error) ? (error.problem?.fieldErrors ?? []) : [];
      void message.error(
        fields.length > 0 ? fields.map((e) => e.message).join(' ') : isApiError(error) ? error.message : String(error),
      );
    } finally {
      setBusy(false);
    }
  };

  const requisition = details?.requisition;
  const dash = '—';
  const salary =
    details && (details.salaryMin != null || details.salaryMax != null)
      ? `${details.salaryMin != null ? money(details.salaryMin) : dash} – ${details.salaryMax != null ? money(details.salaryMax) : dash} ${details.currency ?? ''}`.trim()
      : dash;
  const footer = details && (
    <Flex justify="flex-end" gap={8} wrap>
      {details.canEdit && <Button onClick={() => setDialog('edit')}>{t('web.common.edit')}</Button>}
      {details.canEdit && (
        <Button type="primary" loading={busy} onClick={() => void send()}>
          {t('web.recruitment.requisitions.send')}
        </Button>
      )}
      {details.canCancel && (
        <Button danger onClick={() => setDialog('cancel')}>
          {t('web.recruitment.requisitions.cancel')}
        </Button>
      )}
    </Flex>
  );

  return (
    <Drawer
      title={requisition ? requisition.number : t('web.recruitment.requisitions.detail_title')}
      open
      onClose={onClose}
      size={680}
      footer={footer}
    >
      {!details || !requisition ? (
        <Skeleton active />
      ) : (
        <Flex vertical gap={16}>
          {requisition.status === 'Returned' && details.returnComment && (
            <Alert
              type="warning"
              showIcon
              title={t('web.recruitment.requisitions.returned_reason')}
              description={details.returnComment}
            />
          )}
          {details.canCreateOpening && (
            <Alert
              type="success"
              showIcon
              title={t('web.recruitment.requisitions.ready_for_opening')}
              action={
                canOpen && (
                  <Button
                    size="small"
                    type="primary"
                    onClick={() => void navigate(`/recruitment/openings?fromRequisition=${id}`)}
                  >
                    {t('web.recruitment.requisitions.create_opening')}
                  </Button>
                )
              }
            />
          )}
          <Descriptions
            size="small"
            bordered
            column={1}
            items={[
              {
                key: 'status',
                label: t('web.recruitment.requisitions.fields.status'),
                children: <RequisitionStatusTag status={requisition.status} />,
              },
              {
                key: 'company',
                label: t('web.recruitment.requisitions.fields.company'),
                children: requisition.companyName ?? dash,
              },
              {
                key: 'unit',
                label: t('web.recruitment.requisitions.fields.unit'),
                children: requisition.orgUnitName ?? dash,
              },
              {
                key: 'position',
                label: t('web.recruitment.requisitions.fields.position'),
                children: requisition.positionId
                  ? requisition.positionTitle
                  : `${requisition.positionTitle}${details.newPositionLevel ? ` (${details.newPositionLevel})` : ''} · ${t('web.recruitment.requisitions.kinds.new')}`,
              },
              {
                key: 'headcount',
                label: t('web.recruitment.requisitions.fields.headcount'),
                children: t('web.recruitment.requisitions.filled_of', {
                  filled: requisition.filledCount,
                  headcount: requisition.headcount,
                }),
              },
              {
                key: 'reason',
                label: t('web.recruitment.requisitions.fields.reason'),
                children: t(`web.recruitment.requisitions.reasons.${requisition.reason}`),
              },
              ...(details.replacedEmployeeName
                ? [
                    {
                      key: 'replaced',
                      label: t('web.recruitment.requisitions.fields.replaced_employee'),
                      children: details.replacedEmployeeName,
                    },
                  ]
                : []),
              {
                key: 'employmentType',
                label: t('web.recruitment.requisitions.fields.employment_type'),
                children: t(`web.recruitment.requisitions.employment_types.${details.employmentType}`),
              },
              {
                key: 'start',
                label: t('web.recruitment.requisitions.fields.desired_start_date'),
                children: requisition.desiredStartDate ? formatDate(requisition.desiredStartDate, i18n.language) : dash,
              },
              ...(details.salaryVisible
                ? [{ key: 'salary', label: t('web.recruitment.requisitions.fields.salary'), children: salary }]
                : []),
              {
                key: 'manager',
                label: t('web.recruitment.requisitions.fields.hiring_manager'),
                children: requisition.hiringManagerName ?? dash,
              },
              {
                key: 'requester',
                label: t('web.recruitment.requisitions.fields.requester'),
                children: details.requesterName ?? dash,
              },
              {
                key: 'submitted',
                label: t('web.recruitment.requisitions.fields.submitted_at'),
                children: requisition.submittedAt ? formatDateTime(requisition.submittedAt, i18n.language) : dash,
              },
              {
                key: 'justification',
                label: t('web.recruitment.requisitions.fields.justification'),
                children: <span style={{ whiteSpace: 'pre-wrap' }}>{details.justification}</span>,
              },
              ...(details.cancellationReason
                ? [
                    {
                      key: 'cancellation',
                      label: t('web.recruitment.requisitions.fields.cancellation_reason'),
                      children: details.cancellationReason,
                    },
                  ]
                : []),
            ]}
          />
          {requisition.status !== 'Draft' && (
            <Card size="small" title={t('web.recruitment.requisitions.timeline')}>
              <ApprovalTimeline documentType={REQUISITION_DOCUMENT} documentId={id} />
            </Card>
          )}
        </Flex>
      )}
      {details && dialog === 'edit' && <RequisitionDrawer details={details} onClose={() => setDialog(undefined)} />}
      {dialog === 'cancel' && (
        <CancelDialog
          onClose={() => setDialog(undefined)}
          onSubmit={async (reason) => {
            await cancelRequisition(id, { reason });
            void message.success(t('web.recruitment.requisitions.cancelled'));
            await refresh(id);
          }}
        />
      )}
    </Drawer>
  );
};
