import { Tree } from 'antd';
import { useMemo } from 'react';
import type { PermissionModuleNode } from '@/shared/api/generated/model';
import { toPermissions, toTreeData } from './permissionTree';

interface PermissionTreeFieldProps {
  modules: PermissionModuleNode[];
  /** Permission codes of the role (Form.Item value). */
  value?: string[];
  onChange?: (value: string[]) => void;
  disabled?: boolean;
}

/** The permission matrix: a checkable module → resource → permission tree bound to a list of codes. */
export const PermissionTreeField = ({ modules, value = [], onChange, disabled }: PermissionTreeFieldProps) => {
  const treeData = useMemo(() => toTreeData(modules), [modules]);
  return (
    <Tree
      checkable
      selectable={false}
      defaultExpandAll
      disabled={disabled}
      treeData={treeData}
      checkedKeys={value}
      onCheck={(checked) => onChange?.(toPermissions(Array.isArray(checked) ? checked : checked.checked, modules))}
    />
  );
};
