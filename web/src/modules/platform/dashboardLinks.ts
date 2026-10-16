import type { TFunction } from 'i18next';
import { flattenMenu, type MenuEntry } from '@/app/shell/menuItems';
import type { MenuItemResponse } from '@/shared/api/generated/model';

/** Menu entries offered on the home page, most used first; only those in the user's menu are shown. */
const PREFERRED = [
  'approval-inbox',
  'delegations',
  'companies',
  'branches',
  'users',
  'roles',
  'cv-analysis',
  'document-templates',
  'settings',
];

const MAX_LINKS = 6;

/**
 * Quick links of the home page (PLAN P13): the preferred screens the user may open, then their profile. The menu
 * is already filtered by permission, so a link never leads to a page the user cannot see.
 */
export const quickLinks = (menu: MenuItemResponse[], t: TFunction): MenuEntry[] => {
  const entries = flattenMenu(menu, t);
  const preferred = PREFERRED.flatMap((key) => entries.filter((entry) => entry.key === key)).slice(0, MAX_LINKS);
  return [...preferred, { key: 'profile', path: '/profile', label: t('web.shell.user.profile'), icon: 'user' }];
};
