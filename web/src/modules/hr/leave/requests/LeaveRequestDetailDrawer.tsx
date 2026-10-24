import { DeleteOutlined, DownloadOutlined, FilePdfOutlined, UploadOutlined } from '@ant-design/icons';
import { Alert, App, Button, Card, Descriptions, Drawer, Empty, Flex, List, Popconfirm, Skeleton, Upload } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  cancelLeaveRequest,
  cancelMyLeaveRequest,
  deleteLeaveRequestFile,
  deleteMyLeaveRequestFile,
  downloadLeaveRequestFile,
  downloadMyLeaveRequestFile,
  getLeaveRequestForm,
  getMyLeaveRequestForm,
  requestMyLeaveCancellation,
  returnFromLeaveEarly,
  submitMyLeaveRequest,
  uploadLeaveRequestFile,
  uploadMyLeaveRequestFile,
  useGetLeaveRequest,
  useGetMyLeaveRequest,
} from '@/shared/api/generated/endpoints/hr/hr';
import type { LeaveRequestFileItem } from '@/shared/api/generated/model';
import { saveFile } from '@/shared/api/download';
import { isApiError } from '@/shared/api/problem';
import { ApprovalTimeline } from '@/shared/approvals/ApprovalTimeline';
import { formatDate, formatDateTime } from '@/shared/format/date';
import { EarlyReturnDialog, ReasonDialog } from './LeaveDialogs';
import { LeaveRequestDrawer } from './LeaveRequestDrawer';
import { LeaveRequestStatusTag } from './LeaveRequestStatusTag';
import {
  LEAVE_CANCELLATION_DOCUMENT,
  LEAVE_REQUEST_DOCUMENT,
  lengthText,
  periodText,
  useLeaveRefresh,
} from './leaveRequests';

type Dialog = 'edit' | 'cancel' | 'cancellation' | 'early-return';

interface LeaveRequestDetailDrawerProps {
  /** The employee's own request, or one HR sees in its scope. */
  mode: 'self' | 'hr';
  id: string;
  onClose: () => void;
}

/**
 * A leave request (PLAN L4, D44): its days, notes, files and approval timeline, with what the caller may do now:
 * correct, send or cancel it, ask to cancel an approved leave, or (HR) cancel it or end it early.
 */
export const LeaveRequestDetailDrawer = ({ mode, id, onClose }: LeaveRequestDetailDrawerProps) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const refresh = useLeaveRefresh();
  const self = mode === 'self';
  const mine = useGetMyLeaveRequest(id, { query: { enabled: self } });
  const theirs = useGetLeaveRequest(id, { query: { enabled: !self } });
  const details = (self ? mine : theirs).data;
  const [dialog, setDialog] = useState<Dialog>();
  const [busy, setBusy] = useState(false);

  const run = async (action: () => Promise<unknown>, done: string) => {
    setBusy(true);
    try {
      await action();
      void message.success(t(done));
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

  /** The request form as PDF, to be signed (D46). */
  const printForm = async (number: string) => {
    try {
      await saveFile(
        self ? getMyLeaveRequestForm(id) : getLeaveRequestForm(id),
        t('web.hr.leave_requests.form_file', { number }),
      );
    } catch (error) {
      void message.error(isApiError(error) ? error.message : String(error));
    }
  };

  const download = (file: LeaveRequestFileItem) =>
    void saveFile(
      self ? downloadMyLeaveRequestFile(id, file.id) : downloadLeaveRequestFile(id, file.id),
      file.fileName,
    ).catch(() => undefined);

  const footer = details && (
    <Flex justify="flex-end" gap={8} wrap>
      {details.request.status !== 'Draft' && (
        <Button icon={<FilePdfOutlined />} onClick={() => void printForm(details.request.number)}>
          {t('web.hr.leave_requests.form')}
        </Button>
      )}
      {details.actions.edit && <Button onClick={() => setDialog('edit')}>{t('web.common.edit')}</Button>}
      {details.actions.submit && self && (
        <Button
          type="primary"
          loading={busy}
          onClick={() => void run(() => submitMyLeaveRequest(id), 'web.hr.leave_requests.saved.self')}
        >
          {t('web.hr.leave_requests.send')}
        </Button>
      )}
      {details.actions.requestCancellation && (
        <Button onClick={() => setDialog('cancellation')}>{t('web.hr.leave_requests.request_cancellation')}</Button>
      )}
      {details.actions.returnEarly && (
        <Button onClick={() => setDialog('early-return')}>{t('web.hr.leave_requests.early_return')}</Button>
      )}
      {details.actions.cancel && (
        <Button danger onClick={() => setDialog('cancel')}>
          {t('web.hr.leave_requests.cancel')}
        </Button>
      )}
    </Flex>
  );

  const request = details?.request;
  const dash = '—';
  return (
    <Drawer
      title={request ? request.number : t('web.hr.leave_requests.detail_title')}
      open
      onClose={onClose}
      size={680}
      footer={footer}
    >
      {!details || !request ? (
        <Skeleton active />
      ) : (
        <Flex vertical gap={16}>
          {request.status === 'Returned' && details.returnComment && (
            <Alert
              type="warning"
              showIcon
              title={t('web.hr.leave_requests.returned_reason')}
              description={details.returnComment}
            />
          )}
          <Descriptions
            size="small"
            bordered
            column={1}
            items={[
              ...(self
                ? []
                : [
                    {
                      key: 'employee',
                      label: t('web.hr.leave_requests.fields.employee'),
                      children: request.employeeName,
                    },
                    {
                      key: 'company',
                      label: t('web.hr.leave_requests.fields.company'),
                      children: request.companyName ?? dash,
                    },
                  ]),
              { key: 'type', label: t('web.hr.leave_requests.fields.type'), children: request.leaveTypeName },
              {
                key: 'period',
                label: t('web.hr.leave_requests.fields.period'),
                children: periodText(request, t, i18n.language),
              },
              {
                key: 'length',
                label: t('web.hr.leave_requests.fields.length'),
                children: lengthText(request.basis, request.quantity, t, i18n.language),
              },
              ...(request.travelDays > 0
                ? [
                    {
                      key: 'travel',
                      label: t('web.hr.leave_requests.fields.travel_days'),
                      children: request.travelDays,
                    },
                  ]
                : []),
              ...(details.originalEndDate
                ? [
                    {
                      key: 'original',
                      label: t('web.hr.leave_requests.fields.original_end_date'),
                      children: formatDate(details.originalEndDate, i18n.language),
                    },
                  ]
                : []),
              {
                key: 'status',
                label: t('web.hr.leave_requests.fields.status'),
                children: <LeaveRequestStatusTag status={request.status} />,
              },
              {
                key: 'origin',
                label: t('web.hr.leave_requests.fields.origin'),
                children: t(`web.hr.leave_requests.origins.${request.origin}`),
              },
              {
                key: 'submitted',
                label: t('web.hr.leave_requests.fields.submitted_at'),
                children: request.submittedAt ? formatDateTime(request.submittedAt, i18n.language) : dash,
              },
              { key: 'note', label: t('web.hr.leave_requests.fields.note'), children: details.note ?? dash },
              { key: 'address', label: t('web.hr.leave_requests.fields.address'), children: details.address ?? dash },
              { key: 'phone', label: t('web.hr.leave_requests.fields.phone'), children: details.phone ?? dash },
              {
                key: 'substitute',
                label: t('web.hr.leave_requests.fields.substitute'),
                children: details.substituteName ?? dash,
              },
              ...(details.cancellationReason
                ? [
                    {
                      key: 'reason',
                      label: t('web.hr.leave_requests.fields.reason'),
                      children: details.cancellationReason,
                    },
                  ]
                : []),
            ]}
          />
          <Card
            size="small"
            title={t('web.hr.leave_requests.fields.files')}
            extra={
              details.actions.manageFiles && (
                <Upload
                  showUploadList={false}
                  accept={details.fileExtensions.map((e) => `.${e}`).join(',')}
                  customRequest={({ file }) =>
                    void run(
                      () =>
                        self
                          ? uploadMyLeaveRequestFile(id, { file: file as File })
                          : uploadLeaveRequestFile(id, { file: file as File }),
                      'web.hr.leave_requests.file_added',
                    )
                  }
                >
                  <Button size="small" icon={<UploadOutlined />}>
                    {t('web.hr.leave_requests.add_file')}
                  </Button>
                </Upload>
              )
            }
          >
            {details.requiresDocument && details.files.length === 0 && (
              <Alert
                type="info"
                showIcon
                title={t('web.hr.leave_requests.fields.document_required')}
                style={{ marginBottom: 8 }}
              />
            )}
            <List
              size="small"
              dataSource={details.files}
              locale={{
                emptyText: (
                  <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.hr.leave_requests.no_files')} />
                ),
              }}
              renderItem={(file) => (
                <List.Item
                  actions={[
                    <Button
                      key="download"
                      type="link"
                      size="small"
                      icon={<DownloadOutlined />}
                      onClick={() => download(file)}
                    >
                      {t('web.hr.leave_requests.download')}
                    </Button>,
                    ...(details.actions.manageFiles && (!self || request.status !== 'Pending')
                      ? [
                          <Popconfirm
                            key="delete"
                            title={t('web.hr.leave_requests.delete_file_confirm')}
                            onConfirm={() =>
                              run(
                                () =>
                                  self ? deleteMyLeaveRequestFile(id, file.id) : deleteLeaveRequestFile(id, file.id),
                                'web.hr.leave_requests.file_deleted',
                              )
                            }
                          >
                            <Button
                              type="link"
                              size="small"
                              danger
                              icon={<DeleteOutlined />}
                              aria-label={t('web.common.delete')}
                            />
                          </Popconfirm>,
                        ]
                      : []),
                  ]}
                >
                  {file.fileName}
                </List.Item>
              )}
            />
          </Card>
          {request.status !== 'Draft' && request.origin !== 'Record' && request.origin !== 'Bulk' && (
            <Card size="small" title={t('web.hr.leave_requests.timeline')}>
              <ApprovalTimeline documentType={LEAVE_REQUEST_DOCUMENT} documentId={id} />
            </Card>
          )}
          {details.cancellationRequestedAt && (
            <Card size="small" title={t('web.hr.leave_requests.cancellation_timeline')}>
              <ApprovalTimeline documentType={LEAVE_CANCELLATION_DOCUMENT} documentId={id} />
            </Card>
          )}
        </Flex>
      )}
      {details && dialog === 'edit' && (
        <LeaveRequestDrawer mode="self" request={details} onClose={() => setDialog(undefined)} />
      )}
      {details && dialog === 'cancel' && (
        <ReasonDialog
          title={t('web.hr.leave_requests.cancel_title', { number: details.request.number })}
          required={
            !self && (details.request.status === 'Approved' || details.request.status === 'CancellationPending')
          }
          okText={t('web.hr.leave_requests.cancel')}
          onClose={() => setDialog(undefined)}
          onSubmit={async (reason) => {
            await (self ? cancelMyLeaveRequest(id, { reason }) : cancelLeaveRequest(id, { reason }));
            void message.success(t('web.hr.leave_requests.cancelled'));
            await refresh(id);
          }}
        />
      )}
      {details && dialog === 'cancellation' && (
        <ReasonDialog
          title={t('web.hr.leave_requests.request_cancellation')}
          required
          okText={t('web.hr.leave_requests.send')}
          onClose={() => setDialog(undefined)}
          onSubmit={async (reason) => {
            await requestMyLeaveCancellation(id, { reason });
            void message.success(t('web.hr.leave_requests.cancellation_sent'));
            await refresh(id);
          }}
        />
      )}
      {details && dialog === 'early-return' && (
        <EarlyReturnDialog
          startDate={details.request.startDate}
          endDate={details.request.endDate}
          halfDay={details.request.basis === 'WorkingDays'}
          onClose={() => setDialog(undefined)}
          onSubmit={async (values) => {
            await returnFromLeaveEarly(id, values);
            void message.success(t('web.hr.leave_requests.early_return_saved'));
            await refresh(id);
          }}
        />
      )}
    </Drawer>
  );
};
