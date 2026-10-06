import type { TreeDataNode } from 'antd';
import type { Key } from 'react';
import type { PermissionModuleNode } from '@/shared/api/generated/model';

// Group keys are prefixed so a resource ("hr.employees.sensitive") never clashes with a permission code.
const moduleKey = (code: string) => `module:${code}`;
const resourceKey = (code: string) => `resource:${code}`;

/** Permission catalog (module → resource → permission) as antd tree nodes; leaves are the permission codes. */
export const toTreeData = (modules: PermissionModuleNode[]): TreeDataNode[] =>
  modules.map((module) => ({
    key: moduleKey(module.code),
    title: module.label,
    children: module.resources.map((resource) => ({
      key: resourceKey(resource.code),
      title: resource.label,
      children: resource.permissions.map((permission) => ({
        key: permission.code,
        title: permission.label,
        isLeaf: true,
      })),
    })),
  }));

export const permissionCodes = (modules: PermissionModuleNode[]): Set<string> =>
  new Set(modules.flatMap((m) => m.resources.flatMap((r) => r.permissions.map((p) => p.code))));

/** Checked tree keys → sorted permission codes (group nodes dropped). */
export const toPermissions = (checked: readonly Key[], modules: PermissionModuleNode[]): string[] => {
  const codes = permissionCodes(modules);
  return checked
    .map(String)
    .filter((key) => codes.has(key))
    .sort();
};
