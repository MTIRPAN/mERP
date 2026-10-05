import type { TFunction } from 'i18next';
import { describe, expect, it } from 'vitest';
import type { MenuItemResponse } from '@/shared/api/generated/model';
import { filterMenuEntries, flattenMenu, selectedMenuKey } from './menuItems';

const texts: Record<string, string> = {
  'web.home': 'Ana sayfa',
  'web.system': 'Sistem',
  'web.info': 'Sistem bilgisi',
  'web.jobs': 'İşler',
};
const t = ((key: string) => texts[key] ?? key) as unknown as TFunction;

const menu: MenuItemResponse[] = [
  { key: 'home', labelKey: 'web.home', path: '/', icon: 'home', children: null, ai: false },
  {
    key: 'system',
    labelKey: 'web.system',
    path: null,
    icon: 'settings',
    ai: false,
    children: [
      { key: 'info', labelKey: 'web.info', path: '/system/info', icon: 'info', children: null, ai: false },
      { key: 'jobs', labelKey: 'web.jobs', path: '/system/jobs', icon: 'schedule', children: null, ai: false },
    ],
  },
];

describe('menu helpers', () => {
  it('flattens leaves with their section', () => {
    const entries = flattenMenu(menu, t);

    expect(entries.map((e) => e.path)).toEqual(['/', '/system/info', '/system/jobs']);
    expect(entries[1]?.section).toBe('Sistem');
  });

  it('searches case-insensitively with Turkish rules', () => {
    const entries = flattenMenu(menu, t);

    // "işler" must match "İşler": invariant lower-casing would turn "İ" into "i̇"
    expect(filterMenuEntries(entries, 'işler').map((e) => e.key)).toEqual(['jobs']);
    expect(filterMenuEntries(entries, 'SİSTEM BİLGİ').map((e) => e.key)).toEqual(['info']);
    expect(filterMenuEntries(entries, '  ')).toHaveLength(3);
  });

  it('selects the most specific menu entry for nested paths', () => {
    const entries = flattenMenu(menu, t);

    expect(selectedMenuKey('/system/info', entries)).toBe('/system/info');
    expect(selectedMenuKey('/system/info/details', entries)).toBe('/system/info');
    expect(selectedMenuKey('/', entries)).toBe('/');
    expect(selectedMenuKey('/unknown', entries)).toBeUndefined();
  });
});
