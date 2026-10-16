import { BellOutlined, CheckSquareOutlined, LinkOutlined, StarFilled } from '@ant-design/icons';
import { Button, Card, Col, Flex, Row, Statistic, Tag, Typography, theme } from 'antd';
import { useMemo, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { useMe } from '@/app/me/meContext';
import { menuIcon } from '@/app/shell/menuItems';
import { brand } from '@/app/theme/tokens';
import {
  useCountMyApprovalTasks,
  useCountMyUnreadNotifications,
  useListMyNotifications,
} from '@/shared/api/generated/endpoints/platform/platform';
import { quickLinks } from './dashboardLinks';
import { NotificationList } from './notifications/NotificationList';
import { useOpenNotification } from './notifications/useOpenNotification';

/** Notifications shown on the home page; the rest are on the notifications page. */
const RECENT = 5;

const StatCard = ({
  icon,
  title,
  value = 0,
  onClick,
}: {
  icon: ReactNode;
  title: string;
  value?: number;
  onClick?: () => void;
}) => {
  const { token } = theme.useToken();
  return (
    <Card variant="outlined" hoverable={!!onClick} onClick={onClick} style={{ height: '100%' }}>
      <Flex align="center" gap={14}>
        <div
          style={{
            width: 40,
            height: 40,
            borderRadius: 10,
            display: 'grid',
            placeItems: 'center',
            background: token.colorPrimaryBg,
            color: token.colorPrimary,
            fontSize: 18,
          }}
        >
          {icon}
        </div>
        <Statistic title={title} value={value} />
      </Flex>
    </Card>
  );
};

/** Home page (PLAN P13): what waits for the user, their latest notifications and the screens they use most. */
export const DashboardPage = () => {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const me = useMe();
  const name = me.user.displayName ?? me.user.userName ?? '';
  const navigate = useNavigate();
  const approvals = useCountMyApprovalTasks().data?.count ?? 0;
  const unread = useCountMyUnreadNotifications().data?.count ?? 0;
  const recent = useListMyNotifications({ pageSize: RECENT });
  const openNotification = useOpenNotification();
  const links = useMemo(() => quickLinks(me.menu, t), [me.menu, t]);

  return (
    <Flex vertical gap={token.marginLG}>
      <Card
        variant="borderless"
        style={{ background: brand.gradientSoft, border: `1px solid ${token.colorBorderSecondary}` }}
      >
        <Typography.Title level={3} style={{ margin: 0, letterSpacing: -0.3 }}>
          {t('web.dashboard.greeting', { name })}
        </Typography.Title>
        <Typography.Text type="secondary">{t('web.dashboard.subtitle', { tenant: me.tenant.name })}</Typography.Text>
      </Card>

      <Row gutter={[token.margin, token.margin]}>
        <Col xs={24} md={8}>
          <StatCard
            icon={<CheckSquareOutlined />}
            title={t('web.dashboard.cards.approvals')}
            value={approvals}
            onClick={() => void navigate('/approvals')}
          />
        </Col>
        <Col xs={24} md={8}>
          <StatCard
            icon={<BellOutlined />}
            title={t('web.dashboard.cards.notifications')}
            value={unread}
            onClick={() => void navigate('/notifications')}
          />
        </Col>
        <Col xs={24} md={8}>
          <div className="merp-gradient-border" style={{ height: '100%', padding: token.paddingLG }}>
            <Flex align="center" gap={12} style={{ marginBottom: 8 }}>
              <div
                style={{
                  width: 32,
                  height: 32,
                  borderRadius: 8,
                  display: 'grid',
                  placeItems: 'center',
                  background: brand.gradient,
                  color: '#fff',
                }}
              >
                <StarFilled />
              </div>
              <Typography.Text strong className="merp-gradient-text" style={{ fontSize: 16 }}>
                {t('web.dashboard.assistant.title')}
              </Typography.Text>
              <Tag variant="filled" color="purple">
                {t('web.dashboard.assistant.badge')}
              </Tag>
            </Flex>
            <Typography.Text type="secondary">{t('web.dashboard.assistant.description')}</Typography.Text>
          </div>
        </Col>
      </Row>

      <Row gutter={[token.margin, token.margin]}>
        <Col xs={24} lg={14}>
          <Card
            title={t('web.dashboard.cards.recent_notifications')}
            extra={
              <Button type="link" size="small" onClick={() => void navigate('/notifications')}>
                {t('web.notifications.see_all')}
              </Button>
            }
            style={{ height: '100%' }}
          >
            <NotificationList
              items={recent.data?.items ?? []}
              loading={recent.isLoading}
              onOpen={(item) => void openNotification(item).catch(() => undefined)}
            />
          </Card>
        </Col>
        <Col xs={24} lg={10}>
          <Card title={t('web.dashboard.cards.quick_links')} extra={<LinkOutlined />} style={{ height: '100%' }}>
            <Row gutter={[8, 8]}>
              {links.map((link) => (
                <Col key={link.key} xs={24} sm={12}>
                  <Button
                    block
                    icon={menuIcon(link.icon)}
                    onClick={() => void navigate(link.path)}
                    style={{ justifyContent: 'flex-start', height: 'auto', paddingBlock: 8, whiteSpace: 'normal' }}
                  >
                    {link.label}
                  </Button>
                </Col>
              ))}
            </Row>
          </Card>
        </Col>
      </Row>
    </Flex>
  );
};
