import { UploadOutlined } from '@ant-design/icons';
import { Button, Checkbox, Col, Form, Input, InputNumber, Modal, Radio, Row, Typography, Upload } from 'antd';
import { useState } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import type { CareerConsent, CareerOpeningDetails, CareerQuestion } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';

/** The application as the API reads it from the 'application' form field. */
export interface ApplicationPayload {
  firstName?: string;
  lastName?: string;
  email?: string;
  phone: string | null;
  coverLetter: string | null;
  answers: { questionId: string; values: string[] }[];
  disclosureAccepted: boolean;
  disclosureRevision: number;
  poolConsent: boolean;
  poolConsentRevision: number | null;
  culture: string;
  website?: string;
}

interface ApplicationValues {
  firstName?: string;
  lastName?: string;
  email?: string;
  phone?: string;
  coverLetter?: string;
  answers?: Record<string, string | string[] | number | undefined>;
  disclosureAccepted?: boolean;
  poolConsent?: boolean;
  website?: string;
  cv?: unknown;
}

/** The value(s) of an answer as the API takes them: strings, "yes"/"no", numbers in invariant form. */
const answerValues = (value: string | string[] | number | undefined): string[] => {
  if (value === undefined || value === null || value === '') {
    return [];
  }

  if (Array.isArray(value)) {
    return value;
  }

  return [String(value)];
};

/** The input of a question: an element, not a component, so that Form.Item gives it the value and onChange. */
const questionInput = (question: CareerQuestion, t: TFunction) => {
  switch (question.type) {
    case 'LongText':
      return <Input.TextArea maxLength={4000} autoSize={{ minRows: 3, maxRows: 8 }} />;
    case 'YesNo':
      return (
        <Radio.Group
          options={[
            { value: 'yes', label: t('web.common.yes') },
            { value: 'no', label: t('web.common.no') },
          ]}
        />
      );
    case 'SingleChoice':
      return <Radio.Group options={question.options.map((o) => ({ value: o, label: o }))} />;
    case 'MultiChoice':
      return <Checkbox.Group options={question.options.map((o) => ({ value: o, label: o }))} />;
    case 'Number':
      return <InputNumber style={{ width: 200 }} />;
    default:
      return <Input maxLength={500} />;
  }
};

interface ApplicationFormProps {
  details: CareerOpeningDetails;
  /** Anonymous candidates give their name and e-mail; employees' come from their record. */
  anonymous: boolean;
  culture: string;
  onSubmit: (payload: ApplicationPayload, cv: File | undefined) => Promise<unknown>;
}

/**
 * The application form of an opening (PLAN R3b, D49): contact details, the opening's questions, the CV, and the KVKK
 * texts to accept, readable in a dialog. API field errors land on their fields.
 */
export const ApplicationForm = ({ details, anonymous, culture, onSubmit }: ApplicationFormProps) => {
  const { t } = useTranslation();
  const [form] = Form.useForm<ApplicationValues>();
  const [cv, setCv] = useState<File>();
  const [text, setText] = useState<CareerConsent>();
  const [saving, setSaving] = useState(false);
  const { form: spec, disclosure, poolConsent } = details;
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: ApplicationValues) => {
    setSaving(true);
    try {
      await onSubmit(
        {
          ...(anonymous
            ? { firstName: values.firstName, lastName: values.lastName, email: values.email, website: values.website }
            : {}),
          phone: values.phone?.trim() || null,
          coverLetter: spec.askCoverLetter ? values.coverLetter?.trim() || null : null,
          answers: spec.questions
            .map((q) => ({ questionId: q.id, values: answerValues(values.answers?.[q.id]) }))
            .filter((a) => a.values.length > 0),
          disclosureAccepted: values.disclosureAccepted === true,
          disclosureRevision: disclosure.revision,
          poolConsent: values.poolConsent === true,
          poolConsentRevision: values.poolConsent ? poolConsent.revision : null,
          culture,
        },
        cv,
      );
    } catch (error) {
      if (!isApiError(error) || !applyApiErrorsToForm(form, error)) {
        throw error;
      }
    } finally {
      setSaving(false);
    }
  };

  const consentLabel = (label: string, consent: CareerConsent) => (
    <span>
      {label}{' '}
      <Typography.Link
        onClick={(event) => {
          event.preventDefault();
          setText(consent);
        }}
      >
        {t('web.recruitment.careers.read_text')}
      </Typography.Link>
    </span>
  );

  return (
    <>
      <Form<ApplicationValues> form={form} layout="vertical" requiredMark="optional" onFinish={submit}>
        {anonymous && (
          <>
            <Row gutter={12}>
              <Col xs={24} sm={12}>
                <Form.Item name="firstName" label={t('web.recruitment.careers.fields.first_name')} rules={required}>
                  <Input maxLength={100} autoComplete="given-name" />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12}>
                <Form.Item name="lastName" label={t('web.recruitment.careers.fields.last_name')} rules={required}>
                  <Input maxLength={100} autoComplete="family-name" />
                </Form.Item>
              </Col>
            </Row>
            <Form.Item
              name="email"
              label={t('web.recruitment.careers.fields.email')}
              rules={[...required, { type: 'email', message: t('web.recruitment.careers.invalid_email') }]}
            >
              <Input maxLength={254} autoComplete="email" inputMode="email" />
            </Form.Item>
            {/* Honeypot: hidden from people, filled by bots */}
            <Form.Item name="website" hidden>
              <Input tabIndex={-1} autoComplete="off" />
            </Form.Item>
          </>
        )}
        <Form.Item
          name="phone"
          label={t('web.recruitment.careers.fields.phone')}
          rules={spec.requirePhone && anonymous ? required : []}
          extra={anonymous ? undefined : t('web.recruitment.careers.phone_hint')}
        >
          <Input maxLength={30} autoComplete="tel" inputMode="tel" />
        </Form.Item>
        {spec.askCoverLetter && (
          <Form.Item name="coverLetter" label={t('web.recruitment.careers.fields.cover_letter')}>
            <Input.TextArea maxLength={4000} showCount autoSize={{ minRows: 4, maxRows: 10 }} />
          </Form.Item>
        )}
        {spec.questions.map((question) => (
          <Form.Item
            key={question.id}
            name={['answers', question.id]}
            label={question.text}
            rules={question.required ? required : []}
          >
            {questionInput(question, t)}
          </Form.Item>
        ))}
        <Form.Item
          name="cv"
          label={t('web.recruitment.careers.fields.cv')}
          required={spec.requireCv}
          extra={t('web.recruitment.careers.cv_hint', {
            types: spec.cvExtensions.map((e) => e.toUpperCase()).join(', '),
            size: Math.round(spec.cvMaxBytes / (1024 * 1024)),
          })}
          rules={
            spec.requireCv
              ? [
                  {
                    validator: () =>
                      cv ? Promise.resolve() : Promise.reject(new Error(t('web.platform.fields.required'))),
                  },
                ]
              : []
          }
        >
          <Upload
            accept={spec.cvExtensions.map((e) => `.${e}`).join(',')}
            maxCount={1}
            beforeUpload={(file) => {
              setCv(file);
              form.setFields([{ name: 'cv', errors: [] }]);
              return false;
            }}
            onRemove={() => setCv(undefined)}
            fileList={cv ? [{ uid: 'cv', name: cv.name, status: 'done' }] : []}
          >
            <Button icon={<UploadOutlined />}>{t('web.recruitment.careers.choose_cv')}</Button>
          </Upload>
        </Form.Item>
        <Form.Item
          name="disclosureAccepted"
          valuePropName="checked"
          rules={[
            {
              validator: (_, value) =>
                value ? Promise.resolve() : Promise.reject(new Error(t('web.recruitment.careers.consent_required'))),
            },
          ]}
        >
          <Checkbox>{consentLabel(t('web.recruitment.careers.disclosure'), disclosure)}</Checkbox>
        </Form.Item>
        <Form.Item name="poolConsent" valuePropName="checked">
          <Checkbox>{consentLabel(t('web.recruitment.careers.pool_consent'), poolConsent)}</Checkbox>
        </Form.Item>
        <Button type="primary" htmlType="submit" loading={saving} block>
          {t('web.recruitment.careers.apply')}
        </Button>
      </Form>
      <Modal
        open={!!text}
        title={text ? t(`web.recruitment.careers.consent_titles.${text.kind}`) : undefined}
        onCancel={() => setText(undefined)}
        footer={<Button onClick={() => setText(undefined)}>{t('web.common.close')}</Button>}
      >
        <Typography.Paragraph style={{ whiteSpace: 'pre-wrap' }}>{text?.body}</Typography.Paragraph>
      </Modal>
    </>
  );
};
