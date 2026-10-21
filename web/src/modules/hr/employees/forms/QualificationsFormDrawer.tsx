import { Col, DatePicker, Form, Input, InputNumber, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useUpdateEmployeeQualifications } from '@/shared/api/generated/endpoints/hr/hr';
import { CertificateKind, EducationLevel, LanguageLevel, type EmployeeDetails } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { day, enumOptions, text } from './formValues';
import { ListSection } from './ListSection';
import { useEmployeeRefresh } from './useEmployeeRefresh';

interface QualificationsFormValues {
  educations?: { level?: EducationLevel; school?: string; department?: string; graduationYear?: number | null }[];
  certificates?: {
    kind?: CertificateKind;
    name?: string;
    number?: string;
    issuedOn?: Dayjs | null;
    validUntil?: Dayjs | null;
  }[];
  languages?: { code?: string; level?: LanguageLevel }[];
}

const toDay = (value: string | null | undefined) => (value ? dayjs(value) : null);

interface QualificationsFormDrawerProps {
  employee: EmployeeDetails;
  open: boolean;
  onClose: () => void;
}

/** Replaces the person's education, certificates and driving licences, and languages. */
export const QualificationsFormDrawer = ({ employee, open, onClose }: QualificationsFormDrawerProps) => {
  const { t } = useTranslation();
  const update = useUpdateEmployeeQualifications();
  const refresh = useEmployeeRefresh(employee.id);
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: QualificationsFormValues) => {
    await update.mutateAsync({
      id: employee.id,
      data: {
        version: employee.version,
        educations: (values.educations ?? []).map((e) => ({
          level: e.level ?? null,
          school: text(e.school),
          department: text(e.department),
          graduationYear: e.graduationYear ?? null,
        })),
        certificates: (values.certificates ?? []).map((c) => ({
          kind: c.kind ?? null,
          name: text(c.name),
          number: text(c.number),
          issuedOn: day(c.issuedOn),
          validUntil: day(c.validUntil),
        })),
        languages: (values.languages ?? []).map((l) => ({
          code: text(l.code)?.toLowerCase() ?? null,
          level: l.level ?? null,
        })),
      },
    });
    await refresh();
  };

  return (
    <FormDrawer<QualificationsFormValues>
      key={`${employee.id}-${employee.version}`}
      title={t('web.hr.employees.edit.qualifications')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      width={760}
      initialValues={{
        educations: employee.educations.map((e) => ({ ...e, department: e.department ?? undefined })),
        certificates: employee.certificates.map((c) => ({
          kind: c.kind,
          name: c.name,
          number: c.number ?? undefined,
          issuedOn: toDay(c.issuedOn),
          validUntil: toDay(c.validUntil),
        })),
        languages: employee.languages,
      }}
    >
      <ListSection
        name="educations"
        title={t('web.hr.employees.qualifications.education')}
        addLabel={t('web.hr.employees.qualifications.add_education')}
      >
        {(row) => (
          <>
            <Col span={6}>
              <Form.Item name={[row, 'level']} label={t('web.hr.employees.qualifications.level')} rules={required}>
                <Select options={enumOptions(EducationLevel, 'web.hr.education_level.', t)} />
              </Form.Item>
            </Col>
            <Col span={7}>
              <Form.Item name={[row, 'school']} label={t('web.hr.employees.qualifications.school')} rules={required}>
                <Input maxLength={200} />
              </Form.Item>
            </Col>
            <Col span={7}>
              <Form.Item name={[row, 'department']} label={t('web.hr.employees.qualifications.department')}>
                <Input maxLength={200} />
              </Form.Item>
            </Col>
            <Col span={4}>
              <Form.Item name={[row, 'graduationYear']} label={t('web.hr.employees.qualifications.graduation_year')}>
                <InputNumber min={1950} max={2100} precision={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </>
        )}
      </ListSection>
      <ListSection
        name="certificates"
        title={t('web.hr.employees.qualifications.certificates')}
        addLabel={t('web.hr.employees.qualifications.add_certificate')}
        initialValue={{ kind: CertificateKind.Certificate }}
      >
        {(row) => (
          <>
            <Col span={6}>
              <Form.Item name={[row, 'kind']} label={t('web.hr.employees.qualifications.kind')} rules={required}>
                <Select options={enumOptions(CertificateKind, 'web.hr.certificate_kind.', t)} />
              </Form.Item>
            </Col>
            <Col span={9}>
              <Form.Item name={[row, 'name']} label={t('web.hr.employees.qualifications.name')} rules={required}>
                <Input maxLength={200} />
              </Form.Item>
            </Col>
            <Col span={9}>
              <Form.Item name={[row, 'number']} label={t('web.hr.employees.qualifications.number')}>
                <Input maxLength={50} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name={[row, 'issuedOn']} label={t('web.hr.employees.qualifications.issued_on')}>
                <DatePicker style={{ width: '100%' }} format="L" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name={[row, 'validUntil']} label={t('web.hr.employees.qualifications.valid_until')}>
                <DatePicker style={{ width: '100%' }} format="L" />
              </Form.Item>
            </Col>
          </>
        )}
      </ListSection>
      <ListSection
        name="languages"
        title={t('web.hr.employees.qualifications.languages')}
        addLabel={t('web.hr.employees.qualifications.add_language')}
      >
        {(row) => (
          <>
            <Col span={12}>
              <Form.Item
                name={[row, 'code']}
                label={t('web.hr.employees.qualifications.language')}
                extra={t('web.hr.employees.qualifications.language_hint')}
                rules={required}
              >
                <Input maxLength={2} style={{ textTransform: 'lowercase' }} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item
                name={[row, 'level']}
                label={t('web.hr.employees.qualifications.language_level')}
                rules={required}
              >
                <Select options={enumOptions(LanguageLevel, 'web.hr.language_level.', t)} />
              </Form.Item>
            </Col>
          </>
        )}
      </ListSection>
    </FormDrawer>
  );
};
