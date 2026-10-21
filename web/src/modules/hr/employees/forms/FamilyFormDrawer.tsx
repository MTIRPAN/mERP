import { Checkbox, Col, DatePicker, Form, Input, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useUpdateEmployeeFamily } from '@/shared/api/generated/endpoints/hr/hr';
import { FamilyRelation, type EmployeeDetails } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { day, enumOptions, text } from './formValues';
import { ListSection } from './ListSection';
import { useEmployeeRefresh } from './useEmployeeRefresh';

interface MemberValues {
  relation?: FamilyRelation;
  fullName?: string;
  birthDate?: Dayjs | null;
  isWorking?: boolean;
}

interface FamilyFormDrawerProps {
  employee: EmployeeDetails;
  open: boolean;
  onClose: () => void;
}

/** Replaces the person's family members (aile bilgileri). */
export const FamilyFormDrawer = ({ employee, open, onClose }: FamilyFormDrawerProps) => {
  const { t } = useTranslation();
  const update = useUpdateEmployeeFamily();
  const refresh = useEmployeeRefresh(employee.id);
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: { members?: MemberValues[] }) => {
    await update.mutateAsync({
      id: employee.id,
      data: {
        version: employee.version,
        members: (values.members ?? []).map((m) => ({
          relation: m.relation ?? null,
          fullName: text(m.fullName),
          birthDate: day(m.birthDate),
          isWorking: !!m.isWorking,
        })),
      },
    });
    await refresh();
  };

  return (
    <FormDrawer<{ members?: MemberValues[] }>
      key={`${employee.id}-${employee.version}`}
      title={t('web.hr.employees.edit.family')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      width={720}
      initialValues={{
        members: employee.familyMembers.map((m) => ({
          relation: m.relation,
          fullName: m.fullName,
          birthDate: m.birthDate ? dayjs(m.birthDate) : null,
          isWorking: m.isWorking,
        })),
      }}
    >
      <ListSection
        name="members"
        title={t('web.hr.employees.tabs.family')}
        addLabel={t('web.hr.employees.family.add')}
        initialValue={{ isWorking: false }}
      >
        {(row) => (
          <>
            <Col span={6}>
              <Form.Item name={[row, 'relation']} label={t('web.hr.employees.family.relation')} rules={required}>
                <Select options={enumOptions(FamilyRelation, 'web.hr.family_relation.', t)} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name={[row, 'fullName']} label={t('web.hr.employees.family.name')} rules={required}>
                <Input maxLength={100} />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name={[row, 'birthDate']} label={t('web.hr.employees.fields.birth_date')}>
                <DatePicker style={{ width: '100%' }} format="L" />
              </Form.Item>
            </Col>
            <Col span={4} style={{ paddingTop: 30 }}>
              <Form.Item name={[row, 'isWorking']} valuePropName="checked">
                <Checkbox>{t('web.hr.employees.family.is_working')}</Checkbox>
              </Form.Item>
            </Col>
          </>
        )}
      </ListSection>
    </FormDrawer>
  );
};
