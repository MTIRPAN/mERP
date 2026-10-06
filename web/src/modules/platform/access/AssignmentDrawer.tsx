import { useQueryClient } from '@tanstack/react-query';
import { Col, DatePicker, Descriptions, Form, Row, Select, Skeleton } from 'antd';
import type { Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  getGetMeQueryKey,
  getGetUserQueryKey,
  getListUsersQueryKey,
  useAssignRole,
  useChangeAssignment,
  useListRoles,
  useListScopeTypes,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { AssignmentItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { scopeText } from './assignments';
import { ScopeFields, type ScopeValues } from './ScopeFields';
import { fromDateOnly, toDateOnly } from './validity';

interface AssignmentValues extends ScopeValues {
  roleId?: string;
  validFrom?: Dayjs | null;
  validTo?: Dayjs | null;
}

interface AssignmentDrawerProps {
  userId: string;
  /** Assignment whose period to change; undefined assigns a new role. */
  assignment?: AssignmentItem;
  open: boolean;
  onClose: () => void;
}

/** Gives the user a role in a data scope, or changes the period of an existing assignment. */
export const AssignmentDrawer = ({ userId, assignment, open, onClose }: AssignmentDrawerProps) => {
  const { t } = useTranslation();
  const [form] = Form.useForm<AssignmentValues>();
  const queryClient = useQueryClient();
  const roles = useListRoles({ pageSize: 200 }, { query: { enabled: open && !assignment } });
  const scopeTypes = useListScopeTypes({ query: { enabled: open && !assignment } });
  const assign = useAssignRole();
  const change = useChangeAssignment();

  const submit = async (values: AssignmentValues) => {
    const period = { validFrom: toDateOnly(values.validFrom), validTo: toDateOnly(values.validTo) };
    if (assignment) {
      await change.mutateAsync({ id: userId, assignmentId: assignment.id, data: period });
    } else {
      const type = scopeTypes.data?.find((item) => item.code === values.scopeType);
      await assign.mutateAsync({
        id: userId,
        data: {
          roleId: values.roleId ?? '',
          scopeType: values.scopeType ?? null,
          scopeId: type?.requiresId ? (values.scopeId ?? null) : null,
          includeSubUnits: !!type?.supportsSubUnits && !!values.includeSubUnits,
          ...period,
        },
      });
    }

    await queryClient.invalidateQueries({ queryKey: getGetUserQueryKey(userId) });
    await queryClient.invalidateQueries({ queryKey: getListUsersQueryKey() });
    // The signed-in user may have changed their own roles
    await queryClient.invalidateQueries({ queryKey: getGetMeQueryKey() });
  };

  const loading = !assignment && (!roles.data || !scopeTypes.data);

  return (
    <FormDrawer<AssignmentValues>
      key={assignment?.id ?? 'new'}
      form={form}
      title={t(assignment ? 'web.platform.assignments.edit' : 'web.platform.assignments.new')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      initialValues={
        assignment
          ? { validFrom: fromDateOnly(assignment.validFrom), validTo: fromDateOnly(assignment.validTo) }
          : { scopeType: 'Tenant', includeSubUnits: false }
      }
    >
      {loading ? (
        <Skeleton active />
      ) : (
        <>
          {assignment ? (
            <Descriptions
              column={1}
              size="small"
              style={{ marginBottom: 16 }}
              items={[
                { key: 'role', label: t('web.platform.assignments.role'), children: assignment.roleName },
                { key: 'scope', label: t('web.platform.assignments.scope'), children: scopeText(assignment, t) },
              ]}
            />
          ) : (
            <>
              <Form.Item
                name="roleId"
                label={t('web.platform.assignments.role')}
                rules={[{ required: true, message: t('web.platform.fields.required') }]}
              >
                <Select
                  showSearch={{ optionFilterProp: 'label' }}
                  options={roles.data?.items.map((role) => ({ value: role.id, label: role.name }))}
                />
              </Form.Item>
              <ScopeFields form={form} scopeTypes={scopeTypes.data ?? []} />
            </>
          )}
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item
                name="validFrom"
                label={t('web.platform.assignments.valid_from')}
                tooltip={t('web.platform.assignments.valid_hint')}
              >
                <DatePicker style={{ width: '100%' }} format="L" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="validTo" label={t('web.platform.assignments.valid_to')}>
                <DatePicker style={{ width: '100%' }} format="L" />
              </Form.Item>
            </Col>
          </Row>
        </>
      )}
    </FormDrawer>
  );
};
