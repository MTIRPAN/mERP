import { MenuFoldOutlined, MenuUnfoldOutlined } from '@ant-design/icons';
import { Button, Divider, Flex, Layout, Menu, Skeleton, Tooltip, theme } from 'antd';
import { Suspense, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Outlet, useLocation, useNavigate } from 'react-router';
import { useMe } from '@/app/me/meContext';
import { NotificationsHub } from '@/app/notifications/NotificationsHub';
import { NotificationBell } from '@/modules/platform/notifications/NotificationBell';
import { Brand } from './Brand';
import { CommandPalette } from './CommandPalette';
import {
  ApprovalsBadge,
  CompanySelector,
  LanguageSelector,
  SearchTrigger,
  ThemeSwitcher,
  UserMenu,
} from './HeaderControls';
import { flattenMenu, selectedMenuKey, toAntdMenuItems } from './menuItems';

const SIDER_STORAGE_KEY = 'merp.sider.collapsed';

export const AppLayout = () => {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const me = useMe();
  const navigate = useNavigate();
  const location = useLocation();
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(SIDER_STORAGE_KEY) === 'true');
  const [paletteOpen, setPaletteOpen] = useState(false);

  const menuItems = useMemo(() => toAntdMenuItems(me.menu, t), [me.menu, t]);
  const entries = useMemo(() => flattenMenu(me.menu, t), [me.menu, t]);
  const selected = selectedMenuKey(location.pathname, entries);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        setPaletteOpen(true);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  const toggle = () => {
    localStorage.setItem(SIDER_STORAGE_KEY, String(!collapsed));
    setCollapsed(!collapsed);
  };

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Layout.Sider
        className="merp-sider"
        width={248}
        collapsedWidth={72}
        collapsed={collapsed}
        trigger={null}
        style={{
          borderInlineEnd: `1px solid ${token.colorBorderSecondary}`,
          position: 'sticky',
          top: 0,
          height: '100vh',
        }}
      >
        <Brand collapsed={collapsed} />
        <Menu
          mode="inline"
          items={menuItems}
          selectedKeys={selected ? [selected] : []}
          defaultOpenKeys={me.menu.filter((item) => item.children?.length).map((item) => item.key)}
          onClick={({ key }) => void navigate(key)}
          style={{ borderInlineEnd: 'none', paddingTop: 8 }}
        />
      </Layout.Sider>
      <Layout>
        <Layout.Header
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 12,
            borderBottom: `1px solid ${token.colorBorderSecondary}`,
            position: 'sticky',
            top: 0,
            zIndex: 10,
          }}
        >
          <Tooltip title={collapsed ? t('web.shell.sider.expand') : t('web.shell.sider.collapse')}>
            <Button
              type="text"
              icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
              onClick={toggle}
              aria-label={collapsed ? t('web.shell.sider.expand') : t('web.shell.sider.collapse')}
            />
          </Tooltip>
          <CompanySelector />
          <SearchTrigger onOpen={() => setPaletteOpen(true)} />
          <Flex align="center" gap={4} style={{ marginInlineStart: 'auto' }}>
            <ThemeSwitcher />
            <Divider orientation="vertical" />
            <ApprovalsBadge />
            <NotificationBell />
            <LanguageSelector />
            <Divider orientation="vertical" />
            <UserMenu />
          </Flex>
        </Layout.Header>
        <Layout.Content style={{ padding: token.paddingLG }}>
          {/* Module pages are loaded lazily (AppRoutes) */}
          <Suspense fallback={<Skeleton active />}>
            <Outlet />
          </Suspense>
        </Layout.Content>
      </Layout>
      <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} entries={entries} />
      <NotificationsHub />
    </Layout>
  );
};
