import { UploadOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  Checkbox,
  Col,
  DatePicker,
  Drawer,
  Flex,
  Form,
  Input,
  InputNumber,
  Row,
  Select,
  TimePicker,
  Typography,
  Upload,
} from 'antd';
import dayjs from 'dayjs';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAiFeature } from '@/shared/ai/useAiFeature';
import {
  createLeaveRequestOnBehalf,
  createMyLeaveRequest,
  recordLeave,
  submitMyLeaveRequest,
  updateMyLeaveRequest,
  uploadLeaveRequestFile,
  uploadMyLeaveRequestFile,
  useListLeaveRequestTypes,
  useListMyLeaveTypes,
} from '@/shared/api/generated/endpoints/hr/hr';
import type { LeaveRequestDetails, LeaveRequestDraft } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import { LeavePreviewPanel } from './LeavePreviewPanel';
import { LeaveRequestAiFill } from './LeaveRequestAiFill';
import {
  toLeaveBody,
  useLeavePreview,
  useLeaveRefresh,
  type LeaveFormMode,
  type LeaveRequestValues,
} from './leaveRequests';
import { EmploymentPicker, SubstitutePicker } from './PeoplePickers';

interface LeaveRequestDrawerProps {
  mode: LeaveFormMode;
  /** HR: the employment the form opens for; otherwise HR picks the person. */
  employmentId?: string;
  /** The employee's draft or returned request to correct. */
  request?: LeaveRequestDetails;
  onClose: () => void;
  onSaved?: (id: string) => void;
}

/** Fields the form shows its errors next to; the others (balance, overlap, document) go to a message. */
const FORM_FIELDS = [
  'employmentId',
  'leaveTypeId',
  'startDate',
  'endDate',
  'startTime',
  'endTime',
  'travelDays',
  'startPart',
  'substituteEmployeeId',
];

const time = (value: string | null | undefined) => (value ? dayjs(`2000-01-01T${value}`) : undefined);

/** The form values an AI draft brings (D64); what it left open keeps what the form has. */
const draftValues = (draft: LeaveRequestDraft): LeaveRequestValues => {
  const start = draft.startDate ? dayjs(draft.startDate) : undefined;
  return {
    ...(draft.leaveTypeId && { leaveTypeId: draft.leaveTypeId }),
    ...(start && {
      startDate: start,
      endDate: draft.endDate ? dayjs(draft.endDate) : start,
      startAfternoon: draft.startPart === 'Afternoon',
      endMorning: draft.endPart === 'Morning',
    }),
    ...(draft.startTime && { startTime: time(draft.startTime) }),
    ...(draft.endTime && { endTime: time(draft.endTime) }),
    ...(draft.eventDate && { eventDate: dayjs(draft.eventDate) }),
    ...(draft.note && { note: draft.note }),
  };
};

const initialValues = (
  request: LeaveRequestDetails | undefined,
  employmentId: string | undefined,
): LeaveRequestValues =>
  request
    ? {
        leaveTypeId: request.leaveTypeId,
        startDate: dayjs(request.request.startDate),
        endDate: dayjs(request.request.endDate),
        startAfternoon: request.request.startPart === 'Afternoon',
        endMorning: request.request.endPart === 'Morning',
        startTime: time(request.request.startTime),
        endTime: time(request.request.endTime),
        eventDate: request.eventDate ? dayjs(request.eventDate) : undefined,
        travelDays: request.request.travelDays,
        note: request.note ?? undefined,
        address: request.address ?? undefined,
        phone: request.phone ?? undefined,
        substituteEmployeeId: request.substituteEmployeeId ?? undefined,
      }
    : { employmentId, travelDays: 0 };

/**
 * A leave request form with its live check (PLAN L4, D44): the employee asks for themselves (a draft or sent at
 * once), HR asks on a person's behalf or records an approved leave. Files are attached before the request is sent,
 * so a type that asks for a document goes with it.
 */
export const LeaveRequestDrawer = ({
  mode,
  employmentId: givenEmployment,
  request,
  onClose,
  onSaved,
}: LeaveRequestDrawerProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const refresh = useLeaveRefresh();
  const [form] = Form.useForm<LeaveRequestValues>();
  const values = Form.useWatch((all: LeaveRequestValues) => all, { form, preserve: true }) ?? {};
  const employmentId = values.employmentId ?? givenEmployment;
  const self = mode === 'self';
  const myTypes = useListMyLeaveTypes(undefined, { query: { enabled: self } });
  const hrTypes = useListLeaveRequestTypes(
    { employmentId: employmentId ?? '' },
    { query: { enabled: !self && !!employmentId } },
  );
  const types = (self ? myTypes.data : hrTypes.data) ?? [];
  const type = types.find((option) => option.id === values.leaveTypeId);
  const body = toLeaveBody(values, type);
  const preview = useLeavePreview(mode, body, { employmentId, requestId: request?.request.id });
  const [files, setFiles] = useState<File[]>([]);
  const [saving, setSaving] = useState<'draft' | 'send'>();
  // A draft saved before a failed send is corrected, not saved again
  const [savedId, setSavedId] = useState<string>();
  const required = [{ required: true, message: t('web.platform.fields.required') }];
  const hourly = type?.durationBasis === 'Hours';
  const aiFill = useAiFeature('hr.leave_request_draft') && self && !request;

  const upload = async (id: string) => {
    for (const file of files) {
      await (self ? uploadMyLeaveRequestFile(id, { file }) : uploadLeaveRequestFile(id, { file }));
    }

    setFiles([]);
  };

  /** Saves the request; files go up between saving and sending, so a document a type asks for is there. */
  const save = async (send: boolean) => {
    const id = request?.request.id ?? savedId;
    if (self) {
      const draftFirst = files.length > 0 || !send;
      let saved = id;
      if (saved) {
        await updateMyLeaveRequest(saved, { ...body, submit: send && !draftFirst });
      } else {
        saved = (await createMyLeaveRequest({ ...body, employmentId: null, submit: send && !draftFirst })).id;
        setSavedId(saved);
      }

      await upload(saved);
      if (send && draftFirst) {
        await submitMyLeaveRequest(saved);
      }

      return saved;
    }

    const person = { ...body, employmentId: employmentId ?? '' };
    const saved = (mode === 'record' ? await recordLeave(person) : await createLeaveRequestOnBehalf(person)).id;
    await upload(saved);
    return saved;
  };

  const submit = async (send: boolean) => {
    try {
      await form.validateFields();
    } catch {
      return;
    }

    setSaving(send ? 'send' : 'draft');
    try {
      const id = await save(send);
      void message.success(t(send ? `web.hr.leave_requests.saved.${mode}` : 'web.hr.leave_requests.saved.draft'));
      await refresh(id);
      onSaved?.(id);
      onClose();
    } catch (error) {
      if (!isApiError(error)) {
        throw error;
      }

      const fieldErrors = error.problem?.fieldErrors ?? [];
      applyApiErrorsToForm(form, error);
      const elsewhere = fieldErrors.filter((e) => !FORM_FIELDS.includes(e.field));
      if (fieldErrors.length === 0 || elsewhere.length > 0) {
        void message.error(elsewhere.length > 0 ? elsewhere.map((e) => e.message).join(' ') : error.message);
      }
    } finally {
      setSaving(undefined);
    }
  };

  const title = request
    ? t('web.hr.leave_requests.edit_title', { number: request.request.number })
    : t(`web.hr.leave_requests.new_title.${mode}`);
  return (
    <Drawer
      title={title}
      open
      onClose={onClose}
      size={640}
      destroyOnHidden
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose}>{t('web.common.cancel')}</Button>
          {self && (
            <Button loading={saving === 'draft'} disabled={!!saving} onClick={() => void submit(false)}>
              {t('web.hr.leave_requests.save_draft')}
            </Button>
          )}
          <Button
            type="primary"
            loading={saving === 'send'}
            disabled={!!saving || !!preview.data?.problem}
            onClick={() => void submit(true)}
          >
            {t(mode === 'record' ? 'web.hr.leave_requests.record' : 'web.hr.leave_requests.send')}
          </Button>
        </Flex>
      }
    >
      <Form<LeaveRequestValues>
        form={form}
        layout="vertical"
        requiredMark="optional"
        initialValues={initialValues(request, givenEmployment)}
      >
        {aiFill && <LeaveRequestAiFill onDraft={(draft) => form.setFieldsValue(draftValues(draft))} />}
        {!self && !givenEmployment && (
          <Form.Item name="employmentId" label={t('web.hr.leave_requests.fields.employee')} rules={required}>
            <EmploymentPicker onChange={() => form.setFieldsValue({ leaveTypeId: undefined })} />
          </Form.Item>
        )}
        <Form.Item name="leaveTypeId" label={t('web.hr.leave_requests.fields.type')} rules={required}>
          <Select
            showSearch={{ optionFilterProp: 'label' }}
            loading={self ? myTypes.isLoading : hrTypes.isFetching}
            disabled={!self && !employmentId}
            options={types.map((option) => ({ value: option.id, label: option.name }))}
          />
        </Form.Item>
        <Row gutter={12}>
          <Col span={12}>
            <Form.Item
              name="startDate"
              label={t(hourly ? 'web.hr.leave_requests.fields.date' : 'web.hr.leave_requests.fields.start_date')}
              rules={required}
            >
              <DatePicker format="L" style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          {type?.hasFixedLength ? (
            <Col span={12}>
              <Form.Item
                name="eventDate"
                label={t('web.hr.leave_requests.fields.event_date')}
                extra={t('web.hr.leave_requests.fields.event_date_hint')}
              >
                <DatePicker format="L" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          ) : hourly ? (
            <Col span={12}>
              <Flex gap={8}>
                <Form.Item
                  name="startTime"
                  label={t('web.hr.leave_requests.fields.start_time')}
                  rules={required}
                  style={{ flex: 1 }}
                >
                  <TimePicker format="HH:mm" minuteStep={15} style={{ width: '100%' }} />
                </Form.Item>
                <Form.Item
                  name="endTime"
                  label={t('web.hr.leave_requests.fields.end_time')}
                  rules={required}
                  style={{ flex: 1 }}
                >
                  <TimePicker format="HH:mm" minuteStep={15} style={{ width: '100%' }} />
                </Form.Item>
              </Flex>
            </Col>
          ) : (
            <Col span={12}>
              <Form.Item name="endDate" label={t('web.hr.leave_requests.fields.end_date')} rules={required}>
                <DatePicker format="L" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          )}
        </Row>
        {type?.unit === 'HalfDay' && (
          <Flex gap={16} wrap style={{ marginBottom: 16 }}>
            <Form.Item name="startAfternoon" valuePropName="checked" noStyle>
              <Checkbox>{t('web.hr.leave_requests.fields.start_afternoon')}</Checkbox>
            </Form.Item>
            <Form.Item name="endMorning" valuePropName="checked" noStyle>
              <Checkbox>{t('web.hr.leave_requests.fields.end_morning')}</Checkbox>
            </Form.Item>
          </Flex>
        )}
        {type?.category === 'Annual' && (
          <Form.Item
            name="travelDays"
            label={t('web.hr.leave_requests.fields.travel_days')}
            extra={t('web.hr.leave_requests.fields.travel_days_hint')}
          >
            <InputNumber min={0} max={30} precision={0} style={{ width: 160 }} />
          </Form.Item>
        )}
        <LeavePreviewPanel preview={preview.data} loading={preview.isFetching} showApprovers={mode !== 'record'} />
        <Form.Item name="note" label={t('web.hr.leave_requests.fields.note')} style={{ marginTop: 16 }}>
          <Input.TextArea maxLength={500} showCount autoSize={{ minRows: 2, maxRows: 5 }} />
        </Form.Item>
        <Row gutter={12}>
          <Col span={14}>
            <Form.Item name="address" label={t('web.hr.leave_requests.fields.address')}>
              <Input maxLength={300} />
            </Form.Item>
          </Col>
          <Col span={10}>
            <Form.Item name="phone" label={t('web.hr.leave_requests.fields.phone')}>
              <Input maxLength={30} />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item name="substituteEmployeeId" label={t('web.hr.leave_requests.fields.substitute')}>
          <SubstitutePicker
            self={self}
            known={
              request?.substituteEmployeeId && request.substituteName
                ? { value: request.substituteEmployeeId, label: request.substituteName }
                : undefined
            }
          />
        </Form.Item>
        <Form.Item
          label={t('web.hr.leave_requests.fields.files')}
          extra={type?.requiresDocument ? t('web.hr.leave_requests.fields.document_required') : undefined}
        >
          <Upload
            multiple
            fileList={files.map((file, index) => ({ uid: String(index), name: file.name, status: 'done' }))}
            beforeUpload={(file) => {
              setFiles((current) => [...current, file]);
              return false;
            }}
            onRemove={(removed) => setFiles((current) => current.filter((_, index) => String(index) !== removed.uid))}
          >
            <Button icon={<UploadOutlined />}>{t('web.hr.leave_requests.add_file')}</Button>
          </Upload>
          {request && request.files.length > 0 && (
            <Typography.Text type="secondary">
              {t('web.hr.leave_requests.files_attached', { count: request.files.length })}
            </Typography.Text>
          )}
        </Form.Item>
      </Form>
    </Drawer>
  );
};
