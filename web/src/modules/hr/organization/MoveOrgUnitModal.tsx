import { useQueryClient } from '@tanstack/react-query';
import { Alert, Form, Modal, Radio, TreeSelect } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import {
  getGetOrgUnitQueryKey,
  getGetOrgUnitTreeQueryKey,
  getListOrgUnitsQueryKey,
  useGetOrgUnit,
  useMoveOrgUnit,
} from '@/shared/api/generated/endpoints/hr/hr';
import type { OrgUnitTreeNode } from '@/shared/api/generated/model';
import { buildUnitTree, subtreeIds, toUnitOptions } from './unitTree';

type Target = 'unit' | 'top';

interface MoveValues {
  target: Target;
  parentId?: string;
}

interface MoveOrgUnitModalProps {
  open: boolean;
  onClose: () => void;
  unitId: string;
  /** The company's units; the unit and its sub units cannot be picked. */
  units: OrgUnitTreeNode[];
}

/** Moves a unit with its sub units under another unit of the company or to the top (D37). */
export const MoveOrgUnitModal = ({ open, onClose, unitId, units }: MoveOrgUnitModalProps) => {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<MoveValues>();
  const [saving, setSaving] = useState(false);
  const unit = useGetOrgUnit(unitId, { query: { enabled: open } });
  const move = useMoveOrgUnit();
  const target = Form.useWatch('target', form);
  const options = useMemo(() => toUnitOptions(buildUnitTree(units), subtreeIds(units, unitId)), [units, unitId]);

  const submit = async (values: MoveValues) => {
    if (!unit.data) {
      return;
    }

    setSaving(true);
    try {
      await move.mutateAsync({
        id: unitId,
        data: { version: unit.data.version, parentId: values.target === 'top' ? null : (values.parentId ?? null) },
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: getGetOrgUnitTreeQueryKey() }),
        queryClient.invalidateQueries({ queryKey: getListOrgUnitsQueryKey() }),
        queryClient.invalidateQueries({ queryKey: getGetOrgUnitQueryKey(unitId) }),
      ]);
      onClose();
    } catch (error) {
      if (!applyApiErrorsToForm(form, error)) {
        throw error;
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      title={t('web.hr.org.move_title')}
      open={open}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('web.hr.org.move')}
      cancelText={t('web.common.cancel')}
      confirmLoading={saving}
      okButtonProps={{ disabled: !unit.data }}
      destroyOnHidden
    >
      <Form<MoveValues> form={form} layout="vertical" initialValues={{ target: 'unit' }} onFinish={submit}>
        <Alert type="info" showIcon title={t('web.hr.org.move_hint')} style={{ marginBottom: 16 }} />
        <Form.Item name="target">
          <Radio.Group
            options={[
              { value: 'unit', label: t('web.hr.org.move_target') },
              { value: 'top', label: t('web.hr.org.fields.parent_top') },
            ]}
          />
        </Form.Item>
        {target !== 'top' && (
          <Form.Item
            name="parentId"
            label={t('web.hr.org.move_target')}
            rules={[{ required: true, message: t('web.platform.fields.required') }]}
          >
            <TreeSelect showSearch treeNodeFilterProp="title" treeDefaultExpandAll treeData={options} />
          </Form.Item>
        )}
      </Form>
    </Modal>
  );
};
