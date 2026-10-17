import { useQueryClient } from '@tanstack/react-query';
import { Col, DatePicker, Form, Input, Row, Select, Skeleton, TreeSelect } from 'antd';
import type { Dayjs } from 'dayjs';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { fromDateOnly, toDateOnly } from '@/modules/platform/access/validity';
import {
  getGetOrgUnitQueryKey,
  getGetOrgUnitTreeQueryKey,
  getListOrgUnitsQueryKey,
  useCreateOrgUnit,
  useGetOrgUnit,
  useListPositions,
  useUpdateOrgUnit,
} from '@/shared/api/generated/endpoints/hr/hr';
import { useListBranches } from '@/shared/api/generated/endpoints/platform/platform';
import { OrgUnitType, PositionStatus, type OrgUnitDetails, type OrgUnitTreeNode } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { buildUnitTree, toUnitOptions } from './unitTree';

interface OrgUnitFormValues {
  parentId?: string | null;
  code?: string;
  name?: string;
  type?: OrgUnitType;
  managerPositionId?: string | null;
  costCenterCode?: string;
  branchId?: string | null;
  validFrom?: Dayjs | null;
  validTo?: Dayjs | null;
}

const blank = (value?: string) => (value?.trim() ? value.trim() : null);

const toValues = (unit: OrgUnitDetails | undefined, parentId?: string): OrgUnitFormValues =>
  unit
    ? {
        code: unit.code,
        name: unit.name,
        type: unit.type,
        managerPositionId: unit.managerPositionId,
        costCenterCode: unit.costCenterCode ?? undefined,
        branchId: unit.branchId,
        validFrom: fromDateOnly(unit.validFrom),
        validTo: fromDateOnly(unit.validTo),
      }
    : { parentId: parentId ?? null, type: parentId ? OrgUnitType.Department : OrgUnitType.Directorate };

interface OrgUnitFormDrawerProps {
  open: boolean;
  onClose: () => void;
  companyId: string;
  /** The company's units, for the parent picker. */
  units: OrgUnitTreeNode[];
  /** Unit to edit; undefined creates one. */
  unitId?: string;
  /** Parent of a new unit; undefined creates a top unit. */
  parentId?: string;
  onSaved?: (unitId: string) => void;
}

/**
 * Create or edit a unit (PLAN H1b). The code is fixed after creation and the parent changes with the move action,
 * so editing offers the manager position instead: one of the unit's open positions.
 */
export const OrgUnitFormDrawer = ({
  open,
  onClose,
  companyId,
  units,
  unitId,
  parentId,
  onSaved,
}: OrgUnitFormDrawerProps) => {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const editing = !!unitId;
  const unit = useGetOrgUnit(unitId ?? '', { query: { enabled: open && editing } });
  const positions = useListPositions({ orgUnitId: unitId, pageSize: 200 }, { query: { enabled: open && editing } });
  const branches = useListBranches({ companyId, pageSize: 200 }, { query: { enabled: open } });
  const create = useCreateOrgUnit();
  const update = useUpdateOrgUnit();
  const parents = useMemo(() => toUnitOptions(buildUnitTree(units)), [units]);

  const submit = async (values: OrgUnitFormValues) => {
    const fields = {
      name: blank(values.name),
      type: values.type ?? null,
      costCenterCode: blank(values.costCenterCode),
      branchId: values.branchId ?? null,
      validFrom: toDateOnly(values.validFrom),
      validTo: toDateOnly(values.validTo),
    };

    let savedId: string;
    if (editing && unit.data) {
      await update.mutateAsync({
        id: unit.data.id,
        data: { ...fields, version: unit.data.version, managerPositionId: values.managerPositionId ?? null },
      });
      savedId = unit.data.id;
    } else {
      const created = await create.mutateAsync({
        data: { ...fields, companyId, parentId: values.parentId ?? null, code: blank(values.code) },
      });
      savedId = created.id;
    }

    await Promise.all([
      queryClient.invalidateQueries({ queryKey: getGetOrgUnitTreeQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getListOrgUnitsQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getGetOrgUnitQueryKey(savedId) }),
    ]);
    onSaved?.(savedId);
  };

  const required = [{ required: true, message: t('web.platform.fields.required') }];
  const branchOptions = (branches.data?.items ?? [])
    .filter((b) => b.isActive || b.id === unit.data?.branchId)
    .map((b) => ({ value: b.id, label: `${b.name} (${b.code})` }));
  const managerOptions = (positions.data?.items ?? [])
    .filter((p) => p.status !== PositionStatus.Closed)
    .map((p) => ({ value: p.id, label: `${p.code} · ${p.jobName}` }));

  return (
    <FormDrawer<OrgUnitFormValues>
      key={unit.data ? `${unit.data.id}-${unit.data.version}` : `new-${parentId ?? ''}`}
      title={t(editing ? 'web.hr.org.edit_unit' : parentId ? 'web.hr.org.new_sub_unit' : 'web.hr.org.new_unit')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      initialValues={toValues(unit.data, parentId)}
      width={600}
    >
      {editing && !unit.data ? (
        <Skeleton active />
      ) : (
        <>
          {!editing && (
            <Form.Item name="parentId" label={t('web.hr.org.fields.parent')}>
              <TreeSelect
                allowClear
                showSearch
                treeNodeFilterProp="title"
                treeDefaultExpandAll
                placeholder={t('web.hr.org.fields.parent_top')}
                treeData={parents}
              />
            </Form.Item>
          )}
          <Row gutter={12}>
            <Col span={8}>
              <Form.Item
                name="code"
                label={t('web.platform.fields.code')}
                tooltip={t('web.platform.fields.code_hint')}
                rules={required}
              >
                <Input maxLength={20} disabled={editing} style={{ textTransform: 'uppercase' }} />
              </Form.Item>
            </Col>
            <Col span={16}>
              <Form.Item name="name" label={t('web.hr.org.fields.name')} rules={required}>
                <Input maxLength={150} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item name="type" label={t('web.hr.org.fields.type')} rules={required}>
                <Select
                  options={Object.values(OrgUnitType).map((type) => ({
                    value: type,
                    label: t(`web.hr.org_unit_type.${type}`),
                  }))}
                />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="costCenterCode" label={t('web.hr.org.fields.cost_center')}>
                <Input maxLength={30} />
              </Form.Item>
            </Col>
          </Row>
          {editing && (
            <Form.Item
              name="managerPositionId"
              label={t('web.hr.org.fields.manager')}
              extra={t('web.hr.org.fields.manager_hint')}
            >
              <Select
                allowClear
                showSearch={{ optionFilterProp: 'label' }}
                loading={positions.isLoading}
                placeholder={t('web.hr.org.fields.no_manager')}
                options={managerOptions}
              />
            </Form.Item>
          )}
          <Form.Item name="branchId" label={t('web.hr.org.fields.branch')}>
            <Select
              allowClear
              showSearch={{ optionFilterProp: 'label' }}
              loading={branches.isLoading}
              options={branchOptions}
            />
          </Form.Item>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item name="validFrom" label={t('web.hr.org.fields.valid_from')}>
                <DatePicker style={{ width: '100%' }} format="L" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="validTo" label={t('web.hr.org.fields.valid_to')}>
                <DatePicker style={{ width: '100%' }} format="L" />
              </Form.Item>
            </Col>
          </Row>
        </>
      )}
    </FormDrawer>
  );
};
