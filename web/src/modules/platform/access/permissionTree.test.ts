import { describe, expect, it } from 'vitest';
import type { PermissionModuleNode } from '@/shared/api/generated/model';
import { toPermissions, toTreeData } from './permissionTree';

const catalog: PermissionModuleNode[] = [
  {
    code: 'hr',
    label: 'İK',
    resources: [
      {
        code: 'hr.employees',
        label: 'Personel',
        permissions: [
          { code: 'hr.employees.read', label: 'Görüntüleme' },
          { code: 'hr.employees.sensitive', label: 'Hassas alanlar' },
        ],
      },
      {
        code: 'hr.employees.sensitive',
        label: 'Hassas alanlar',
        permissions: [{ code: 'hr.employees.sensitive.read', label: 'Görüntüleme' }],
      },
    ],
  },
  {
    code: 'platform',
    label: 'Platform',
    resources: [
      {
        code: 'platform.users',
        label: 'Kullanıcılar',
        permissions: [{ code: 'platform.users.read', label: 'Görüntüleme' }],
      },
    ],
  },
];

describe('permission tree', () => {
  it('keeps group keys apart from permission codes', () => {
    const keys = JSON.stringify(toTreeData(catalog));

    expect(keys).toContain('"key":"resource:hr.employees.sensitive"');
    expect(keys).toContain('"key":"hr.employees.sensitive"');
  });

  it('turns checked keys into sorted permission codes', () => {
    const checked = ['module:platform', 'platform.users.read', 'resource:hr.employees.sensitive', 'hr.employees.read'];

    expect(toPermissions(checked, catalog)).toEqual(['hr.employees.read', 'platform.users.read']);
  });
});
