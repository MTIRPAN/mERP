import { Col, Form, Input, Row, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import { useListDistricts, useListProvinces } from '@/shared/api/generated/endpoints/platform/platform';

const searchable = {
  showSearch: { optionFilterProp: 'label' },
  allowClear: true,
} as const;

/** Address inputs under the form field `address`; API errors such as `address.districtCode` land on the field. */
export const AddressFields = ({ required = false }: { required?: boolean }) => {
  const { t } = useTranslation();
  const form = Form.useFormInstance();
  const provinceCode = Form.useWatch<number | undefined>(['address', 'provinceCode'], form);
  const provinces = useListProvinces({ query: { staleTime: Infinity } });
  const districts = useListDistricts(provinceCode ?? 0, { query: { enabled: !!provinceCode, staleTime: Infinity } });
  const rule = { required, message: t('web.platform.address.required') };

  return (
    <>
      <Form.Item name={['address', 'line1']} label={t('web.platform.address.line1')} rules={[rule]}>
        <Input maxLength={250} />
      </Form.Item>
      <Form.Item name={['address', 'line2']} label={t('web.platform.address.line2')}>
        <Input maxLength={250} />
      </Form.Item>
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item name={['address', 'provinceCode']} label={t('web.platform.address.province')} rules={[rule]}>
            <Select
              {...searchable}
              loading={provinces.isFetching}
              options={provinces.data?.map((p) => ({ value: p.code, label: p.name }))}
              onChange={() => form.setFieldValue(['address', 'districtCode'], undefined)}
            />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name={['address', 'districtCode']} label={t('web.platform.address.district')} rules={[rule]}>
            <Select
              {...searchable}
              disabled={!provinceCode}
              loading={districts.isFetching}
              options={districts.data?.map((d) => ({ value: d.code, label: d.name }))}
            />
          </Form.Item>
        </Col>
      </Row>
      <Form.Item name={['address', 'postalCode']} label={t('web.platform.address.postal_code')}>
        <Input maxLength={5} inputMode="numeric" style={{ maxWidth: 160 }} />
      </Form.Item>
    </>
  );
};
