import { Breadcrumb, Flex, Typography, theme } from 'antd';
import type { ReactNode } from 'react';

interface PageHeaderProps {
  title: ReactNode;
  subtitle?: ReactNode;
  breadcrumb?: { title: ReactNode; href?: string }[];
  extra?: ReactNode;
}

export const PageHeader = ({ title, subtitle, breadcrumb, extra }: PageHeaderProps) => {
  const { token } = theme.useToken();
  return (
    <div style={{ marginBottom: token.marginLG }}>
      {breadcrumb && breadcrumb.length > 0 && (
        <Breadcrumb items={breadcrumb} style={{ marginBottom: token.marginXS }} />
      )}
      <Flex justify="space-between" align="flex-start" gap={token.margin} wrap>
        <div>
          <Typography.Title level={3} style={{ margin: 0, fontWeight: 600, letterSpacing: -0.2 }}>
            {title}
          </Typography.Title>
          {subtitle && (
            <Typography.Text type="secondary" style={{ display: 'block', marginTop: 4 }}>
              {subtitle}
            </Typography.Text>
          )}
        </div>
        {extra && (
          <Flex gap={token.marginXS} wrap>
            {extra}
          </Flex>
        )}
      </Flex>
    </div>
  );
};
