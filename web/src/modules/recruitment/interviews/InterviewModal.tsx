import { Alert, App, Card, DatePicker, Flex, Form, Input, Modal, Select, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  scheduleInterview,
  updateInterview,
  useListInterviewConflicts,
  useListInterviewTemplates,
  useSearchInterviewUsers,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { InterviewDetails, InterviewType } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import { formatDateTime } from '@/shared/format/date';
import { useSettled } from '../requisitions/requisitions';
import { interviewTypes, useInterviewRefresh } from './interviews';

interface InterviewValues {
  type?: InterviewType;
  when?: [Dayjs, Dayjs];
  location?: string;
  onlineLink?: string;
  interviewerUserIds?: string[];
  evaluationTemplateId?: string;
  notes?: string;
}

/** The API names the times; the form keeps them in one range. */
const FIELD_ALIASES: Record<string, (string | number)[]> = { startsAt: ['when'], endsAt: ['when'] };

interface InterviewModalProps {
  /** The application to plan an interview for; with <c>interview</c> the planned one is changed. */
  applicationId: string;
  candidateName: string;
  openingTitle: string;
  interview?: InterviewDetails;
  onSaved?: (id: string) => void;
  onClose: () => void;
}

/**
 * Plans or changes an interview (PLAN R5, D52): the interviewers' other interviews at the same time are shown as a
 * warning, and a preview tells what the candidate and the interviewers get with the calendar file.
 */
export const InterviewModal = ({
  applicationId,
  candidateName,
  openingTitle,
  interview,
  onSaved,
  onClose,
}: InterviewModalProps) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<InterviewValues>();
  const refresh = useInterviewRefresh();
  const [saving, setSaving] = useState(false);
  const [userSearch, setUserSearch] = useState('');
  const settledUser = useSettled(userSearch);
  const users = useSearchInterviewUsers({ search: settledUser || undefined });
  const templates = useListInterviewTemplates({ query: { enabled: !interview } });
  const values = Form.useWatch((all: InterviewValues) => all, { form, preserve: true }) ?? {};
  const [start, end] = values.when ?? [];
  const interviewers = values.interviewerUserIds ?? [];
  const range = useSettled(
    start && end && end.isAfter(start) && interviewers.length > 0
      ? {
          startsAt: start.toISOString(),
          endsAt: end.toISOString(),
          interviewerUserIds: interviewers,
          exceptInterviewId: interview?.interview.id,
        }
      : undefined,
    400,
  );
  const conflicts = useListInterviewConflicts(range ?? { startsAt: '', endsAt: '' }, { query: { enabled: !!range } });
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const known = new Map((interview?.interview.interviewers ?? []).map((p) => [p.userId, p.name ?? p.userId]));
  for (const user of users.data ?? []) {
    known.set(user.id, user.displayName);
  }

  const userOptions = [...known].map(([value, label]) => ({ value, label }));

  const submit = async () => {
    let checked: InterviewValues;
    try {
      checked = await form.validateFields();
    } catch {
      return;
    }

    setSaving(true);
    try {
      const [startsAt, endsAt] = checked.when!;
      const body = {
        type: checked.type!,
        startsAt: startsAt.toISOString(),
        endsAt: endsAt.toISOString(),
        location: checked.location?.trim() || null,
        onlineLink: checked.onlineLink?.trim() || null,
        interviewerUserIds: checked.interviewerUserIds ?? [],
        notes: checked.notes?.trim() || null,
      };
      let id = interview?.interview.id;
      if (interview && id) {
        await updateInterview(id, { ...body, version: interview.version });
      } else {
        id = (
          await scheduleInterview({
            ...body,
            applicationId,
            evaluationTemplateId: checked.evaluationTemplateId ?? null,
          })
        ).id;
      }

      void message.success(
        t(interview ? 'web.recruitment.interviews.saved.updated' : 'web.recruitment.interviews.saved.scheduled'),
      );
      await refresh(id, applicationId);
      onSaved?.(id);
      onClose();
    } catch (error) {
      if (!applyApiErrorsToForm(form, error, FIELD_ALIASES)) {
        void message.error(isApiError(error) ? error.message : String(error));
      }
    } finally {
      setSaving(false);
    }
  };

  const place = values.location?.trim() || values.onlineLink?.trim();
  const names = interviewers.map((id) => known.get(id) ?? id);

  return (
    <Modal
      title={t(interview ? 'web.recruitment.interviews.edit' : 'web.recruitment.interviews.schedule')}
      open
      width={720}
      okText={t(interview ? 'web.recruitment.interviews.send_update' : 'web.recruitment.interviews.send_invitations')}
      cancelText={t('web.common.cancel')}
      confirmLoading={saving}
      onOk={() => void submit()}
      onCancel={onClose}
      destroyOnHidden
    >
      <Form<InterviewValues>
        form={form}
        layout="vertical"
        initialValues={
          interview
            ? {
                type: interview.interview.type,
                when: [dayjs(interview.interview.startsAt), dayjs(interview.interview.endsAt)],
                location: interview.interview.location ?? undefined,
                onlineLink: interview.interview.onlineLink ?? undefined,
                interviewerUserIds: interview.interview.interviewers.map((p) => p.userId),
                notes: interview.notes ?? undefined,
              }
            : { type: 'Video' }
        }
      >
        <Flex gap={12} wrap>
          <Form.Item
            name="type"
            label={t('web.recruitment.interviews.fields.type')}
            rules={required}
            style={{ minWidth: 180, flex: 1 }}
          >
            <Select
              options={interviewTypes.map((type) => ({
                value: type,
                label: t(`web.recruitment.interviews.types.${type}`),
              }))}
            />
          </Form.Item>
          <Form.Item
            name="when"
            label={t('web.recruitment.interviews.fields.when')}
            rules={required}
            style={{ minWidth: 320, flex: 2 }}
          >
            <DatePicker.RangePicker
              showTime={{ format: 'HH:mm', minuteStep: 5 }}
              format="L LT"
              style={{ width: '100%' }}
            />
          </Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item
            name="location"
            label={t('web.recruitment.interviews.fields.location')}
            style={{ minWidth: 220, flex: 1 }}
          >
            <Input maxLength={300} />
          </Form.Item>
          <Form.Item
            name="onlineLink"
            label={t('web.recruitment.interviews.fields.online_link')}
            style={{ minWidth: 220, flex: 1 }}
          >
            <Input maxLength={500} placeholder={t('web.recruitment.interviews.link_placeholder')} />
          </Form.Item>
        </Flex>
        <Form.Item
          name="interviewerUserIds"
          label={t('web.recruitment.interviews.fields.interviewers')}
          rules={required}
        >
          <Select
            mode="multiple"
            showSearch={{ filterOption: false, onSearch: setUserSearch }}
            loading={users.isFetching}
            options={userOptions}
            placeholder={t('web.recruitment.requisitions.search_person')}
          />
        </Form.Item>
        {!interview && (
          <Form.Item
            name="evaluationTemplateId"
            label={t('web.recruitment.interviews.fields.template')}
            extra={t('web.recruitment.interviews.template_hint')}
          >
            <Select
              allowClear
              loading={templates.isLoading}
              options={(templates.data ?? []).map((tpl) => ({ value: tpl.id, label: tpl.name }))}
            />
          </Form.Item>
        )}
        <Form.Item name="notes" label={t('web.recruitment.interviews.fields.notes')}>
          <Input.TextArea rows={2} maxLength={2000} />
        </Form.Item>
      </Form>
      <Flex vertical gap={12}>
        {(conflicts.data ?? []).length > 0 && (
          <Alert
            type="warning"
            showIcon
            title={t('web.recruitment.interviews.conflicts')}
            description={
              <ul style={{ margin: 0, paddingInlineStart: 20 }}>
                {conflicts.data!.map((c) => (
                  <li key={`${c.userId}-${c.interviewId}`}>
                    {t('web.recruitment.interviews.conflict', {
                      name: c.name ?? known.get(c.userId) ?? c.userId,
                      start: formatDateTime(c.startsAt, i18n.language),
                      end: dayjs(c.endsAt).format('LT'),
                    })}
                  </li>
                ))}
              </ul>
            }
          />
        )}
        <Card size="small" title={t('web.recruitment.interviews.preview.title')}>
          <Typography.Paragraph strong style={{ marginBottom: 4 }}>
            {t('web.recruitment.interviews.preview.subject', { position: openingTitle })}
          </Typography.Paragraph>
          <Typography.Paragraph style={{ marginBottom: 4 }}>
            {t('web.recruitment.interviews.preview.to', {
              candidate: candidateName,
              interviewers: names.join(', ') || '—',
            })}
          </Typography.Paragraph>
          <Typography.Paragraph style={{ marginBottom: 4 }}>
            {start && end
              ? t('web.recruitment.interviews.preview.when', {
                  start: formatDateTime(start.toISOString(), i18n.language),
                  end: end.format('LT'),
                })
              : t('web.recruitment.interviews.preview.no_time')}
            {place ? ` · ${place}` : ''}
          </Typography.Paragraph>
          <Typography.Text type="secondary">
            {t(
              interview ? 'web.recruitment.interviews.preview.update_note' : 'web.recruitment.interviews.preview.note',
            )}
          </Typography.Text>
        </Card>
      </Flex>
    </Modal>
  );
};
