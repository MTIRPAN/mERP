import { DownloadOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Descriptions,
  Drawer,
  Flex,
  Input,
  Modal,
  Popconfirm,
  Radio,
  Skeleton,
  Space,
  Tag,
  Typography,
} from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  downloadMyHiringOfferLetter,
  downloadOfferLetter,
  recordOfferResponse,
  sendOffer,
  submitOffer,
  withdrawOffer,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { OfferStatus } from '@/shared/api/generated/model';
import { saveFile } from '@/shared/api/download';
import { isApiError } from '@/shared/api/problem';
import { ApprovalTimeline } from '@/shared/approvals/ApprovalTimeline';
import { formatDate, formatDateTime } from '@/shared/format/date';
import { OfferFormModal } from './OfferFormModal';
import { OFFER_DOCUMENT, formatMoney, offerStatusColors, useOfferDetails, useOfferRefresh } from './offers';

export const OfferStatusTag = ({ status }: { status: OfferStatus }) => {
  const { t } = useTranslation();
  return <Tag color={offerStatusColors[status]}>{t(`web.recruitment.offers.status.${status}`)}</Tag>;
};

type Dialog = 'edit' | 'response' | 'withdraw';

interface OfferDrawerProps {
  id: string;
  /** From self service: the hiring manager reads it, the salary only as an approver. */
  team?: boolean;
  onClose: () => void;
}

/**
 * An offer (PLAN R6, D53): what is offered, the salary for those who may see it with the requisition's budget, the
 * approval timeline and what the caller may do now: correct, send for approval, send to the candidate, record the
 * answer or withdraw.
 */
export const OfferDrawer = ({ id, team = false, onClose }: OfferDrawerProps) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const { data: details } = useOfferDetails(id, team);
  const refresh = useOfferRefresh();
  const [dialog, setDialog] = useState<Dialog>();
  const [accepted, setAccepted] = useState(true);
  const [note, setNote] = useState('');
  const offer = details?.offer;
  const dash = '—';

  const run = async (action: (version: number) => Promise<unknown>, done?: string) => {
    if (!details || !offer) {
      return false;
    }

    try {
      await action(details.version);
      if (done) {
        void message.success(t(done));
      }

      await refresh(id, offer.applicationId);
      return true;
    } catch (error) {
      const fields = isApiError(error) ? (error.problem?.fieldErrors ?? []) : [];
      void message.error(
        fields.length > 0 ? fields.map((e) => e.message).join(' ') : isApiError(error) ? error.message : String(error),
      );
      return false;
    }
  };

  const close = () => {
    setDialog(undefined);
    setNote('');
    setAccepted(true);
  };

  const letter = () =>
    void saveFile(
      team ? downloadMyHiringOfferLetter(id) : downloadOfferLetter(id),
      t('web.recruitment.offers.letter_file', { number: offer?.number }),
    ).catch(() => undefined);

  const footer = details &&
    offer &&
    !team &&
    (details.canEdit || details.canSend || details.canRecordResponse || details.canWithdraw) && (
      <Flex justify="flex-end" gap={8} wrap>
        {details.canWithdraw && (
          <Button danger onClick={() => setDialog('withdraw')}>
            {t('web.recruitment.offers.withdraw')}
          </Button>
        )}
        {details.canEdit && <Button onClick={() => setDialog('edit')}>{t('web.common.edit')}</Button>}
        {details.canEdit && (
          <Popconfirm
            title={t('web.recruitment.offers.submit_confirm')}
            onConfirm={() => run((version) => submitOffer(id, { version }), 'web.recruitment.offers.submitted')}
          >
            <Button type="primary">{t('web.recruitment.offers.submit')}</Button>
          </Popconfirm>
        )}
        {details.canSend && (
          <Popconfirm
            title={t('web.recruitment.offers.send_confirm')}
            onConfirm={() => run((version) => sendOffer(id, { version }), 'web.recruitment.offers.sent')}
          >
            <Button type="primary">{t('web.recruitment.offers.send')}</Button>
          </Popconfirm>
        )}
        {details.canRecordResponse && (
          <Button type="primary" onClick={() => setDialog('response')}>
            {t('web.recruitment.offers.record_response')}
          </Button>
        )}
      </Flex>
    );

  return (
    <Drawer
      title={offer ? `${offer.number} · ${offer.candidateName}` : t('web.recruitment.offers.title')}
      open
      onClose={onClose}
      size={720}
      footer={footer}
    >
      {!details || !offer ? (
        <Skeleton active />
      ) : (
        <Flex vertical gap={16}>
          {offer.status === 'Returned' && details.returnComment && (
            <Alert
              type="warning"
              showIcon
              title={t('web.recruitment.offers.returned')}
              description={details.returnComment}
            />
          )}
          <Descriptions
            size="small"
            bordered
            column={1}
            items={[
              {
                key: 'status',
                label: t('web.recruitment.offers.fields.status'),
                children: (
                  <Space wrap>
                    <OfferStatusTag status={offer.status} />
                    {offer.isAboveBudget && <Tag color="warning">{t('web.recruitment.offers.above_budget')}</Tag>}
                  </Space>
                ),
              },
              {
                key: 'candidate',
                label: t('web.recruitment.offers.fields.candidate'),
                children: `${offer.candidateName} · ${offer.openingTitle}`,
              },
              {
                key: 'position',
                label: t('web.recruitment.offers.fields.position'),
                children: details.grade ? `${offer.positionTitle} (${details.grade})` : offer.positionTitle,
              },
              {
                key: 'salary',
                label: t('web.recruitment.offers.fields.salary'),
                children:
                  details.salaryVisible && details.salary != null && details.currency ? (
                    <Flex vertical gap={2}>
                      <Typography.Text strong>
                        {formatMoney(details.salary, details.currency, i18n.language)} ·{' '}
                        {t(`web.recruitment.offers.periods.${details.period ?? 'Monthly'}`)}
                      </Typography.Text>
                      {details.budgetMax != null && details.budgetCurrency && (
                        <Typography.Text type="secondary">
                          {t('web.recruitment.offers.budget', {
                            max: formatMoney(details.budgetMax, details.budgetCurrency, i18n.language),
                          })}
                        </Typography.Text>
                      )}
                    </Flex>
                  ) : (
                    <Typography.Text type="secondary">{t('web.recruitment.offers.salary_hidden')}</Typography.Text>
                  ),
              },
              {
                key: 'start',
                label: t('web.recruitment.offers.fields.start_date'),
                children: formatDate(offer.startDate, i18n.language),
              },
              {
                key: 'expires',
                label: t('web.recruitment.offers.fields.expires_on'),
                children: formatDate(offer.expiresOn, i18n.language),
              },
              ...(details.benefits
                ? [
                    {
                      key: 'benefits',
                      label: t('web.recruitment.offers.fields.benefits'),
                      children: <span style={{ whiteSpace: 'pre-wrap' }}>{details.benefits}</span>,
                    },
                  ]
                : []),
              {
                key: 'people',
                label: t('web.recruitment.offers.fields.people'),
                children: t('web.recruitment.offers.people', {
                  requester: details.requesterName ?? dash,
                  manager: details.hiringManagerName ?? dash,
                }),
              },
              ...(offer.sentAt
                ? [
                    {
                      key: 'sent',
                      label: t('web.recruitment.offers.fields.sent_at'),
                      children: formatDateTime(offer.sentAt, i18n.language),
                    },
                  ]
                : []),
              ...(offer.respondedAt
                ? [
                    {
                      key: 'response',
                      label: t('web.recruitment.offers.fields.response'),
                      children: (
                        <Flex vertical gap={2}>
                          <span>
                            {t(`web.recruitment.offers.channels.${details.respondedVia ?? 'Hr'}`, {
                              name: details.respondedByName ?? dash,
                            })}{' '}
                            · {formatDateTime(offer.respondedAt, i18n.language)}
                          </span>
                          {details.responseNote && (
                            <Typography.Text type="secondary">{details.responseNote}</Typography.Text>
                          )}
                        </Flex>
                      ),
                    },
                  ]
                : []),
              ...(details.withdrawalReason
                ? [
                    {
                      key: 'withdrawal',
                      label: t('web.recruitment.offers.fields.withdrawal_reason'),
                      children: details.withdrawalReason,
                    },
                  ]
                : []),
            ]}
          />
          {details.salaryVisible && (
            <Button icon={<DownloadOutlined />} style={{ alignSelf: 'flex-start' }} onClick={letter}>
              {t(offer.sentAt ? 'web.recruitment.offers.letter' : 'web.recruitment.offers.letter_preview')}
            </Button>
          )}
          {offer.status !== 'Draft' && <ApprovalTimeline documentType={OFFER_DOCUMENT} documentId={id} />}
        </Flex>
      )}
      {dialog === 'edit' && details && offer && (
        <OfferFormModal
          applicationId={offer.applicationId}
          openingTitle={offer.openingTitle}
          offer={details}
          onClose={close}
        />
      )}
      <Modal
        title={t('web.recruitment.offers.record_response')}
        open={dialog === 'response'}
        okText={t('web.common.save')}
        cancelText={t('web.common.cancel')}
        onCancel={close}
        onOk={() =>
          void run(
            (version) => recordOfferResponse(id, { version, accepted, note: note.trim() || null }),
            'web.recruitment.offers.response_saved',
          ).then((ok) => ok && close())
        }
      >
        <Flex vertical gap={12}>
          <Radio.Group
            value={accepted}
            onChange={(e) => setAccepted(e.target.value as boolean)}
            options={[
              { value: true, label: t('web.recruitment.offers.answers.accepted') },
              { value: false, label: t('web.recruitment.offers.answers.declined') },
            ]}
          />
          <Input.TextArea
            aria-label={t('web.recruitment.offers.fields.note')}
            placeholder={t('web.recruitment.offers.fields.note')}
            rows={3}
            maxLength={500}
            value={note}
            onChange={(e) => setNote(e.target.value)}
          />
        </Flex>
      </Modal>
      <Modal
        title={t('web.recruitment.offers.withdraw')}
        open={dialog === 'withdraw'}
        okText={t('web.recruitment.offers.withdraw')}
        okButtonProps={{ danger: true }}
        cancelText={t('web.common.cancel')}
        onCancel={close}
        onOk={() =>
          void run(
            (version) => withdrawOffer(id, { version, reason: note.trim() || null }),
            'web.recruitment.offers.withdrawn',
          ).then((ok) => ok && close())
        }
      >
        <Input.TextArea
          aria-label={t('web.recruitment.offers.fields.withdrawal_reason')}
          placeholder={t('web.recruitment.offers.fields.withdrawal_reason')}
          rows={3}
          maxLength={500}
          value={note}
          onChange={(e) => setNote(e.target.value)}
        />
      </Modal>
    </Drawer>
  );
};
