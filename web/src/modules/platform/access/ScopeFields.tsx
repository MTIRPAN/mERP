import { Form, Select, Switch, type FormInstance } from 'antd';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import { useListBranches } from '@/shared/api/generated/endpoints/platform/platform';
import type { ScopeTypeItem } from '@/shared/api/generated/model';

export interface ScopeValues {
  scopeType?: string;
  /** Company whose branches the branch picker lists; not sent to the API. */
  scopeCompanyId?: string;
  scopeId?: string;
  includeSubUnits?: boolean;
}

/** Scope types whose record the web can pick. Types of later modules (OrgUnit) add their picker here. */
const pickableScopeTypes: ReadonlySet<string> = new Set(['Company', 'Branch']);

const BranchPicker = () => {
  const { t } = useTranslation();
  const companyId = Form.useWatch<string | undefined>('scopeCompanyId');
  const branches = useListBranches(
    { companyId, pageSize: 200, isActive: true, sort: 'name' },
    { query: { enabled: !!companyId } },
  );
  return (
    <Form.Item
      name="scopeId"
      label={t('web.platform.assignments.branch')}
      rules={[{ required: true, message: t('web.platform.fields.required') }]}
    >
      <Select
        showSearch={{ optionFilterProp: 'label' }}
        disabled={!companyId}
        loading={branches.isFetching}
        options={branches.data?.items.map((branch) => ({ value: branch.id, label: `${branch.code} · ${branch.name}` }))}
      />
    </Form.Item>
  );
};

interface ScopeFieldsProps {
  form: FormInstance;
  scopeTypes: ScopeTypeItem[];
}

/** Scope type with the record it points to (company or branch) and, where the type allows, sub-units. */
export const ScopeFields = ({ form, scopeTypes }: ScopeFieldsProps) => {
  const { t } = useTranslation();
  const me = useMe();
  const scopeType = Form.useWatch<string | undefined>('scopeType', form);
  const selected = scopeTypes.find((type) => type.code === scopeType);
  const companies = me.companies.map((company) => ({ value: company.id, label: company.name }));
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  return (
    <>
      <Form.Item name="scopeType" label={t('web.platform.assignments.scope')} rules={required}>
        <Select
          options={scopeTypes.map((type) => ({
            value: type.code,
            label: type.label,
            disabled: type.requiresId && !pickableScopeTypes.has(type.code),
          }))}
          onChange={() =>
            form.setFieldsValue({ scopeId: undefined, scopeCompanyId: undefined, includeSubUnits: false })
          }
        />
      </Form.Item>
      {scopeType === 'Company' && (
        <Form.Item name="scopeId" label={t('web.platform.assignments.company')} rules={required}>
          <Select showSearch={{ optionFilterProp: 'label' }} options={companies} />
        </Form.Item>
      )}
      {scopeType === 'Branch' && (
        <>
          <Form.Item name="scopeCompanyId" label={t('web.platform.assignments.company')} rules={required}>
            <Select
              showSearch={{ optionFilterProp: 'label' }}
              options={companies}
              onChange={() => form.setFieldsValue({ scopeId: undefined })}
            />
          </Form.Item>
          <BranchPicker />
        </>
      )}
      {selected?.supportsSubUnits && (
        <Form.Item
          name="includeSubUnits"
          label={t('web.platform.assignments.include_sub_units')}
          valuePropName="checked"
        >
          <Switch />
        </Form.Item>
      )}
    </>
  );
};
