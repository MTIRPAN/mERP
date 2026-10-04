import { theme, type ThemeConfig } from 'antd';

/**
 * Visual language: corporate and calm (neutral surfaces, dense tables, restrained colour) with an "AI product"
 * accent — an indigo/violet/cyan gradient used sparingly for the brand mark, the assistant entry point and the
 * active navigation indicator.
 */
export const brand = {
  primary: '#4F46E5',
  gradient: 'linear-gradient(135deg, #6366F1 0%, #8B5CF6 50%, #06B6D4 100%)',
  gradientSoft:
    'linear-gradient(135deg, rgba(99,102,241,0.12) 0%, rgba(139,92,246,0.10) 50%, rgba(6,182,212,0.10) 100%)',
} as const;

const shared: ThemeConfig = {
  token: {
    colorPrimary: brand.primary,
    colorInfo: brand.primary,
    colorSuccess: '#10B981',
    colorWarning: '#F59E0B',
    colorError: '#EF4444',
    borderRadius: 8,
    borderRadiusLG: 12,
    fontFamily:
      "Inter, 'Segoe UI Variable', 'Segoe UI', system-ui, -apple-system, Roboto, 'Helvetica Neue', Arial, sans-serif",
    fontSize: 14,
    controlHeight: 34,
    wireframe: false,
  },
  components: {
    Layout: { headerHeight: 56, headerPadding: '0 20px' },
    Menu: {
      itemBorderRadius: 8,
      itemMarginInline: 8,
      iconSize: 16,
      collapsedIconSize: 16,
      subMenuItemBg: 'transparent',
    },
    Card: { headerFontSize: 15 },
    Table: { cellPaddingBlock: 10 },
    Button: { fontWeight: 500 },
  },
};

export const lightTheme: ThemeConfig = {
  ...shared,
  algorithm: theme.defaultAlgorithm,
  token: {
    ...shared.token,
    colorBgLayout: '#F5F6FA',
    colorBgContainer: '#FFFFFF',
    colorBorderSecondary: '#ECEEF3',
    colorTextBase: '#0F172A',
  },
  components: {
    ...shared.components,
    Layout: { ...shared.components?.Layout, headerBg: '#FFFFFF', siderBg: '#FFFFFF', bodyBg: '#F5F6FA' },
    Menu: {
      ...shared.components?.Menu,
      itemBg: 'transparent',
      itemSelectedBg: '#EEF0FF',
      itemSelectedColor: brand.primary,
    },
    Table: { ...shared.components?.Table, headerBg: '#F8F9FC' },
  },
};

export const darkTheme: ThemeConfig = {
  ...shared,
  algorithm: theme.darkAlgorithm,
  token: {
    ...shared.token,
    colorPrimary: '#6D6AF0',
    colorInfo: '#6D6AF0',
    colorBgLayout: '#0B0D14',
    colorBgContainer: '#12141C',
    colorBgElevated: '#181B25',
    colorBorderSecondary: '#232736',
    colorTextBase: '#E5E7EB',
  },
  components: {
    ...shared.components,
    Layout: { ...shared.components?.Layout, headerBg: '#12141C', siderBg: '#12141C', bodyBg: '#0B0D14' },
    Menu: {
      ...shared.components?.Menu,
      itemBg: 'transparent',
      itemSelectedBg: 'rgba(109,106,240,0.16)',
      itemSelectedColor: '#A5A3FF',
    },
    Table: { ...shared.components?.Table, headerBg: '#161924' },
  },
};
