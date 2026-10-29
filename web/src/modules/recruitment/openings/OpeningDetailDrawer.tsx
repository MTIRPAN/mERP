import {
  App,
  Button,
  Card,
  Descriptions,
  Drawer,
  Flex,
  Form,
  Input,
  List,
  Modal,
  Skeleton,
  Space,
  Tag,
  Typography,
} from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import {
  closeOpening,
  holdOpening,
  publishOpening,
  useGetOpening,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { OpeningStatus } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { RichTextView } from '@/shared/components/RichTextEditor';
import { formatDate, formatDateTime } from '@/shared/format/date';
import { recruitmentPermissions } from '../permissions';
import { OpeningDrawer } from './OpeningDrawer';
import { OpeningStagesDrawer } from './OpeningStagesDrawer';
import { stageTypeColors } from '../settings/stages';
import { NO, openingStatusColors, useOpeningRefresh, YES } from './openings';

export const OpeningStatusTag = ({ status }: { status: OpeningStatus }) => {
  const { t } = useTranslation();
  return <Tag color={openingStatusColors[status]}>{t(`web.recruitment.openings.status.${status}`)}</Tag>;
};

/** Asks for the reason of closing; API field errors are shown on the field. */
const CloseDialog = ({
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
      title={t('web.recruitment.openings.close_title')}
      open
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('web.recruitment.openings.close')}
      okButtonProps={{ danger: true }}
      confirmLoading={saving}
      destroyOnHidden
    >
      <Typography.Paragraph type="secondary">{t('web.recruitment.openings.close_hint')}</Typography.Paragraph>
      <Form form={form} layout="vertical" onFinish={submit}>
        <Form.Item name="reason" label={t('web.recruitment.openings.fields.close_reason')}>
          <Input.TextArea maxLength={500} showCount autoSize={{ minRows: 3, maxRows: 6 }} />
        </Form.Item>
      </Form>
    </Modal>
  );
};

interface OpeningDetailDrawerProps {
  id: string;
  onClose: () => void;
}

/**
 * A job opening (PLAN R3a, D49): what it says as candidates will read it, the pipeline with its applications (D50), the hiring team, the
 * application form and what the caller may do now: correct, publish, hold or close it.
 */
export const OpeningDetailDrawer = ({ id, onClose }: OpeningDetailDrawerProps) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const refresh = useOpeningRefresh();
  const { data: details } = useGetOpening(id);
  const navigate = useNavigate();
  const readsApplications = useHasPermission(recruitmentPermissions.applicationsRead);
  const [dialog, setDialog] = useState<'edit' | 'stages' | 'close'>();
  const [busy, setBusy] = useState<'publish' | 'hold'>();

  const act = async (action: 'publish' | 'hold') => {
    setBusy(action);
    try {
      await (action === 'publish' ? publishOpening(id) : holdOpening(id));
      void message.success(t(`web.recruitment.openings.done.${action}`));
      await refresh(id);
    } catch (error) {
      const fields = isApiError(error) ? (error.problem?.fieldErrors ?? []) : [];
      void message.error(
        fields.length > 0 ? fields.map((e) => e.message).join(' ') : isApiError(error) ? error.message : String(error),
      );
    } finally {
      setBusy(undefined);
    }
  };

  const opening = details?.opening;
  const dash = '—';
  const stageCount = (stageId: string) => {
    const count = opening?.stageCounts.find((s) => s.stageId === stageId)?.count ?? 0;
    return count > 0 ? ` · ${count}` : '';
  };
  const names = (members: { displayName?: string | null; userId: string }[]) =>
    members.length > 0 ? members.map((m) => m.displayName ?? m.userId).join(', ') : dash;
  const footer = details && (
    <Flex justify="flex-end" gap={8} wrap>
      {readsApplications && details.opening.status !== 'Draft' && (
        <Button type="primary" ghost onClick={() => void navigate(`/recruitment/openings/${id}/pipeline`)}>
          {t('web.recruitment.openings.applications')}
        </Button>
      )}
      {details.canEdit && <Button onClick={() => setDialog('edit')}>{t('web.common.edit')}</Button>}
      {details.canEdit && (
        <Button onClick={() => setDialog('stages')}>{t('web.recruitment.openings.edit_stages')}</Button>
      )}
      {details.canHold && (
        <Button loading={busy === 'hold'} disabled={!!busy} onClick={() => void act('hold')}>
          {t('web.recruitment.openings.hold')}
        </Button>
      )}
      {details.canPublish && (
        <Button type="primary" loading={busy === 'publish'} disabled={!!busy} onClick={() => void act('publish')}>
          {t('web.recruitment.openings.publish')}
        </Button>
      )}
      {details.canClose && (
        <Button danger onClick={() => setDialog('close')}>
          {t('web.recruitment.openings.close')}
        </Button>
      )}
    </Flex>
  );

  return (
    <Drawer
      title={opening ? opening.title : t('web.recruitment.openings.detail_title')}
      open
      onClose={onClose}
      size={720}
      footer={footer}
    >
      {!details || !opening ? (
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
                label: t('web.recruitment.openings.fields.status'),
                children: <OpeningStatusTag status={opening.status} />,
              },
              {
                key: 'company',
                label: t('web.recruitment.openings.fields.company'),
                children: opening.companyName ?? dash,
              },
              ...(opening.requisitionNumber
                ? [
                    {
                      key: 'requisition',
                      label: t('web.recruitment.openings.fields.requisition'),
                      children: opening.requisitionNumber,
                    },
                  ]
                : []),
              {
                key: 'where',
                label: t('web.recruitment.openings.fields.branch'),
                children: `${opening.branchName ?? dash} · ${t(`web.recruitment.openings.work_models.${opening.workModel}`)}`,
              },
              {
                key: 'employment',
                label: t('web.recruitment.openings.fields.employment_type'),
                children: t(`web.recruitment.requisitions.employment_types.${opening.employmentType}`),
              },
              { key: 'headcount', label: t('web.recruitment.openings.fields.headcount'), children: opening.headcount },
              {
                key: 'visibility',
                label: t('web.recruitment.openings.fields.visibility'),
                children: t(`web.recruitment.openings.visibilities.${opening.visibility}`),
              },
              {
                key: 'period',
                label: t('web.recruitment.openings.fields.period'),
                children: `${opening.publishFrom ? formatDate(opening.publishFrom, i18n.language) : dash} – ${opening.publishUntil ? formatDate(opening.publishUntil, i18n.language) : dash}`,
              },
              ...(details.publishedAt
                ? [
                    {
                      key: 'published',
                      label: t('web.recruitment.openings.fields.published_at'),
                      children: formatDateTime(details.publishedAt, i18n.language),
                    },
                  ]
                : []),
              {
                key: 'manager',
                label: t('web.recruitment.openings.fields.hiring_manager'),
                children: details.hiringManager.displayName ?? dash,
              },
              {
                key: 'recruiters',
                label: t('web.recruitment.openings.fields.recruiters'),
                children: names(details.recruiters),
              },
              {
                key: 'interviewers',
                label: t('web.recruitment.openings.fields.interviewers'),
                children: names(details.interviewers),
              },
              ...(details.closeReason
                ? [
                    {
                      key: 'closeReason',
                      label: t('web.recruitment.openings.fields.close_reason'),
                      children: details.closeReason,
                    },
                  ]
                : []),
            ]}
          />
          <Card size="small" title={t('web.recruitment.openings.sections.pipeline')}>
            <Space size={[4, 4]} wrap>
              {details.stages.map((stage) => (
                <Tag key={stage.id} color={stageTypeColors[stage.type]}>
                  {stage.name}
                  {stageCount(stage.id)}
                </Tag>
              ))}
            </Space>
          </Card>
          <Card size="small" title={t('web.recruitment.openings.fields.description')}>
            {details.description ? <RichTextView html={details.description} /> : dash}
          </Card>
          {details.qualifications && (
            <Card size="small" title={t('web.recruitment.openings.fields.qualifications')}>
              <RichTextView html={details.qualifications} />
            </Card>
          )}
          <Card size="small" title={t('web.recruitment.openings.sections.form')}>
            <Space size={[4, 4]} wrap style={{ marginBottom: 8 }}>
              {details.form.requirePhone && <Tag>{t('web.recruitment.openings.fields.require_phone')}</Tag>}
              {details.form.requireCv && <Tag>{t('web.recruitment.openings.fields.require_cv')}</Tag>}
              {details.form.askCoverLetter && <Tag>{t('web.recruitment.openings.fields.ask_cover_letter')}</Tag>}
            </Space>
            <List
              size="small"
              dataSource={details.form.questions}
              locale={{ emptyText: t('web.recruitment.openings.no_questions') }}
              renderItem={(question) => (
                <List.Item>
                  <Flex vertical gap={2}>
                    <span>
                      {question.text}
                      {question.required && <Typography.Text type="danger"> *</Typography.Text>}
                    </span>
                    <Typography.Text type="secondary">
                      {t(`web.recruitment.openings.question_types.${question.type}`)}
                      {question.options.length > 0 && ` · ${question.options.join(', ')}`}
                    </Typography.Text>
                    {question.acceptedAnswers && question.acceptedAnswers.length > 0 && (
                      <Typography.Text type="warning">
                        {t('web.recruitment.openings.knockout', {
                          answers: question.acceptedAnswers
                            .map((a) => (a === YES || a === NO ? t(`web.recruitment.openings.answers.${a}`) : a))
                            .join(', '),
                        })}
                      </Typography.Text>
                    )}
                  </Flex>
                </List.Item>
              )}
            />
          </Card>
        </Flex>
      )}
      {details && dialog === 'edit' && <OpeningDrawer details={details} onClose={() => setDialog(undefined)} />}
      {details && dialog === 'stages' && <OpeningStagesDrawer details={details} onClose={() => setDialog(undefined)} />}
      {dialog === 'close' && (
        <CloseDialog
          onClose={() => setDialog(undefined)}
          onSubmit={async (reason) => {
            await closeOpening(id, { reason });
            void message.success(t('web.recruitment.openings.done.close'));
            await refresh(id);
          }}
        />
      )}
    </Drawer>
  );
};
