import { Checkbox, Col, Form, Input, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import { useUpdateEmployeeContacts } from '@/shared/api/generated/endpoints/hr/hr';
import {
  ContactKind,
  FamilyRelation,
  type EmployeeAddress,
  type EmployeeDetails,
  type EmployeeEmail,
  type EmployeePhone,
  type EmergencyContact,
} from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { enumOptions, text } from './formValues';
import { ListSection } from './ListSection';
import { useEmployeeRefresh } from './useEmployeeRefresh';

interface ContactsFormValues {
  emails?: Partial<EmployeeEmail>[];
  phones?: Partial<EmployeePhone>[];
  addresses?: Partial<EmployeeAddress>[];
  emergencyContacts?: Partial<EmergencyContact>[];
}

interface ContactsFormDrawerProps {
  employee: EmployeeDetails;
  open: boolean;
  onClose: () => void;
}

/** Replaces the person's e-mail addresses, phones, addresses and emergency contacts; at most one primary of each. */
export const ContactsFormDrawer = ({ employee, open, onClose }: ContactsFormDrawerProps) => {
  const { t } = useTranslation();
  const update = useUpdateEmployeeContacts();
  const refresh = useEmployeeRefresh(employee.id);
  const required = [{ required: true, message: t('web.platform.fields.required') }];
  const kinds = enumOptions(ContactKind, 'web.hr.contact_kind.', t);

  const submit = async (values: ContactsFormValues) => {
    await update.mutateAsync({
      id: employee.id,
      data: {
        version: employee.version,
        emails: (values.emails ?? []).map((e) => ({
          kind: e.kind ?? null,
          address: text(e.address),
          isPrimary: !!e.isPrimary,
        })),
        phones: (values.phones ?? []).map((p) => ({
          kind: p.kind ?? null,
          number: text(p.number),
          isPrimary: !!p.isPrimary,
        })),
        addresses: (values.addresses ?? []).map((a) => ({
          kind: a.kind ?? null,
          line: text(a.line),
          district: text(a.district),
          city: text(a.city),
          postalCode: text(a.postalCode),
          countryCode: text(a.countryCode)?.toUpperCase() ?? null,
        })),
        emergencyContacts: (values.emergencyContacts ?? []).map((c) => ({
          fullName: text(c.fullName),
          relation: c.relation ?? null,
          phone: text(c.phone),
        })),
      },
    });
    await refresh();
  };

  return (
    <FormDrawer<ContactsFormValues>
      key={`${employee.id}-${employee.version}`}
      title={t('web.hr.employees.edit.contacts')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      width={760}
      initialValues={{
        emails: employee.emails,
        phones: employee.phones,
        addresses: employee.addresses,
        emergencyContacts: employee.emergencyContacts,
      }}
    >
      <ListSection
        name="emails"
        title={t('web.hr.employees.contacts.emails')}
        addLabel={t('web.hr.employees.contacts.add_email')}
        initialValue={{ kind: ContactKind.Work, isPrimary: false }}
      >
        {(row) => (
          <>
            <Col span={7}>
              <Form.Item name={[row, 'kind']} label={t('web.hr.employees.contacts.kind')} rules={required}>
                <Select options={kinds} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item
                name={[row, 'address']}
                label={t('web.hr.employees.contacts.email')}
                rules={[...required, { type: 'email', message: t('web.hr.employees.contacts.email_invalid') }]}
              >
                <Input maxLength={200} />
              </Form.Item>
            </Col>
            <Col span={5} style={{ paddingTop: 30 }}>
              <Form.Item name={[row, 'isPrimary']} valuePropName="checked">
                <Checkbox>{t('web.hr.employees.primary')}</Checkbox>
              </Form.Item>
            </Col>
          </>
        )}
      </ListSection>
      <ListSection
        name="phones"
        title={t('web.hr.employees.contacts.phones')}
        addLabel={t('web.hr.employees.contacts.add_phone')}
        initialValue={{ kind: ContactKind.Mobile, isPrimary: false }}
      >
        {(row) => (
          <>
            <Col span={7}>
              <Form.Item name={[row, 'kind']} label={t('web.hr.employees.contacts.kind')} rules={required}>
                <Select options={kinds} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name={[row, 'number']} label={t('web.hr.employees.contacts.phone')} rules={required}>
                <Input maxLength={30} inputMode="tel" />
              </Form.Item>
            </Col>
            <Col span={5} style={{ paddingTop: 30 }}>
              <Form.Item name={[row, 'isPrimary']} valuePropName="checked">
                <Checkbox>{t('web.hr.employees.primary')}</Checkbox>
              </Form.Item>
            </Col>
          </>
        )}
      </ListSection>
      <ListSection
        name="addresses"
        title={t('web.hr.employees.contacts.addresses')}
        addLabel={t('web.hr.employees.contacts.add_address')}
        initialValue={{ kind: ContactKind.Home, countryCode: 'TR' }}
      >
        {(row) => (
          <>
            <Col span={6}>
              <Form.Item name={[row, 'kind']} label={t('web.hr.employees.contacts.kind')} rules={required}>
                <Select options={kinds} />
              </Form.Item>
            </Col>
            <Col span={18}>
              <Form.Item name={[row, 'line']} label={t('web.hr.employees.contacts.address')} rules={required}>
                <Input maxLength={200} />
              </Form.Item>
            </Col>
            <Col span={7}>
              <Form.Item name={[row, 'district']} label={t('web.hr.employees.contacts.district')}>
                <Input maxLength={100} />
              </Form.Item>
            </Col>
            <Col span={7}>
              <Form.Item name={[row, 'city']} label={t('web.hr.employees.contacts.city')}>
                <Input maxLength={100} />
              </Form.Item>
            </Col>
            <Col span={5}>
              <Form.Item name={[row, 'postalCode']} label={t('web.hr.employees.contacts.postal_code')}>
                <Input maxLength={10} />
              </Form.Item>
            </Col>
            <Col span={5}>
              <Form.Item name={[row, 'countryCode']} label={t('web.hr.employees.contacts.country')} rules={required}>
                <Input maxLength={2} style={{ textTransform: 'uppercase' }} />
              </Form.Item>
            </Col>
          </>
        )}
      </ListSection>
      <ListSection
        name="emergencyContacts"
        title={t('web.hr.employees.contacts.emergency')}
        addLabel={t('web.hr.employees.contacts.add_emergency')}
      >
        {(row) => (
          <>
            <Col span={10}>
              <Form.Item name={[row, 'fullName']} label={t('web.hr.employees.contacts.name')} rules={required}>
                <Input maxLength={100} />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name={[row, 'relation']} label={t('web.hr.employees.contacts.relation')} rules={required}>
                <Select options={enumOptions(FamilyRelation, 'web.hr.family_relation.', t)} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name={[row, 'phone']} label={t('web.hr.employees.contacts.phone')} rules={required}>
                <Input maxLength={30} inputMode="tel" />
              </Form.Item>
            </Col>
          </>
        )}
      </ListSection>
    </FormDrawer>
  );
};
