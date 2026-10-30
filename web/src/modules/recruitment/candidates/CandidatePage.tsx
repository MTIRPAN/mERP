import { ArrowLeftOutlined, DownloadOutlined, UploadOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Descriptions,
  Flex,
  List,
  Popconfirm,
  Skeleton,
  Space,
  Table,
  Tabs,
  Tag,
  Timeline,
  Typography,
  Upload,
} from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
import {
  downloadCandidateCv,
  mergeCandidates,
  uploadCandidateCv,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { CandidateApplicationItem, CandidateDetails, CandidateTimelineItem } from '@/shared/api/generated/model';
import { saveFile } from '@/shared/api/download';
import { isApiError } from '@/shared/api/problem';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDate, formatDateTime, formatMomentDate } from '@/shared/format/date';
import { CandidateEvaluations } from '../interviews/CandidateEvaluations';
import { ApplicationStatusTag } from '../pipeline/ApplicationDrawer';
import { stageTypeColors } from '../settings/stages';
import { AddToOpeningModal } from './AddToOpeningModal';
import { CandidateFormDrawer } from './CandidateFormDrawer';
import { CandidateNotes } from './CandidateNotes';
import { CandidateConsents, PrivacyActions } from './CandidatePrivacy';
import { fullName, useCandidateRefresh, useCandidateCard } from './candidates';

const timelineColors: Record<CandidateTimelineItem['kind'], string> = {
  Applied: 'blue',
  StageChanged: 'gray',
  Note: 'green',
  Cv: 'purple',
};

/** One step of the timeline in words. */
const TimelineText = ({ item }: { item: CandidateTimelineItem }) => {
  const { t, i18n } = useTranslation();
  const what =
    item.kind === 'Applied'
      ? t('web.recruitment.candidates.timeline.applied', { opening: item.openingTitle ?? '' })
      : item.kind === 'StageChanged'
        ? t('web.recruitment.candidates.timeline.moved', {
            opening: item.openingTitle ?? '',
            from: item.fromStageName ?? '—',
            to: item.toStageName ?? '—',
          })
        : item.kind === 'Note'
          ? t('web.recruitment.candidates.timeline.note')
          : t('web.recruitment.candidates.timeline.cv', { name: item.text ?? '' });
  return (
    <Flex vertical gap={2}>
      <Typography.Text strong>{what}</Typography.Text>
      {item.text && item.kind !== 'Cv' && <Typography.Text type="secondary">{item.text}</Typography.Text>}
      <Typography.Text type="secondary">
        {formatDateTime(item.at, i18n.language)}
        {item.userName ? ` · ${item.userName}` : ''}
      </Typography.Text>
    </Flex>
  );
};

/** Possible duplicates of the candidate; those who manage the pool merge one into this candidate (D50). */
const Duplicates = ({ candidate }: { candidate: CandidateDetails }) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const refresh = useCandidateRefresh();

  const merge = async (duplicateId: string) => {
    try {
      await mergeCandidates(candidate.id, { version: candidate.version, duplicateId });
      void message.success(t('web.recruitment.candidates.merged'));
      await refresh(candidate.id);
    } catch (error) {
      void message.error(
        isApiError(error) ? (error.problem?.fieldErrors?.[0]?.message ?? error.message) : String(error),
      );
    }
  };

  return (
    <Alert
      type="warning"
      showIcon
      style={{ marginBottom: 16 }}
      title={t('web.recruitment.candidates.duplicates_title')}
      description={
        <List
          size="small"
          dataSource={candidate.duplicates}
          renderItem={(duplicate) => (
            <List.Item
              key={duplicate.id}
              actions={[
                <Popconfirm
                  key="merge"
                  title={t('web.recruitment.candidates.merge_confirm', { name: duplicate.fullName })}
                  onConfirm={() => merge(duplicate.id)}
                >
                  <Button size="small">{t('web.recruitment.candidates.merge')}</Button>
                </Popconfirm>,
              ]}
            >
              <Space wrap>
                <Typography.Link onClick={() => void navigate(`/recruitment/candidates/${duplicate.id}`)}>
                  {duplicate.fullName}
                </Typography.Link>
                <Typography.Text type="secondary">{duplicate.email}</Typography.Text>
                {duplicate.samePhone && <Tag>{t('web.recruitment.candidates.same_phone')}</Tag>}
                {duplicate.sameName && <Tag>{t('web.recruitment.candidates.same_name')}</Tag>}
              </Space>
            </List.Item>
          )}
        />
      }
    />
  );
};

/**
 * A candidate's card (PLAN R4b, D50): profile, applications, notes, CVs, consent and timeline; possible duplicates
 * with a merge. <c>team</c>: the hiring team reads it from self service and writes notes.
 */
export const CandidatePage = ({ team = false }: { team?: boolean }) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { id = '' } = useParams();
  const { data: candidate } = useCandidateCard(id, team);
  const refresh = useCandidateRefresh();
  const [dialog, setDialog] = useState<'edit' | 'opening'>();

  if (!candidate) {
    return <Skeleton active />;
  }

  const dash = '—';
  const profile = candidate.profile;
  // An anonymized candidate keeps only what the figures need; nothing is changed any more (D55)
  const editable = candidate.canManage && !candidate.anonymizedAt;
  const pipelineOf = (item: CandidateApplicationItem) =>
    team
      ? `/recruitment/hiring/${item.application.openingId}`
      : `/recruitment/openings/${item.application.openingId}/pipeline`;
  const link = (url: string | null) =>
    url ? (
      <Typography.Link href={url} target="_blank" rel="noopener noreferrer nofollow">
        {url}
      </Typography.Link>
    ) : (
      dash
    );

  return (
    <>
      <PageHeader
        title={fullName(candidate)}
        subtitle={
          <Space size={[4, 4]} wrap>
            {[profile.currentTitle, profile.currentCompany].filter(Boolean).join(' · ')}
            {candidate.isEmployee && <Tag color="blue">{t('web.recruitment.pipeline.internal')}</Tag>}
            {candidate.tags.map((tag) => (
              <Tag key={tag.code}>{tag.name}</Tag>
            ))}
          </Space>
        }
        extra={
          <Space wrap>
            <Button
              icon={<ArrowLeftOutlined />}
              onClick={() => void navigate(team ? '/recruitment/my-openings' : '/recruitment/candidates')}
            >
              {t(team ? 'web.recruitment.pipeline.back_to_mine' : 'web.recruitment.candidates.back')}
            </Button>
            {candidate.canManagePrivacy && <PrivacyActions candidate={candidate} />}
            {editable && (
              <>
                <Button onClick={() => setDialog('edit')}>{t('web.common.edit')}</Button>
                <Upload
                  accept=".pdf,.docx"
                  showUploadList={false}
                  customRequest={({ file, onSuccess, onError }) => {
                    void uploadCandidateCv(candidate.id, { cv: file as File }).then(
                      async (result) => {
                        onSuccess?.(result);
                        void message.success(t('web.recruitment.candidates.cv_uploaded'));
                        await refresh(candidate.id);
                      },
                      (error: unknown) => {
                        onError?.(error as Error);
                        void message.error(
                          isApiError(error)
                            ? (error.problem?.fieldErrors?.[0]?.message ?? error.message)
                            : String(error),
                        );
                      },
                    );
                  }}
                >
                  <Button icon={<UploadOutlined />}>{t('web.recruitment.candidates.upload_cv')}</Button>
                </Upload>
              </>
            )}
            {candidate.canAddToOpening && !candidate.anonymizedAt && (
              <Button type="primary" onClick={() => setDialog('opening')}>
                {t('web.recruitment.candidates.add_to_opening')}
              </Button>
            )}
          </Space>
        }
      />
      {candidate.anonymizedAt && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('web.recruitment.candidates.privacy.anonymized', {
            at: formatDateTime(candidate.anonymizedAt, i18n.language),
          })}
        />
      )}
      {editable && candidate.duplicates.length > 0 && <Duplicates candidate={candidate} />}
      <Tabs
        items={[
          {
            key: 'profile',
            label: t('web.recruitment.candidates.tabs.profile'),
            children: (
              <Descriptions
                size="small"
                bordered
                column={1}
                items={[
                  { key: 'email', label: t('web.recruitment.candidates.fields.email'), children: candidate.email },
                  ...(candidate.anonymizedAt
                    ? []
                    : [
                        {
                          key: 'retention',
                          label: t('web.recruitment.candidates.fields.retention'),
                          children: candidate.retentionEndsOn
                            ? t('web.recruitment.candidates.privacy.retention_until', {
                                date: formatDate(candidate.retentionEndsOn, i18n.language),
                              })
                            : t('web.recruitment.candidates.privacy.retention_paused'),
                        },
                      ]),
                  {
                    key: 'phone',
                    label: t('web.recruitment.candidates.fields.phone'),
                    children: candidate.phone ?? dash,
                  },
                  {
                    key: 'source',
                    label: t('web.recruitment.candidates.fields.source'),
                    children: candidate.referrerName
                      ? t('web.recruitment.candidates.referred_by', {
                          source: candidate.sourceName ?? candidate.source,
                          name: candidate.referrerName,
                        })
                      : (candidate.sourceName ?? candidate.source),
                  },
                  {
                    key: 'location',
                    label: t('web.recruitment.candidates.fields.location'),
                    children: profile.location ?? dash,
                  },
                  {
                    key: 'experience',
                    label: t('web.recruitment.candidates.fields.experience_years'),
                    children:
                      profile.experienceYears != null
                        ? t('web.recruitment.candidates.years', {
                            years: new Intl.NumberFormat(i18n.language).format(profile.experienceYears),
                          })
                        : dash,
                  },
                  {
                    key: 'education',
                    label: t('web.recruitment.candidates.fields.education'),
                    children: profile.education ?? dash,
                  },
                  {
                    key: 'linkedin',
                    label: t('web.recruitment.candidates.fields.linkedin'),
                    children: link(profile.linkedInUrl),
                  },
                  {
                    key: 'portfolio',
                    label: t('web.recruitment.candidates.fields.portfolio'),
                    children: link(profile.portfolioUrl),
                  },
                  {
                    key: 'consent',
                    label: t('web.recruitment.candidates.fields.pool_consent'),
                    children:
                      candidate.poolConsentRevision != null && candidate.poolConsentAt
                        ? t('web.recruitment.candidates.pool_consent_given', {
                            revision: candidate.poolConsentRevision,
                            at: formatDateTime(candidate.poolConsentAt, i18n.language),
                          })
                        : t('web.recruitment.candidates.pool_consent_none'),
                  },
                ]}
              />
            ),
          },
          {
            key: 'applications',
            label: t('web.recruitment.candidates.tabs.applications', { count: candidate.applications.length }),
            children: (
              <Table<CandidateApplicationItem>
                rowKey={(row) => row.application.id}
                dataSource={candidate.applications}
                pagination={false}
                columns={[
                  {
                    key: 'opening',
                    title: t('web.recruitment.candidates.fields.opening'),
                    render: (_, row) => (
                      <Typography.Link onClick={() => void navigate(pipelineOf(row))}>
                        {row.application.openingTitle}
                      </Typography.Link>
                    ),
                  },
                  {
                    key: 'stage',
                    title: t('web.recruitment.pipeline.fields.stage'),
                    render: (_, row) =>
                      row.application.stageType ? (
                        <Tag color={stageTypeColors[row.application.stageType]}>{row.application.stageName}</Tag>
                      ) : (
                        dash
                      ),
                  },
                  {
                    key: 'status',
                    title: t('web.recruitment.pipeline.fields.status'),
                    render: (_, row) => <ApplicationStatusTag status={row.application.status} />,
                  },
                  {
                    key: 'appliedAt',
                    title: t('web.recruitment.pipeline.fields.applied_at'),
                    render: (_, row) => formatMomentDate(row.application.appliedAt, i18n.language),
                  },
                  {
                    key: 'consent',
                    title: t('web.recruitment.pipeline.fields.consent'),
                    render: (_, row) =>
                      row.consent
                        ? t('web.recruitment.candidates.disclosure_revision', {
                            revision: row.consent.disclosureRevision,
                          })
                        : t('web.recruitment.candidates.added_by_hr'),
                  },
                ]}
              />
            ),
          },
          {
            key: 'consents',
            label: t('web.recruitment.candidates.tabs.consents', { count: candidate.consents.length }),
            children: <CandidateConsents consents={candidate.consents} />,
          },
          {
            key: 'evaluations',
            label: t('web.recruitment.candidates.tabs.evaluations', { count: candidate.evaluations.length }),
            children: <CandidateEvaluations evaluations={candidate.evaluations} />,
          },
          {
            key: 'notes',
            label: t('web.recruitment.candidates.tabs.notes', { count: candidate.notes.length }),
            children: (
              <CandidateNotes
                candidateId={candidate.id}
                notes={candidate.notes}
                team={team}
                canWrite={!candidate.anonymizedAt && (team || candidate.canManage)}
              />
            ),
          },
          ...(team
            ? []
            : [
                {
                  key: 'files',
                  label: t('web.recruitment.candidates.tabs.files', { count: candidate.files.length }),
                  children: (
                    <List
                      dataSource={candidate.files}
                      locale={{ emptyText: t('web.recruitment.candidates.no_files') }}
                      renderItem={(file) => (
                        <List.Item
                          key={file.id}
                          actions={[
                            <Button
                              key="download"
                              type="text"
                              icon={<DownloadOutlined />}
                              aria-label={t('web.recruitment.candidates.download', { name: file.fileName })}
                              onClick={() =>
                                void saveFile(downloadCandidateCv(candidate.id, file.id), file.fileName).catch(
                                  () => undefined,
                                )
                              }
                            />,
                          ]}
                        >
                          <Space wrap>
                            {file.fileName}
                            {file.isLatest && <Tag color="green">{t('web.recruitment.candidates.latest_cv')}</Tag>}
                            <Typography.Text type="secondary">
                              {formatDateTime(file.uploadedAt, i18n.language)}
                            </Typography.Text>
                          </Space>
                        </List.Item>
                      )}
                    />
                  ),
                },
              ]),
          {
            key: 'timeline',
            label: t('web.recruitment.candidates.tabs.timeline'),
            children: (
              <Timeline
                items={candidate.timeline.map((item, index) => ({
                  key: index,
                  color: timelineColors[item.kind],
                  content: <TimelineText item={item} />,
                }))}
              />
            ),
          },
        ]}
      />
      {dialog === 'edit' && <CandidateFormDrawer candidate={candidate} onClose={() => setDialog(undefined)} />}
      {dialog === 'opening' && (
        <AddToOpeningModal
          candidateId={candidate.id}
          candidateName={fullName(candidate)}
          appliedTo={candidate.applications.map((a) => a.application.openingId)}
          onClose={() => setDialog(undefined)}
          onAdded={(_, openingId) => {
            void message.success(t('web.recruitment.candidates.added_to_opening'));
            void refresh(candidate.id);
            void navigate(`/recruitment/openings/${openingId}/pipeline`);
          }}
        />
      )}
    </>
  );
};

/** A candidate of an opening of the caller's hiring team (D50). */
export const HiringCandidatePage = () => <CandidatePage team />;
