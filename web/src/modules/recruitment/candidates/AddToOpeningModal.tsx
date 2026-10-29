import { Form, Modal, Select } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { addCandidateToOpening, useListOpenings } from '@/shared/api/generated/endpoints/recruitment/recruitment';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';

interface AddToOpeningModalProps {
  candidateId: string;
  candidateName: string;
  /** Openings the candidate already applied to. */
  appliedTo: string[];
  onAdded: (applicationId: string, openingId: string) => void;
  onClose: () => void;
}

/** Adds a candidate of the pool to an opening that is published or on hold (D50). */
export const AddToOpeningModal = ({
  candidateId,
  candidateName,
  appliedTo,
  onAdded,
  onClose,
}: AddToOpeningModalProps) => {
  const { t } = useTranslation();
  const [form] = Form.useForm<{ openingId?: string }>();
  const [saving, setSaving] = useState(false);
  const [search, setSearch] = useState<string>();
  const openings = useListOpenings({ status: ['Published', 'OnHold'], search, pageSize: 50 });

  const submit = async ({ openingId }: { openingId?: string }) => {
    setSaving(true);
    try {
      const { id } = await addCandidateToOpening(candidateId, { openingId: openingId! });
      onAdded(id, openingId!);
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
      title={t('web.recruitment.candidates.add_to_opening_title', { name: candidateName })}
      open
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('web.recruitment.candidates.add_to_opening')}
      confirmLoading={saving}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" onFinish={submit}>
        <Form.Item
          name="openingId"
          label={t('web.recruitment.candidates.fields.opening')}
          rules={[{ required: true, message: t('web.platform.fields.required') }]}
        >
          <Select
            showSearch={{ filterOption: false, onSearch: (value) => setSearch(value || undefined) }}
            loading={openings.isFetching}
            options={(openings.data?.items ?? []).map((o) => ({
              value: o.id,
              label: `${o.title} · ${o.companyName ?? ''}`,
              disabled: appliedTo.includes(o.id),
            }))}
          />
        </Form.Item>
      </Form>
    </Modal>
  );
};
