import { DownloadOutlined, IdcardOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  Card,
  Descriptions,
  Drawer,
  Flex,
  List,
  Popconfirm,
  Select,
  Skeleton,
  Space,
  Tag,
  Timeline,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import type { ApplicationListItem, ApplicationStatus, OpeningPipeline } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { formatDateTime } from '@/shared/format/date';
import { YES, NO } from '../openings/openings';
import { recruitmentPermissions } from '../permissions';
import { applicationStatusColors, downloadCv, needsReason, useApplicationDetails } from './pipeline';

export const ApplicationStatusTag = ({ status }: { status: ApplicationStatus }) => {
  const { t } = useTranslation();
  return <Tag color={applicationStatusColors[status]}>{t(`web.recruitment.pipeline.status.${status}`)}</Tag>;
};

interface ApplicationDrawerProps {
  id: string;
  pipeline: OpeningPipeline;
  /** From self service: the hiring team only reads. */
  team: boolean;
  onMove: (stageId: string) => Promise<unknown>;
  onLeave: (kind: 'reject' | 'withdraw', application: ApplicationListItem) => void;
  onReopen: () => Promise<unknown>;
  onClose: () => void;
}

/**
 * An application (PLAN R4a, D50): the candidate's contact, the answers, the CV, the KVKK consent and every step through
 * the pipeline; those who manage applications move, reject, withdraw and reopen it here too.
 */
export const ApplicationDrawer = ({
  id,
  pipeline,
  team,
  onMove,
  onLeave,
  onReopen,
  onClose,
}: ApplicationDrawerProps) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const { data: details } = useApplicationDetails(id, team);
  const navigate = useNavigate();
  const readsCandidates = useHasPermission(recruitmentPermissions.candidatesRead);
  const application = details?.application;
  const dash = '—';

  const run = async (action: () => Promise<unknown>) => {
    try {
      await action();
    } catch (error) {
      const fields = isApiError(error) ? (error.problem?.fieldErrors ?? []) : [];
      void message.error(
        fields.length > 0 ? fields.map((e) => e.message).join(' ') : isApiError(error) ? error.message : String(error),
      );
    }
  };

  const answer = (value: string) =>
    value === YES || value === NO ? t(`web.recruitment.openings.answers.${value}`) : value;
  const active = application?.status === 'Active';
  const footer = details?.canManage && application && (
    <Flex justify="flex-end" gap={8} wrap>
      {active && (
        <>
          <Select
            aria-label={t('web.recruitment.pipeline.move_to')}
            placeholder={t('web.recruitment.pipeline.move_to')}
            style={{ minWidth: 200 }}
            value={null}
            onChange={(stageId: string) => void run(() => onMove(stageId))}
            options={pipeline.stages
              .filter((s) => s.id !== application.stageId && !needsReason(s.type))
              .map((s) => ({ value: s.id, label: s.name }))}
          />
          <Button onClick={() => onLeave('withdraw', application)}>{t('web.recruitment.pipeline.withdraw.ok')}</Button>
          <Button danger onClick={() => onLeave('reject', application)}>
            {t('web.recruitment.pipeline.reject.ok')}
          </Button>
        </>
      )}
      {(application.status === 'Rejected' || application.status === 'Withdrawn') && (
        <Popconfirm title={t('web.recruitment.pipeline.reopen_confirm')} onConfirm={() => run(onReopen)}>
          <Button>{t('web.recruitment.pipeline.reopen')}</Button>
        </Popconfirm>
      )}
    </Flex>
  );

  return (
    <Drawer
      title={application?.candidateName ?? t('web.recruitment.pipeline.application')}
      open
      onClose={onClose}
      size={720}
      footer={footer}
    >
      {!details || !application ? (
        <Skeleton active />
      ) : (
        <Flex vertical gap={16}>
          {(team || readsCandidates) && (
            <Button
              icon={<IdcardOutlined />}
              style={{ alignSelf: 'flex-start' }}
              onClick={() =>
                void navigate(
                  team
                    ? `/recruitment/hiring/candidates/${application.candidateId}`
                    : `/recruitment/candidates/${application.candidateId}`,
                )
              }
            >
              {t('web.recruitment.pipeline.candidate_card')}
            </Button>
          )}
          <Descriptions
            size="small"
            bordered
            column={1}
            items={[
              {
                key: 'stage',
                label: t('web.recruitment.pipeline.fields.stage'),
                children: (
                  <Space wrap>
                    {application.stageName ?? dash}
                    <ApplicationStatusTag status={application.status} />
                    {details.reasonName && <Typography.Text type="secondary">{details.reasonName}</Typography.Text>}
                  </Space>
                ),
              },
              { key: 'email', label: t('web.recruitment.pipeline.fields.email'), children: application.email },
              { key: 'phone', label: t('web.recruitment.pipeline.fields.phone'), children: application.phone ?? dash },
              {
                key: 'applied',
                label: t('web.recruitment.pipeline.fields.applied_at'),
                children: formatDateTime(application.appliedAt, i18n.language),
              },
              {
                key: 'source',
                label: t('web.recruitment.pipeline.fields.source'),
                children: (
                  <Space wrap>
                    {application.source}
                    {application.isInternal && <Tag color="blue">{t('web.recruitment.pipeline.internal')}</Tag>}
                    {application.knockedOut && <Tag color="warning">{t('web.recruitment.pipeline.knocked_out')}</Tag>}
                  </Space>
                ),
              },
              ...(details.rejectionEmailDueAt || details.rejectionEmailSentAt
                ? [
                    {
                      key: 'email-status',
                      label: t('web.recruitment.pipeline.fields.rejection_email'),
                      children: details.rejectionEmailSentAt
                        ? t('web.recruitment.pipeline.email_sent', {
                            at: formatDateTime(details.rejectionEmailSentAt, i18n.language),
                          })
                        : t('web.recruitment.pipeline.email_due', {
                            at: formatDateTime(details.rejectionEmailDueAt!, i18n.language),
                          }),
                    },
                  ]
                : []),
              {
                key: 'consent',
                label: t('web.recruitment.pipeline.fields.consent'),
                children: details.consent
                  ? t(
                      details.consent.poolConsentRevision != null
                        ? 'web.recruitment.pipeline.consent_pool'
                        : 'web.recruitment.pipeline.consent_disclosure',
                      { revision: details.consent.disclosureRevision },
                    )
                  : t('web.recruitment.candidates.added_by_hr'),
              },
            ]}
          />
          {details.cv && (
            <Button
              icon={<DownloadOutlined />}
              onClick={() => void downloadCv(id, team, details.cv!.fileName).catch(() => undefined)}
              style={{ alignSelf: 'flex-start' }}
            >
              {t('web.recruitment.pipeline.download_cv', { name: details.cv.fileName })}
            </Button>
          )}
          {details.coverLetter && (
            <Card size="small" title={t('web.recruitment.pipeline.fields.cover_letter')}>
              <Typography.Paragraph style={{ whiteSpace: 'pre-wrap', marginBottom: 0 }}>
                {details.coverLetter}
              </Typography.Paragraph>
            </Card>
          )}
          <Card size="small" title={t('web.recruitment.pipeline.answers')}>
            <List
              size="small"
              dataSource={details.answers}
              locale={{ emptyText: t('web.recruitment.pipeline.no_answers') }}
              renderItem={(item) => (
                <List.Item>
                  <Flex vertical gap={2}>
                    <Typography.Text type="secondary">{item.question ?? dash}</Typography.Text>
                    <Typography.Text type={item.missed ? 'warning' : undefined}>
                      {item.values.map(answer).join(', ')}
                    </Typography.Text>
                  </Flex>
                </List.Item>
              )}
            />
          </Card>
          <Card size="small" title={t('web.recruitment.pipeline.history')}>
            <Timeline
              items={details.history.map((step, index) => ({
                key: index,
                color: step.status === 'Rejected' ? 'red' : step.status === 'Hired' ? 'green' : 'blue',
                content: (
                  <Flex vertical gap={2}>
                    <Typography.Text strong>
                      {step.fromStageId
                        ? t('web.recruitment.pipeline.moved', {
                            from: step.fromStageName ?? dash,
                            to: step.toStageName ?? dash,
                          })
                        : t('web.recruitment.pipeline.applied', { stage: step.toStageName ?? dash })}
                    </Typography.Text>
                    {step.reasonName && <Typography.Text>{step.reasonName}</Typography.Text>}
                    {step.note && <Typography.Text type="secondary">{step.note}</Typography.Text>}
                    <Typography.Text type="secondary">
                      {formatDateTime(step.at, i18n.language)}
                      {step.userName ? ` · ${step.userName}` : ''}
                    </Typography.Text>
                  </Flex>
                ),
              }))}
            />
          </Card>
        </Flex>
      )}
    </Drawer>
  );
};
