import { App, DatePicker, Flex, Form, Input, InputNumber, Modal, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { createOffer, updateOffer } from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { OfferDetails, SalaryPeriod } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import { CURRENCIES, salaryPeriods, useOfferRefresh } from './offers';

interface OfferValues {
  positionTitle?: string;
  grade?: string;
  startDate?: Dayjs;
  salary?: number;
  currency?: string;
  period?: SalaryPeriod;
  benefits?: string;
  expiresOn?: Dayjs;
}

const DAY = 'YYYY-MM-DD';

interface OfferFormModalProps {
  /** The application to offer a job; with <c>offer</c> the draft or returned offer is corrected. */
  applicationId: string;
  openingTitle: string;
  offer?: OfferDetails;
  onSaved?: (id: string) => void;
  onClose: () => void;
}

/** Drafts or corrects an offer (PLAN R6, D53): the job, the start, the salary and its period, benefits and the last day. */
export const OfferFormModal = ({ applicationId, openingTitle, offer, onSaved, onClose }: OfferFormModalProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<OfferValues>();
  const refresh = useOfferRefresh();
  const [saving, setSaving] = useState(false);
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async () => {
    let values: OfferValues;
    try {
      values = await form.validateFields();
    } catch {
      return;
    }

    setSaving(true);
    try {
      const body = {
        positionTitle: values.positionTitle?.trim() || null,
        grade: values.grade?.trim() || null,
        startDate: values.startDate!.format(DAY),
        salary: values.salary ?? 0,
        currency: values.currency ?? null,
        period: values.period ?? 'Monthly',
        benefits: values.benefits?.trim() || null,
        expiresOn: values.expiresOn!.format(DAY),
      };
      let id = offer?.offer.id;
      if (offer && id) {
        await updateOffer(id, { ...body, version: offer.version });
      } else {
        id = (await createOffer({ ...body, applicationId })).id;
      }

      void message.success(t('web.recruitment.offers.saved'));
      await refresh(id, applicationId);
      onSaved?.(id);
      onClose();
    } catch (error) {
      if (!applyApiErrorsToForm(form, error)) {
        void message.error(isApiError(error) ? error.message : String(error));
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      title={t(offer ? 'web.recruitment.offers.edit' : 'web.recruitment.offers.new')}
      open
      width={640}
      okText={t('web.common.save')}
      cancelText={t('web.common.cancel')}
      confirmLoading={saving}
      onOk={() => void submit()}
      onCancel={onClose}
      destroyOnHidden
    >
      <Form<OfferValues>
        form={form}
        layout="vertical"
        initialValues={
          offer
            ? {
                positionTitle: offer.offer.positionTitle,
                grade: offer.grade ?? undefined,
                startDate: dayjs(offer.offer.startDate),
                salary: offer.salary ?? undefined,
                currency: offer.currency ?? 'TRY',
                period: offer.period ?? 'Monthly',
                benefits: offer.benefits ?? undefined,
                expiresOn: dayjs(offer.offer.expiresOn),
              }
            : { positionTitle: openingTitle, currency: 'TRY', period: 'Monthly' }
        }
      >
        <Flex gap={12} wrap>
          <Form.Item
            name="positionTitle"
            label={t('web.recruitment.offers.fields.position')}
            style={{ flex: 2, minWidth: 220 }}
          >
            <Input maxLength={150} placeholder={openingTitle} />
          </Form.Item>
          <Form.Item name="grade" label={t('web.recruitment.offers.fields.grade')} style={{ flex: 1, minWidth: 120 }}>
            <Input maxLength={20} />
          </Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item
            name="salary"
            label={t('web.recruitment.offers.fields.salary')}
            rules={required}
            style={{ flex: 2, minWidth: 180 }}
          >
            <InputNumber min={1} precision={2} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            name="currency"
            label={t('web.recruitment.offers.fields.currency')}
            rules={required}
            style={{ width: 110 }}
          >
            <Select options={CURRENCIES.map((c) => ({ value: c, label: c }))} />
          </Form.Item>
          <Form.Item
            name="period"
            label={t('web.recruitment.offers.fields.period')}
            rules={required}
            style={{ width: 160 }}
          >
            <Select
              options={salaryPeriods.map((p) => ({ value: p, label: t(`web.recruitment.offers.periods.${p}`) }))}
            />
          </Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item
            name="startDate"
            label={t('web.recruitment.offers.fields.start_date')}
            rules={required}
            style={{ flex: 1, minWidth: 180 }}
          >
            <DatePicker format="L" style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            name="expiresOn"
            label={t('web.recruitment.offers.fields.expires_on')}
            rules={required}
            extra={t('web.recruitment.offers.expires_hint')}
            style={{ flex: 1, minWidth: 180 }}
          >
            <DatePicker format="L" style={{ width: '100%' }} />
          </Form.Item>
        </Flex>
        <Form.Item name="benefits" label={t('web.recruitment.offers.fields.benefits')}>
          <Input.TextArea rows={3} maxLength={2000} />
        </Form.Item>
      </Form>
    </Modal>
  );
};
