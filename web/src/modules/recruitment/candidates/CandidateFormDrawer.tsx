import { Alert, Flex, Form, Input, InputNumber, Select, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { useMe } from '@/app/me/meContext';
import {
  createCandidate,
  listCandidates,
  updateCandidate,
  useGetCandidateOptions,
  useSearchCandidateReferrers,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { CandidateDetails, CandidateListItem, CreateCandidateCommand } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { EMAIL_TAKEN, REFERRAL, useCandidateRefresh } from './candidates';

type CandidateValues = Omit<CreateCandidateCommand, 'tags'> & { tags?: string[]; referrerCompanyId?: string };

interface CandidateFormDrawerProps {
  /** The candidate to correct; a new one when absent. */
  candidate?: CandidateDetails;
  onClose: () => void;
  onSaved?: (id: string) => void;
}

/**
 * Adds a candidate to the pool or corrects one (D50): contact, source (with the referring employee), profile and
 * tags. An e-mail another candidate has is refused; the form then offers that candidate.
 */
export const CandidateFormDrawer = ({ candidate, onClose, onSaved }: CandidateFormDrawerProps) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const me = useMe();
  const [form] = Form.useForm<CandidateValues>();
  const refresh = useCandidateRefresh();
  const options = useGetCandidateOptions();
  const [existing, setExisting] = useState<CandidateListItem>();
  const [referrerSearch, setReferrerSearch] = useState<string>();
  const source = Form.useWatch('source', form);
  const referrerCompanyId = Form.useWatch('referrerCompanyId', form);
  const referrers = useSearchCandidateReferrers(
    { companyId: referrerCompanyId ?? '', search: referrerSearch },
    { query: { enabled: source === REFERRAL && !!referrerCompanyId } },
  );
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: CandidateValues) => {
    // The company only narrows the referrer search; it is not sent
    const fields: Partial<CandidateValues> = { ...values };
    delete fields.referrerCompanyId;
    const body = {
      ...fields,
      phone: fields.phone || null,
      referrerEmployeeId: fields.source === REFERRAL ? (fields.referrerEmployeeId ?? null) : null,
      tags: fields.tags ?? [],
    } as CreateCandidateCommand;
    setExisting(undefined);
    try {
      if (candidate) {
        await updateCandidate(candidate.id, { ...body, version: candidate.version });
        await refresh(candidate.id);
        onSaved?.(candidate.id);
      } else {
        const { id } = await createCandidate(body);
        await refresh(id);
        onSaved?.(id);
      }
    } catch (error) {
      // The e-mail is another candidate's: find it in the pool and offer it
      if (isApiError(error) && error.problem?.fieldErrors?.some((e) => e.code === EMAIL_TAKEN)) {
        const page = await listCandidates({ search: body.email ?? '', pageSize: 5 });
        setExisting(page.items.find((c) => c.email === body.email?.trim().toLowerCase()));
      }

      throw error;
    }
  };

  return (
    <FormDrawer<CandidateValues>
      title={candidate ? t('web.recruitment.candidates.edit_title') : t('web.recruitment.candidates.new_title')}
      open
      width={640}
      form={form}
      onClose={onClose}
      onSubmit={submit}
      initialValues={
        candidate
          ? {
              email: candidate.email,
              firstName: candidate.firstName,
              lastName: candidate.lastName,
              phone: candidate.phone,
              source: candidate.source,
              referrerEmployeeId: candidate.referrerEmployeeId,
              ...candidate.profile,
              tags: candidate.tags.map((tag) => tag.code),
            }
          : { source: REFERRAL, tags: [] }
      }
    >
      {existing && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('web.recruitment.candidates.email_taken', { name: existing.fullName })}
          action={
            <Typography.Link onClick={() => void navigate(`/recruitment/candidates/${existing.id}`)}>
              {t('web.recruitment.candidates.open_existing')}
            </Typography.Link>
          }
        />
      )}
      <Flex gap={12}>
        <Form.Item
          name="firstName"
          label={t('web.recruitment.candidates.fields.first_name')}
          rules={required}
          style={{ flex: 1 }}
        >
          <Input maxLength={100} />
        </Form.Item>
        <Form.Item
          name="lastName"
          label={t('web.recruitment.candidates.fields.last_name')}
          rules={required}
          style={{ flex: 1 }}
        >
          <Input maxLength={100} />
        </Form.Item>
      </Flex>
      <Form.Item
        name="email"
        label={t('web.recruitment.candidates.fields.email')}
        rules={[...required, { type: 'email' }]}
      >
        <Input maxLength={254} />
      </Form.Item>
      <Form.Item name="phone" label={t('web.recruitment.candidates.fields.phone')}>
        <Input maxLength={30} />
      </Form.Item>
      <Form.Item name="source" label={t('web.recruitment.candidates.fields.source')} rules={required}>
        <Select options={(options.data?.sources ?? []).map((s) => ({ value: s.code, label: s.name }))} />
      </Form.Item>
      {source === REFERRAL && (
        <Flex gap={12}>
          <Form.Item
            name="referrerCompanyId"
            label={t('web.recruitment.candidates.fields.referrer_company')}
            style={{ flex: 1 }}
          >
            <Select allowClear options={me.companies.map((c) => ({ value: c.id, label: c.name }))} />
          </Form.Item>
          <Form.Item
            name="referrerEmployeeId"
            label={t('web.recruitment.candidates.fields.referrer')}
            style={{ flex: 1 }}
          >
            <Select
              allowClear
              showSearch={{ filterOption: false, onSearch: (value) => setReferrerSearch(value || undefined) }}
              disabled={!referrerCompanyId && !candidate?.referrerEmployeeId}
              loading={referrers.isFetching}
              options={[
                ...(candidate?.referrerEmployeeId && candidate.referrerName
                  ? [{ value: candidate.referrerEmployeeId, label: candidate.referrerName }]
                  : []),
                ...(referrers.data ?? [])
                  .filter((e) => e.employeeId !== candidate?.referrerEmployeeId)
                  .map((e) => ({ value: e.employeeId, label: `${e.fullName} · ${e.registryNumber}` })),
              ]}
            />
          </Form.Item>
        </Flex>
      )}
      <Flex gap={12}>
        <Form.Item name="currentTitle" label={t('web.recruitment.candidates.fields.current_title')} style={{ flex: 1 }}>
          <Input maxLength={150} />
        </Form.Item>
        <Form.Item
          name="currentCompany"
          label={t('web.recruitment.candidates.fields.current_company')}
          style={{ flex: 1 }}
        >
          <Input maxLength={150} />
        </Form.Item>
      </Flex>
      <Flex gap={12}>
        <Form.Item name="location" label={t('web.recruitment.candidates.fields.location')} style={{ flex: 1 }}>
          <Input maxLength={150} />
        </Form.Item>
        <Form.Item
          name="experienceYears"
          label={t('web.recruitment.candidates.fields.experience_years')}
          style={{ width: 180 }}
        >
          <InputNumber min={0} max={60} step={0.5} precision={1} style={{ width: '100%' }} />
        </Form.Item>
      </Flex>
      <Form.Item name="education" label={t('web.recruitment.candidates.fields.education')}>
        <Input.TextArea maxLength={300} autoSize={{ minRows: 1, maxRows: 3 }} />
      </Form.Item>
      <Form.Item name="linkedInUrl" label={t('web.recruitment.candidates.fields.linkedin')}>
        <Input maxLength={300} placeholder={t('web.recruitment.candidates.url_placeholder')} />
      </Form.Item>
      <Form.Item name="portfolioUrl" label={t('web.recruitment.candidates.fields.portfolio')}>
        <Input maxLength={300} placeholder={t('web.recruitment.candidates.url_placeholder')} />
      </Form.Item>
      <Form.Item name="tags" label={t('web.recruitment.candidates.fields.tags')}>
        <Select
          mode="multiple"
          allowClear
          options={(options.data?.tags ?? []).map((tag) => ({ value: tag.code, label: tag.name }))}
        />
      </Form.Item>
    </FormDrawer>
  );
};
