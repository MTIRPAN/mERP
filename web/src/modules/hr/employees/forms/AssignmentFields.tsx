import { Col, Form, Input, Row, Select, TreeSelect, type FormInstance } from 'antd';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { platformPermissions } from '@/modules/platform/permissions';
import { useGetOrgUnitTree, useListPositions } from '@/shared/api/generated/endpoints/hr/hr';
import { useListBranches } from '@/shared/api/generated/endpoints/platform/platform';
import { PositionStatus } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { buildUnitTree, toUnitOptions } from '../../organization/unitTree';

const LOOKUP_SIZE = 100;

/** Values of the assignment fields; the unit only narrows the position list. */
export interface AssignmentValues {
  orgUnitId?: string;
  positionId?: string;
  branchId?: string;
  managerPositionId?: string;
  level?: string;
  costCenterCode?: string;
}

interface AssignmentFieldsProps {
  form: FormInstance;
  /** The employment's company; the lists wait for it. */
  companyId?: string;
  /** Lists load only while the form is shown. */
  enabled: boolean;
  /** Offer the manager override (yönetici ezmesi). */
  withManager?: boolean;
}

/**
 * Position, branch, manager override, level and cost center of an assignment (D38). The branch list needs
 * platform.branches.read, so the field is offered only with it; level and cost center default on the server.
 */
export const AssignmentFields = ({ form, companyId, enabled, withManager = false }: AssignmentFieldsProps) => {
  const { t } = useTranslation();
  const canReadBranches = useHasPermission(platformPermissions.branchesRead);
  const orgUnitId = Form.useWatch<string | undefined>('orgUnitId', form);
  const active = enabled && !!companyId;
  const tree = useGetOrgUnitTree({ companyId }, { query: { enabled: active } });
  const positions = useListPositions(
    { companyId, orgUnitId, includeSubUnits: true, status: PositionStatus.Active, pageSize: LOOKUP_SIZE },
    { query: { enabled: active } },
  );
  const managers = useListPositions(
    { companyId, status: PositionStatus.Active, pageSize: LOOKUP_SIZE },
    { query: { enabled: active && withManager } },
  );
  const branches = useListBranches(
    { companyId, isActive: true, pageSize: LOOKUP_SIZE },
    { query: { enabled: active && canReadBranches } },
  );
  const unitOptions = useMemo(() => toUnitOptions(buildUnitTree(tree.data?.units ?? [])), [tree.data]);
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  return (
    <>
      <Form.Item
        name="orgUnitId"
        label={t('web.hr.employees.fields.org_unit')}
        extra={t('web.hr.employees.org_unit_hint')}
      >
        <TreeSelect
          allowClear
          showSearch
          treeNodeFilterProp="title"
          treeDefaultExpandAll
          loading={tree.isLoading}
          treeData={unitOptions}
          onChange={() => form.setFieldsValue({ positionId: undefined })}
        />
      </Form.Item>
      <Form.Item name="positionId" label={t('web.hr.employees.fields.position')} rules={required}>
        <Select
          showSearch={{ optionFilterProp: 'label' }}
          loading={positions.isFetching}
          options={(positions.data?.items ?? []).map((p) => ({
            value: p.id,
            label: `${p.code} · ${p.jobName} · ${p.orgUnitName}${p.vacant > 0 ? '' : ` (${t('web.hr.employees.position_full')})`}`,
          }))}
        />
      </Form.Item>
      {withManager && (
        <Form.Item
          name="managerPositionId"
          label={t('web.hr.employees.fields.manager_override')}
          extra={t('web.hr.employees.manager_override_hint')}
        >
          <Select
            allowClear
            showSearch={{ optionFilterProp: 'label' }}
            loading={managers.isFetching}
            options={(managers.data?.items ?? []).map((p) => ({ value: p.id, label: `${p.code} · ${p.jobName}` }))}
          />
        </Form.Item>
      )}
      <Row gutter={12}>
        {canReadBranches && (
          <Col span={12}>
            <Form.Item name="branchId" label={t('web.hr.employees.fields.branch')}>
              <Select
                allowClear
                showSearch={{ optionFilterProp: 'label' }}
                loading={branches.isFetching}
                options={(branches.data?.items ?? []).map((b) => ({ value: b.id, label: `${b.name} (${b.code})` }))}
              />
            </Form.Item>
          </Col>
        )}
        <Col span={6}>
          <Form.Item name="level" label={t('web.hr.employees.fields.level')} tooltip={t('web.hr.employees.level_hint')}>
            <Input maxLength={10} />
          </Form.Item>
        </Col>
        <Col span={6}>
          <Form.Item
            name="costCenterCode"
            label={t('web.hr.employees.fields.cost_center')}
            tooltip={t('web.hr.employees.cost_center_hint')}
          >
            <Input maxLength={30} />
          </Form.Item>
        </Col>
      </Row>
    </>
  );
};
