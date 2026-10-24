import { Col, Form, Row, Select, TreeSelect } from 'antd';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { platformPermissions } from '@/modules/platform/permissions';
import { useGetOrgUnitTree } from '@/shared/api/generated/endpoints/hr/hr';
import { useGetCustomFieldForm, useListBranches } from '@/shared/api/generated/endpoints/platform/platform';
import { CriterionKind, EmploymentType } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { EMPLOYEE_ENTITY } from '../employees/employeeFilters';
import { enumOptions } from '../employees/forms/formValues';
import { buildUnitTree, toUnitOptions } from '../organization/unitTree';

const LOOKUP_SIZE = 100;

interface CriterionFieldsProps {
  /** Path of the criterion in the form, e.g. ['rules', 0, 'criteria', 1]. */
  name: (string | number)[];
  /** Where it sits inside a Form.List, relative to the list item, e.g. [1]. */
  field: (string | number)[];
  /** The company the criterion's branches and units belong to; without it they are typed as ids. */
  companyId?: string;
}

/**
 * One criterion of an assignment rule or an extra day rule (D41): what it looks at and the values that match. Branches,
 * units and employment types are picked; levels and custom field values are typed.
 */
export const CriterionFields = ({ name, field, companyId }: CriterionFieldsProps) => {
  const { t } = useTranslation();
  const form = Form.useFormInstance();
  const kind = Form.useWatch<CriterionKind | undefined>([...name, 'kind'], form);
  const canReadBranches = useHasPermission(platformPermissions.branchesRead);
  const unitKind = kind === CriterionKind.OrgUnit || kind === CriterionKind.OrgUnitTree;
  const tree = useGetOrgUnitTree({ companyId }, { query: { enabled: !!companyId && unitKind } });
  const branches = useListBranches(
    { companyId, isActive: true, pageSize: LOOKUP_SIZE },
    { query: { enabled: !!companyId && canReadBranches && kind === CriterionKind.Branch } },
  );
  const customFields = useGetCustomFieldForm(
    { entityType: EMPLOYEE_ENTITY, companyId },
    { query: { enabled: kind === CriterionKind.CustomField } },
  );
  const unitOptions = useMemo(() => toUnitOptions(buildUnitTree(tree.data?.units ?? [])), [tree.data]);
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const values = () => {
    switch (kind) {
      case CriterionKind.EmploymentType:
        return <Select mode="multiple" options={enumOptions(EmploymentType, 'web.hr.employment_type.', t)} />;
      case CriterionKind.OrgUnit:
      case CriterionKind.OrgUnitTree:
        return companyId ? (
          <TreeSelect
            multiple
            treeDefaultExpandAll
            showSearch
            treeNodeFilterProp="title"
            loading={tree.isLoading}
            treeData={unitOptions}
          />
        ) : (
          <Select mode="tags" open={false} />
        );
      case CriterionKind.Branch:
        return companyId && canReadBranches ? (
          <Select
            mode="multiple"
            showSearch={{ optionFilterProp: 'label' }}
            loading={branches.isFetching}
            options={(branches.data?.items ?? []).map((b) => ({ value: b.id, label: `${b.name} (${b.code})` }))}
          />
        ) : (
          <Select mode="tags" open={false} />
        );
      default:
        return <Select mode="tags" open={false} tokenSeparators={[',']} />;
    }
  };

  return (
    <Row gutter={8}>
      <Col xs={24} md={kind === CriterionKind.CustomField ? 6 : 8}>
        <Form.Item name={[...field, 'kind']} label={t('web.hr.leave.criteria.kind')} rules={required}>
          <Select
            options={enumOptions(CriterionKind, 'web.hr.leave.criteria.kinds.', t)}
            onChange={() => form.setFieldValue([...name, 'values'], [])}
          />
        </Form.Item>
      </Col>
      {kind === CriterionKind.CustomField && (
        <Col xs={24} md={6}>
          <Form.Item name={[...field, 'fieldCode']} label={t('web.hr.leave.criteria.field')} rules={required}>
            <Select
              showSearch={{ optionFilterProp: 'label' }}
              loading={customFields.isFetching}
              options={(customFields.data ?? []).map((f) => ({ value: f.code, label: f.label }))}
            />
          </Form.Item>
        </Col>
      )}
      <Col xs={24} md={kind === CriterionKind.CustomField ? 12 : 16}>
        <Form.Item
          name={[...field, 'values']}
          label={t('web.hr.leave.criteria.values')}
          tooltip={t('web.hr.leave.criteria.values_hint')}
          rules={[{ required: true, type: 'array', min: 1, message: t('web.platform.fields.required') }]}
        >
          {values()}
        </Form.Item>
      </Col>
    </Row>
  );
};
