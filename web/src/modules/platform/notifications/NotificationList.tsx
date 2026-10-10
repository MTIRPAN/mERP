import { BellOutlined } from '@ant-design/icons';
import { Avatar, Badge, Empty, List, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import type { NotificationItem } from '@/shared/api/generated/model';
import { formatDateTime, formatRelativeTime } from '@/shared/format/date';

interface NotificationListProps {
  items: NotificationItem[];
  loading?: boolean;
  onOpen: (item: NotificationItem) => void;
}

/** Notifications newest first; unread ones are marked with a dot and bold titles. */
export const NotificationList = ({ items, loading, onOpen }: NotificationListProps) => {
  const { t, i18n } = useTranslation();
  const { token } = theme.useToken();

  return (
    <List
      loading={loading}
      dataSource={items}
      rowKey="id"
      locale={{
        emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.shell.notifications.empty')} />,
      }}
      renderItem={(item) => (
        <List.Item
          onClick={() => onOpen(item)}
          style={{ cursor: 'pointer', paddingInline: 8, background: item.readAt ? undefined : token.colorPrimaryBg }}
        >
          <List.Item.Meta
            avatar={
              <Badge dot={!item.readAt}>
                <Avatar size="small" icon={<BellOutlined />} />
              </Badge>
            }
            title={<Typography.Text strong={!item.readAt}>{item.title}</Typography.Text>}
            description={
              <>
                {item.body && <Typography.Paragraph style={{ marginBottom: 4 }}>{item.body}</Typography.Paragraph>}
                <Typography.Text type="secondary" title={formatDateTime(item.createdAt, i18n.language)}>
                  {formatRelativeTime(item.createdAt, i18n.language)}
                </Typography.Text>
              </>
            }
          />
        </List.Item>
      )}
    />
  );
};
