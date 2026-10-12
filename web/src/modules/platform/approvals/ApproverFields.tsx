import { Col, Form, Input, Row, Select, type FormInstance } from 'antd';
import { useTranslation } from 'react-i18next';
import { platformPermissions } from '@/modules/platform/permissions';
import { useListRoles } from '@/shared/api/generated/endpoints/platform/platform';
import type { ApprovalFactItem, ApproverResolverItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { UserPicker } from './UserPicker';

interface ApproverFieldsProps {
  /** Name of the rule's fields where they render: inside a Form.List relative to the list item, e.g. [0, 'approver']. */
  name: (string | number)[];
  /** Full form path of the rule, e.g. ['steps', 0, 'approver'] or ['fallback']. */
  path: (string | number)[];
  form: FormInstance;
  resolvers: ApproverResolverItem[];
  facts: ApprovalFactItem[];
  required?: boolean;
  label: string;
}

/** Resolver and its parameters (§2.4): users, roles and facts are picked; other parameters are typed in. */
export const ApproverFields = ({ name, path, form, resolvers, facts, required = true, label }: ApproverFieldsProps) => {
  const { t } = useTranslation();
  const resolverKey = Form.useWatch([...path, 'resolver'], form) as string | undefined;
  const resolver = resolvers.find((r) => r.key === resolverKey);

  return (
    <Row gutter={12}>
      <Col xs={24} md={10}>
        <Form.Item
          name={[...name, 'resolver']}
          label={label}
          rules={required ? [{ required: true, message: t('web.platform.fields.required') }] : undefined}
        >
          <Select
            allowClear={!required}
            placeholder={t('web.approvals.flows.resolver')}
            options={resolvers.map((r) => ({ value: r.key, label: r.name }))}
            // Parameters of another resolver do not apply
            onChange={() => form.setFieldValue([...path, 'parameters'], {})}
          />
        </Form.Item>
      </Col>
      {resolver?.parameters.map((parameter) => (
        <Col xs={24} md={14} key={parameter.name}>
          <Form.Item
            name={[...name, 'parameters', parameter.name]}
            label={parameter.label}
            rules={parameter.required ? [{ required: true, message: t('web.platform.fields.required') }] : undefined}
          >
            <ParameterInput name={parameter.name} facts={facts} label={parameter.label} />
          </Form.Item>
        </Col>
      ))}
    </Row>
  );
};

interface ParameterInputProps {
  name: string;
  facts: ApprovalFactItem[];
  label: string;
  value?: string;
  onChange?: (value: string | undefined) => void;
}

const ParameterInput = ({ name, facts, label, value, onChange }: ParameterInputProps) => {
  switch (name) {
    case 'userId':
      return <UserPicker value={value} onChange={onChange} ariaLabel={label} />;
    case 'roleId':
      return <RolePicker value={value} onChange={onChange} label={label} />;
    case 'factName':
      return (
        <Select
          value={value}
          onChange={onChange}
          aria-label={label}
          options={facts.filter((f) => f.type === 'Identifier').map((f) => ({ value: f.name, label: f.label }))}
        />
      );
    default:
      return (
        <Input value={value} onChange={(event) => onChange?.(event.target.value || undefined)} aria-label={label} />
      );
  }
};

/** Roles to pick from; without the right to read roles their id is typed in. */
const RolePicker = ({
  value,
  onChange,
  label,
}: {
  value?: string;
  onChange?: (value: string | undefined) => void;
  label: string;
}) => {
  const canRead = useHasPermission(platformPermissions.rolesRead);
  const roles = useListRoles({ pageSize: 200 }, { query: { enabled: canRead } });

  if (!canRead) {
    return <Input value={value} onChange={(event) => onChange?.(event.target.value || undefined)} aria-label={label} />;
  }

  return (
    <Select
      showSearch={{ optionFilterProp: 'label' }}
      value={value}
      onChange={onChange}
      aria-label={label}
      loading={roles.isLoading}
      options={(roles.data?.items ?? []).map((role) => ({ value: role.id, label: role.name }))}
    />
  );
};
