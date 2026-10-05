import { useQueryClient } from '@tanstack/react-query';
import { Col, Divider, Form, Input, Row, Select, Skeleton, Switch } from 'antd';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import {
  getGetCompanyQueryKey,
  getGetMeQueryKey,
  getListCompaniesQueryKey,
  useCreateCompany,
  useGetCompany,
  useUpdateCompany,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { CompanyDetails } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { fromAddressDto, toAddressInput, type AddressValues } from './address';
import { AddressFields } from './AddressFields';
import { timeZones } from './timeZones';

interface CompanyFormValues {
  code?: string;
  title?: string;
  shortName?: string;
  taxOffice?: string;
  taxNumber?: string;
  mersisNumber?: string;
  sgkWorkplaceNumber?: string;
  phone?: string;
  email?: string;
  culture?: string;
  timeZoneId?: string;
  isActive?: boolean;
  address?: AddressValues;
}

const blank = (value?: string) => (value?.trim() ? value.trim() : null);

const toValues = (company?: CompanyDetails): CompanyFormValues =>
  company
    ? {
        code: company.code,
        title: company.title,
        shortName: company.shortName,
        taxOffice: company.taxOffice ?? undefined,
        taxNumber: company.taxNumber,
        mersisNumber: company.mersisNumber ?? undefined,
        sgkWorkplaceNumber: company.sgkWorkplaceNumber ?? undefined,
        phone: company.phone ?? undefined,
        email: company.email ?? undefined,
        culture: company.culture,
        timeZoneId: company.timeZoneId,
        isActive: company.isActive,
        address: fromAddressDto(company.address),
      }
    : { culture: 'tr-TR', timeZoneId: 'Europe/Istanbul', isActive: true };

interface CompanyFormDrawerProps {
  /** Company to edit; undefined creates a new one. */
  companyId?: string;
  open: boolean;
  onClose: () => void;
}

/** Create or edit a company. The code is fixed after creation. */
export const CompanyFormDrawer = ({ companyId, open, onClose }: CompanyFormDrawerProps) => {
  const { t } = useTranslation();
  const me = useMe();
  const queryClient = useQueryClient();
  const editing = !!companyId;
  const company = useGetCompany(companyId ?? '', { query: { enabled: open && editing } });
  const create = useCreateCompany();
  const update = useUpdateCompany();

  const submit = async (values: CompanyFormValues) => {
    const fields = {
      title: blank(values.title),
      shortName: blank(values.shortName),
      taxOffice: blank(values.taxOffice),
      taxNumber: blank(values.taxNumber),
      mersisNumber: blank(values.mersisNumber),
      sgkWorkplaceNumber: blank(values.sgkWorkplaceNumber),
      address: toAddressInput(values.address),
      phone: blank(values.phone),
      email: blank(values.email),
      culture: values.culture ?? null,
      timeZoneId: values.timeZoneId ?? null,
    };

    if (editing && company.data) {
      await update.mutateAsync({
        id: company.data.id,
        data: { ...fields, version: company.data.version, isActive: values.isActive ?? true },
      });
    } else {
      await create.mutateAsync({ data: { ...fields, code: blank(values.code) } });
    }

    await queryClient.invalidateQueries({ queryKey: getListCompaniesQueryKey() });
    await queryClient.invalidateQueries({ queryKey: getGetMeQueryKey() });
    if (companyId) {
      await queryClient.invalidateQueries({ queryKey: getGetCompanyQueryKey(companyId) });
    }
  };

  const loading = editing && !company.data;

  return (
    <FormDrawer<CompanyFormValues>
      key={company.data ? `${company.data.id}-${company.data.version}` : 'new'}
      title={t(editing ? 'web.platform.companies.edit' : 'web.platform.companies.new')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      initialValues={toValues(company.data)}
      width={640}
    >
      {loading ? (
        <Skeleton active />
      ) : (
        <>
          <Row gutter={12}>
            <Col span={8}>
              <Form.Item
                name="code"
                label={t('web.platform.fields.code')}
                tooltip={t('web.platform.fields.code_hint')}
                rules={[{ required: true, message: t('web.platform.fields.required') }]}
              >
                <Input maxLength={20} disabled={editing} style={{ textTransform: 'uppercase' }} />
              </Form.Item>
            </Col>
            <Col span={16}>
              <Form.Item
                name="shortName"
                label={t('web.platform.companies.short_name')}
                rules={[{ required: true, message: t('web.platform.fields.required') }]}
              >
                <Input maxLength={100} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item
            name="title"
            label={t('web.platform.companies.title_field')}
            rules={[{ required: true, message: t('web.platform.fields.required') }]}
          >
            <Input maxLength={250} />
          </Form.Item>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item
                name="taxNumber"
                label={t('web.platform.companies.tax_number')}
                rules={[{ required: true, message: t('web.platform.fields.required') }]}
              >
                <Input maxLength={10} inputMode="numeric" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="taxOffice" label={t('web.platform.companies.tax_office')}>
                <Input maxLength={100} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item name="mersisNumber" label={t('web.platform.companies.mersis_number')}>
                <Input maxLength={19} inputMode="numeric" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="sgkWorkplaceNumber" label={t('web.platform.fields.sgk_workplace_number')}>
                <Input maxLength={40} inputMode="numeric" />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item name="phone" label={t('web.platform.fields.phone')}>
                <Input maxLength={20} inputMode="tel" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="email" label={t('web.platform.fields.email')}>
                <Input maxLength={254} inputMode="email" />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item name="culture" label={t('web.platform.companies.culture')}>
                <Select options={me.cultures.map((c) => ({ value: c.code, label: c.name }))} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="timeZoneId" label={t('web.platform.companies.time_zone')}>
                <Select showSearch options={timeZones.map((zone) => ({ value: zone, label: zone }))} />
              </Form.Item>
            </Col>
          </Row>
          {editing && (
            <Form.Item name="isActive" label={t('web.platform.fields.active')} valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
          <Divider titlePlacement="start">{t('web.platform.address.title')}</Divider>
          <AddressFields />
        </>
      )}
    </FormDrawer>
  );
};
