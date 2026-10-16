import type { TFunction } from 'i18next';
import { describe, expect, it } from 'vitest';
import type { MenuItemResponse } from '@/shared/api/generated/model';
import { quickLinks } from './dashboardLinks';

const t = ((key: string) => key) as unknown as TFunction;

const leaf = (key: string, path: string): MenuItemResponse => ({
  key,
  labelKey: `web.${key}`,
  path,
  icon: null,
  children: null,
  ai: false,
});

const group = (key: string, children: MenuItemResponse[]): MenuItemResponse => ({
  key,
  labelKey: `web.${key}`,
  path: null,
  icon: null,
  children,
  ai: false,
});

describe('quickLinks', () => {
  it('offers the preferred screens of the menu in order, then the profile', () => {
    const menu = [
      leaf('home', '/'),
      group('organization', [leaf('branches', '/organization/branches'), leaf('companies', '/organization/companies')]),
      group('approvals', [leaf('approval-inbox', '/approvals')]),
      group('system', [leaf('system-info', '/system/info')]),
    ];

    expect(quickLinks(menu, t).map((link) => link.path)).toEqual([
      '/approvals',
      '/organization/companies',
      '/organization/branches',
      '/profile',
    ]);
  });

  it('shows at most six screens', () => {
    const menu = [
      group('approvals', [leaf('approval-inbox', '/approvals'), leaf('delegations', '/approvals/delegations')]),
      group('organization', [leaf('companies', '/a'), leaf('branches', '/b')]),
      group('access', [leaf('users', '/c'), leaf('roles', '/d')]),
      group('configuration', [leaf('settings', '/e'), leaf('document-templates', '/f')]),
    ];

    const links = quickLinks(menu, t);

    expect(links).toHaveLength(7);
    expect(links.at(-1)?.path).toBe('/profile');
  });
});
