import { App, Checkbox, Col, DatePicker, Form, Input, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useSubmitFamilyChange, useUpdateEmployeeFamily } from '@/shared/api/generated/endpoints/hr/hr';
import { FamilyRelation, type EmployeeDetails, type FamilyMember } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { NoteField } from '../../selfService/NoteField';
import { useProfileChangesRefresh } from '../../selfService/useProfileChangesRefresh';
import { day, enumOptions, text } from './formValues';
import { ListSection } from './ListSection';
import { useEmployeeRefresh } from './useEmployeeRefresh';

interface MemberValues {
  relation?: FamilyRelation;
  fullName?: string;
  birthDate?: Dayjs | null;
  isWorking?: boolean;
}

interface FamilyFormValues {
  members?: MemberValues[];
  note?: string;
}

const toInputs = (values: FamilyFormValues) =>
  (values.members ?? []).map((m) => ({
    relation: m.relation ?? null,
    fullName: text(m.fullName),
    birthDate: day(m.birthDate),
    isWorking: !!m.isWorking,
  }));

interface FamilyFormDrawerProps {
  employee: EmployeeDetails;
  open: boolean;
  onClose: () => void;
  /** Self service (D39): sent for approval as a change request; see ContactsFormDrawer. */
  request?: { requestId?: string; initial?: FamilyMember[]; note?: string | null };
}

/** Replaces the person's family members (aile bilgileri). */
export const FamilyFormDrawer = ({ employee, open, onClose, request }: FamilyFormDrawerProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const update = useUpdateEmployeeFamily();
  const submitChange = useSubmitFamilyChange();
  const refresh = useEmployeeRefresh(employee.id);
  const refreshRequests = useProfileChangesRefresh();
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: FamilyFormValues) => {
    if (request) {
      await submitChange.mutateAsync({
        data: { members: toInputs(values), note: text(values.note), requestId: request.requestId ?? null },
      });
      await refreshRequests();
      message.success(t('web.hr.profile_changes.request.sent'));
      return;
    }

    await update.mutateAsync({ id: employee.id, data: { version: employee.version, members: toInputs(values) } });
    await refresh();
  };

  return (
    <FormDrawer<FamilyFormValues>
      key={`${employee.id}-${employee.version}`}
      title={t(request ? 'web.hr.profile_changes.request.family' : 'web.hr.employees.edit.family')}
      submitLabel={request ? t('web.hr.profile_changes.request.send') : undefined}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      width={720}
      initialValues={{
        note: request?.note ?? undefined,
        members: (request?.initial ?? employee.familyMembers).map((m) => ({
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
      {request && <NoteField />}
    </FormDrawer>
  );
};
