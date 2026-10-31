import { CheckCircleTwoTone, DownloadOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  Descriptions,
  Drawer,
  Flex,
  Input,
  Modal,
  Popconfirm,
  Skeleton,
  Space,
  Tag,
  Typography,
} from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  cancelInterview,
  closeInterview,
  downloadInterviewCv,
  summarizeInterviewFeedback,
  useGetInterview,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { InterviewStatus } from '@/shared/api/generated/model';
import { saveFile } from '@/shared/api/download';
import { isApiError } from '@/shared/api/problem';
import { formatDateTime } from '@/shared/format/date';
import { FeedbackForm } from './FeedbackForm';
import { FeedbackList } from './FeedbackList';
import { FeedbackSummaryPanel, InterviewQuestionsPanel } from './InterviewAi';
import { InterviewModal } from './InterviewModal';
import { recruitmentAiFeatures } from '../permissions';
import { interviewStatusColors, useInterviewRefresh, useRecruitmentAi } from './interviews';

export const InterviewStatusTag = ({ status }: { status: InterviewStatus }) => {
  const { t } = useTranslation();
  return <Tag color={interviewStatusColors[status]}>{t(`web.recruitment.interviews.status.${status}`)}</Tag>;
};

/** A link shown only when it is a web address; anything else is plain text. */
const SafeLink = ({ href }: { href: string }) =>
  /^https?:\/\//i.test(href) ? (
    <Typography.Link href={href} target="_blank" rel="noopener noreferrer">
      {href}
    </Typography.Link>
  ) : (
    <>{href}</>
  );

/**
 * An interview (PLAN R5, D52): its time, place and interviewers, the candidate's CV, the reader's evaluation form and
 * the forms the blind rule lets them see; those who manage interviews change, cancel or close it here.
 */
export const InterviewDrawer = ({ id, onClose }: { id: string; onClose: () => void }) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const { data: details, refetch } = useGetInterview(id);
  const refresh = useInterviewRefresh();
  const questionsAi = useRecruitmentAi(recruitmentAiFeatures.interviewQuestions);
  const summaryAi = useRecruitmentAi(recruitmentAiFeatures.feedbackSummary);
  const [editing, setEditing] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [reason, setReason] = useState('');
  const interview = details?.interview;
  const dash = '—';

  const run = async (action: (version: number) => Promise<unknown>) => {
    if (!details || !interview) {
      return;
    }

    try {
      await action(details.version);
      await refresh(id, interview.applicationId);
    } catch (error) {
      const fields = isApiError(error) ? (error.problem?.fieldErrors ?? []) : [];
      void message.error(
        fields.length > 0 ? fields.map((e) => e.message).join(' ') : isApiError(error) ? error.message : String(error),
      );
    }
  };

  const footer = details?.canManage && interview?.status === 'Scheduled' && (
    <Flex justify="flex-end" gap={8} wrap>
      <Button onClick={() => setEditing(true)}>{t('web.common.edit')}</Button>
      <Popconfirm
        title={t('web.recruitment.interviews.no_show_confirm')}
        onConfirm={() => run((version) => closeInterview(id, { version, status: 'NoShow' }))}
      >
        <Button>{t('web.recruitment.interviews.no_show')}</Button>
      </Popconfirm>
      <Button danger onClick={() => setCancelling(true)}>
        {t('web.recruitment.interviews.cancel')}
      </Button>
      <Popconfirm
        title={t('web.recruitment.interviews.complete_confirm')}
        onConfirm={() => run((version) => closeInterview(id, { version, status: 'Completed' }))}
      >
        <Button type="primary">{t('web.recruitment.interviews.complete')}</Button>
      </Popconfirm>
    </Flex>
  );

  return (
    <Drawer
      title={
        interview ? `${interview.candidateName} · ${interview.openingTitle}` : t('web.recruitment.interviews.title')
      }
      open
      onClose={onClose}
      size={720}
      footer={footer}
    >
      {!details || !interview ? (
        <Skeleton active />
      ) : (
        <Flex vertical gap={16}>
          <Descriptions
            size="small"
            bordered
            column={1}
            items={[
              {
                key: 'status',
                label: t('web.recruitment.interviews.fields.status'),
                children: (
                  <Space wrap>
                    <InterviewStatusTag status={interview.status} />
                    {details.cancelReason && <Typography.Text type="secondary">{details.cancelReason}</Typography.Text>}
                  </Space>
                ),
              },
              {
                key: 'type',
                label: t('web.recruitment.interviews.fields.type'),
                children: t(`web.recruitment.interviews.types.${interview.type}`),
              },
              {
                key: 'when',
                label: t('web.recruitment.interviews.fields.when'),
                children: `${formatDateTime(interview.startsAt, i18n.language)} – ${formatDateTime(interview.endsAt, i18n.language)}`,
              },
              ...(interview.location
                ? [
                    {
                      key: 'location',
                      label: t('web.recruitment.interviews.fields.location'),
                      children: interview.location,
                    },
                  ]
                : []),
              ...(interview.onlineLink
                ? [
                    {
                      key: 'link',
                      label: t('web.recruitment.interviews.fields.online_link'),
                      children: <SafeLink href={interview.onlineLink} />,
                    },
                  ]
                : []),
              {
                key: 'interviewers',
                label: t('web.recruitment.interviews.fields.interviewers'),
                children: (
                  <Flex vertical gap={2}>
                    {interview.interviewers.map((person) => (
                      <span key={person.userId}>{person.name ?? person.userId}</span>
                    ))}
                  </Flex>
                ),
              },
              {
                key: 'organizer',
                label: t('web.recruitment.interviews.fields.organizer'),
                children: details.organizer.name ?? dash,
              },
              ...(details.notes
                ? [
                    {
                      key: 'notes',
                      label: t('web.recruitment.interviews.fields.notes'),
                      children: <span style={{ whiteSpace: 'pre-wrap' }}>{details.notes}</span>,
                    },
                  ]
                : []),
            ]}
          />
          {details.cvFileName && (
            <Button
              icon={<DownloadOutlined />}
              style={{ alignSelf: 'flex-start' }}
              onClick={() => void saveFile(downloadInterviewCv(id), details.cvFileName!).catch(() => undefined)}
            >
              {t('web.recruitment.interviews.download_cv', { name: details.cvFileName })}
            </Button>
          )}
          {questionsAi && interview.status === 'Scheduled' && <InterviewQuestionsPanel interviewId={id} />}
          {interview.myFeedbackSent && (
            <Typography.Text>
              <CheckCircleTwoTone twoToneColor="#52c41a" /> {t('web.recruitment.interviews.feedback.mine_sent')}
            </Typography.Text>
          )}
          {details.canSubmitFeedback && details.evaluation && (
            <FeedbackForm
              key={details.myFeedback?.id ?? 'new'}
              interviewId={id}
              form={details.evaluation}
              mine={details.myFeedback}
              onSent={() => Promise.all([refetch(), refresh(id, interview.applicationId)])}
            />
          )}
          {!details.evaluation && (
            <Typography.Text type="secondary">{t('web.recruitment.interviews.no_form')}</Typography.Text>
          )}
          {summaryAi && !details.feedbackHidden && details.feedback.length > 0 && (
            <FeedbackSummaryPanel
              key={id}
              pending={details.feedbackPending ?? 0}
              summarize={() => summarizeInterviewFeedback(id)}
            />
          )}
          <FeedbackList feedback={details.feedback} score={details.applicationScore} hidden={details.feedbackHidden} />
        </Flex>
      )}
      {editing && details && interview && (
        <InterviewModal
          applicationId={interview.applicationId}
          candidateName={interview.candidateName}
          openingTitle={interview.openingTitle}
          interview={details}
          onClose={() => setEditing(false)}
        />
      )}
      <Modal
        title={t('web.recruitment.interviews.cancel')}
        open={cancelling}
        okText={t('web.recruitment.interviews.send_cancellation')}
        okButtonProps={{ danger: true }}
        cancelText={t('web.common.cancel')}
        onCancel={() => setCancelling(false)}
        onOk={() =>
          void run((version) => cancelInterview(id, { version, reason: reason.trim() || null })).then(() => {
            setCancelling(false);
            setReason('');
          })
        }
      >
        <Typography.Paragraph type="secondary">{t('web.recruitment.interviews.cancel_hint')}</Typography.Paragraph>
        <Input.TextArea
          aria-label={t('web.recruitment.interviews.fields.cancel_reason')}
          placeholder={t('web.recruitment.interviews.fields.cancel_reason')}
          rows={3}
          maxLength={500}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
      </Modal>
    </Drawer>
  );
};
