import {
  BellOutlined,
  CheckSquareOutlined,
  DesktopOutlined,
  GlobalOutlined,
  LogoutOutlined,
  MoonOutlined,
  SearchOutlined,
  SunOutlined,
  UserOutlined,
} from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import {
  Avatar,
  Badge,
  Button,
  Dropdown,
  Empty,
  Flex,
  Popover,
  Segmented,
  Select,
  Tooltip,
  Typography,
  theme,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useAuth } from 'react-oidc-context';
import { useNavigate } from 'react-router';
import { setActiveCompanyId, useActiveCompanyId } from '@/app/company/companyStore';
import { useMe } from '@/app/me/meContext';
import { useCulturePreference } from '@/app/me/useCulturePreference';
import { useThemeMode, type ThemeMode } from '@/app/theme/themeModeContext';

export const SearchTrigger = ({ onOpen }: { onOpen: () => void }) => {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  return (
    <Button
      onClick={onOpen}
      style={{ width: 300, justifyContent: 'space-between', color: token.colorTextTertiary }}
      aria-label={t('web.shell.header.search')}
    >
      <Flex gap={8} align="center">
        <SearchOutlined />
        {t('web.shell.header.search')}
      </Flex>
      <Typography.Text keyboard type="secondary" style={{ fontSize: 11 }}>
        {t('web.shell.header.search_hint')}
      </Typography.Text>
    </Button>
  );
};

export const CompanySelector = () => {
  const { t } = useTranslation();
  const me = useMe();
  const activeCompanyId = useActiveCompanyId();
  const queryClient = useQueryClient();
  return (
    <Select
      style={{ minWidth: 200 }}
      value={activeCompanyId ?? ''}
      aria-label={t('web.shell.header.company')}
      disabled={me.companies.length === 0}
      options={[
        { value: '', label: t('web.shell.header.all_companies') },
        ...me.companies.map((company) => ({ value: company.id, label: company.name })),
      ]}
      onChange={(value: string) => {
        setActiveCompanyId(value || undefined);
        void queryClient.invalidateQueries();
      }}
    />
  );
};

export const ThemeSwitcher = () => {
  const { t } = useTranslation();
  const { mode, setMode } = useThemeMode();
  return (
    <Tooltip title={t('web.shell.header.theme')}>
      <Segmented<ThemeMode>
        size="small"
        value={mode}
        onChange={setMode}
        options={[
          { value: 'light', icon: <SunOutlined aria-label={t('web.shell.theme.light')} /> },
          { value: 'dark', icon: <MoonOutlined aria-label={t('web.shell.theme.dark')} /> },
          { value: 'system', icon: <DesktopOutlined aria-label={t('web.shell.theme.system')} /> },
        ]}
      />
    </Tooltip>
  );
};

export const NotificationBell = () => {
  const { t } = useTranslation();
  return (
    <Popover
      trigger="click"
      placement="bottomRight"
      title={t('web.shell.header.notifications')}
      content={<Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.shell.notifications.empty')} />}
    >
      <Badge count={0} size="small">
        <Button type="text" shape="circle" icon={<BellOutlined />} aria-label={t('web.shell.header.notifications')} />
      </Badge>
    </Popover>
  );
};

export const ApprovalsBadge = () => {
  const { t } = useTranslation();
  return (
    <Tooltip title={t('web.shell.header.approvals')}>
      <Badge count={0} size="small">
        <Button
          type="text"
          shape="circle"
          icon={<CheckSquareOutlined />}
          aria-label={t('web.shell.header.approvals')}
        />
      </Badge>
    </Tooltip>
  );
};

export const LanguageSelector = () => {
  const { t, i18n } = useTranslation();
  const me = useMe();
  const applyCulture = useCulturePreference();
  return (
    <Dropdown
      trigger={['click']}
      menu={{
        selectable: true,
        selectedKeys: [i18n.language],
        items: me.cultures.map((culture) => ({ key: culture.code, label: culture.name })),
        onClick: ({ key }) => {
          // Failures are reported by ApiErrorNotifier
          applyCulture(key).catch(() => undefined);
        },
      }}
    >
      <Tooltip title={t('web.shell.header.language')}>
        <Button type="text" shape="circle" icon={<GlobalOutlined />} aria-label={t('web.shell.header.language')} />
      </Tooltip>
    </Dropdown>
  );
};

export const UserMenu = () => {
  const { t } = useTranslation();
  const auth = useAuth();
  const me = useMe();
  const navigate = useNavigate();
  const name = me.user.displayName ?? me.user.userName ?? me.user.subject;
  const initials = name
    .split(/\s+/)
    .map((part) => part.charAt(0))
    .join('')
    .slice(0, 2)
    .toLocaleUpperCase('tr-TR');

  return (
    <Dropdown
      trigger={['click']}
      menu={{
        items: [
          { key: 'profile', icon: <UserOutlined />, label: t('web.shell.user.profile') },
          { type: 'divider' },
          { key: 'logout', icon: <LogoutOutlined />, label: t('web.shell.user.logout'), danger: true },
        ],
        onClick: ({ key }) => {
          if (key === 'profile') {
            void navigate('/profile');
          } else if (key === 'logout') {
            void auth.signoutRedirect();
          }
        },
      }}
    >
      <Button type="text" style={{ height: 40, paddingInline: 6 }}>
        <Flex align="center" gap={8}>
          <Avatar size={30} style={{ background: 'linear-gradient(135deg, #6366F1, #8B5CF6)', fontSize: 12 }}>
            {initials}
          </Avatar>
          <Flex vertical align="flex-start" style={{ lineHeight: 1.2 }}>
            <Typography.Text strong style={{ fontSize: 13 }}>
              {name}
            </Typography.Text>
            <Typography.Text type="secondary" style={{ fontSize: 11 }}>
              {me.tenant.name}
            </Typography.Text>
          </Flex>
        </Flex>
      </Button>
    </Dropdown>
  );
};
