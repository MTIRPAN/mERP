import { BellOutlined, CheckSquareOutlined, LinkOutlined, StarFilled } from '@ant-design/icons';
import { Card, Col, Empty, Flex, Row, Statistic, Tag, Typography, theme } from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import { brand } from '@/app/theme/tokens';

const StatCard = ({ icon, titleKey }: { icon: ReactNode; titleKey: string }) => {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  return (
    <Card variant="outlined" style={{ height: '100%' }}>
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
        <Statistic title={t(titleKey)} value={0} />
      </Flex>
    </Card>
  );
};

export const DashboardPage = () => {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const me = useMe();
  const name = me.user.displayName ?? me.user.userName ?? '';

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
          <StatCard icon={<CheckSquareOutlined />} titleKey="web.dashboard.cards.approvals" />
        </Col>
        <Col xs={24} md={8}>
          <StatCard icon={<BellOutlined />} titleKey="web.dashboard.cards.notifications" />
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

      <Card title={t('web.dashboard.cards.quick_links')} extra={<LinkOutlined />}>
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={
            <Flex vertical gap={4}>
              <Typography.Text strong>{t('web.dashboard.empty.title')}</Typography.Text>
              <Typography.Text type="secondary">{t('web.dashboard.empty.description')}</Typography.Text>
            </Flex>
          }
        />
      </Card>
    </Flex>
  );
};
