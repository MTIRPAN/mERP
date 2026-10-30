import {
  ApartmentOutlined,
  AppstoreOutlined,
  CalendarOutlined,
  BankOutlined,
  AuditOutlined,
  BarChartOutlined,
  CarryOutOutlined,
  CoffeeOutlined,
  CommentOutlined,
  FileProtectOutlined,
  ProfileOutlined,
  RocketOutlined,
  FileSearchOutlined,
  NotificationOutlined,
  UsergroupAddOutlined,
  CheckSquareOutlined,
  ContactsOutlined,
  ControlOutlined,
  DesktopOutlined,
  FileTextOutlined,
  MailOutlined,
  InboxOutlined,
  NodeIndexOutlined,
  UserSwitchOutlined,
  FieldNumberOutlined,
  FileDoneOutlined,
  FieldTimeOutlined,
  FormOutlined,
  HistoryOutlined,
  HomeOutlined,
  IdcardOutlined,
  InfoCircleOutlined,
  LockOutlined,
  SafetyCertificateOutlined,
  ScheduleOutlined,
  SettingOutlined,
  SolutionOutlined,
  ShopOutlined,
  TranslationOutlined,
  StarOutlined,
  TagsOutlined,
  TeamOutlined,
  UserOutlined,
} from '@ant-design/icons';
import type { MenuProps } from 'antd';
import type { TFunction } from 'i18next';
import type { ReactNode } from 'react';
import type { MenuItemResponse } from '@/shared/api/generated/model';

const icons: Record<string, ReactNode> = {
  home: <HomeOutlined />,
  settings: <SettingOutlined />,
  info: <InfoCircleOutlined />,
  schedule: <ScheduleOutlined />,
  ai: <StarOutlined />,
  recruitment: <TeamOutlined />,
  requisition: <UsergroupAddOutlined />,
  'my-openings': <RocketOutlined />,
  candidates: <FileSearchOutlined />,
  interviews: <CommentOutlined />,
  offers: <FileProtectOutlined />,
  opening: <NotificationOutlined />,
  hr: <IdcardOutlined />,
  employees: <ContactsOutlined />,
  'my-card': <IdcardOutlined />,
  'leave-settings': <CarryOutOutlined />,
  'my-leave': <CoffeeOutlined />,
  'leave-requests': <ProfileOutlined />,
  'leave-calendar': <CalendarOutlined />,
  'leave-reports': <BarChartOutlined />,
  'document-types': <FileDoneOutlined />,
  positions: <SolutionOutlined />,
  'job-titles': <TagsOutlined />,
  organization: <ApartmentOutlined />,
  company: <BankOutlined />,
  branch: <ShopOutlined />,
  access: <LockOutlined />,
  user: <UserOutlined />,
  role: <SafetyCertificateOutlined />,
  audit: <HistoryOutlined />,
  language: <TranslationOutlined />,
  numbering: <FieldNumberOutlined />,
  'custom-fields': <FormOutlined />,
  holidays: <CalendarOutlined />,
  'work-schedules': <FieldTimeOutlined />,
  approvals: <CheckSquareOutlined />,
  'approval-inbox': <InboxOutlined />,
  delegations: <UserSwitchOutlined />,
  'approval-flows': <NodeIndexOutlined />,
  'approval-requests': <AuditOutlined />,
  'document-templates': <FileTextOutlined />,
  'notification-templates': <MailOutlined />,
  configuration: <ControlOutlined />,
  system: <DesktopOutlined />,
};

export const menuIcon = (name: string | null): ReactNode => (name ? icons[name] : undefined) ?? <AppstoreOutlined />;

type AntdMenuItem = Required<MenuProps>['items'][number];

/** Server menu (already filtered by permission) → antd menu items; keys are paths for leaves. */
export const toAntdMenuItems = (items: MenuItemResponse[], t: TFunction): AntdMenuItem[] =>
  items.map((item) =>
    item.children?.length
      ? {
          key: item.key,
          icon: menuIcon(item.icon),
          label: t(item.labelKey),
          children: toAntdMenuItems(item.children, t),
        }
      : {
          key: item.path ?? item.key,
          icon: menuIcon(item.icon),
          // AI features carry the brand gradient (D23)
          label: item.ai ? <span className="merp-gradient-text">{t(item.labelKey)}</span> : t(item.labelKey),
        },
  );

export interface MenuEntry {
  key: string;
  path: string;
  label: string;
  section?: string;
  icon: string | null;
}

/** Leaves of the menu with translated labels, for the command palette. */
export const flattenMenu = (items: MenuItemResponse[], t: TFunction, section?: string): MenuEntry[] =>
  items.flatMap((item) =>
    item.children?.length
      ? flattenMenu(item.children, t, t(item.labelKey))
      : item.path
        ? [{ key: item.key, path: item.path, label: t(item.labelKey), section, icon: item.icon }]
        : [],
  );

const flattenPaths = (items: MenuItemResponse[]): string[] =>
  items.flatMap((item) => (item.children?.length ? flattenPaths(item.children) : item.path ? [item.path] : []));

/** Keys of the groups that hold the entry with this path, outermost first: the sidebar opens where the user is. */
export const openMenuKeys = (items: MenuItemResponse[], path: string | undefined): string[] => {
  if (!path) {
    return [];
  }

  for (const item of items) {
    if (item.children?.length && flattenPaths(item.children).includes(path)) {
      return [item.key, ...openMenuKeys(item.children, path)];
    }
  }

  return [];
};

/** Case-insensitive search that respects Turkish İ/ı (CLAUDE.md culture safety). */
export const filterMenuEntries = (entries: MenuEntry[], query: string, locale = 'tr-TR'): MenuEntry[] => {
  const needle = query.trim().toLocaleLowerCase(locale);
  if (!needle) {
    return entries;
  }

  return entries.filter((entry) => `${entry.section ?? ''} ${entry.label}`.toLocaleLowerCase(locale).includes(needle));
};

/** Key of the menu entry that best matches the current path. */
export const selectedMenuKey = (pathname: string, entries: MenuEntry[]): string | undefined =>
  entries
    .filter((entry) => entry.path === pathname || (entry.path !== '/' && pathname.startsWith(`${entry.path}/`)))
    .sort((a, b) => b.path.length - a.path.length)[0]?.path;
