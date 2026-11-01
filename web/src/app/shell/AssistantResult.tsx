import { RightOutlined } from '@ant-design/icons';
import { Alert, Button, Empty, Flex, Space, Spin, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import type { AssistantResponse } from '@/shared/api/generated/model';
import { AiBadge } from '@/shared/components/AiBadge';

const prefix = 'web.shell.assistant';

interface AssistantResultProps {
  result: AssistantResponse | undefined;
  loading: boolean;
  onOpen: (path: string) => void;
}

/**
 * The assistant's answer in the command palette (PLAN A12, D65): the first matching rows and a link to all of them,
 * or what the assistant can do. The rows come from the user's own queries; the model only chose the question.
 */
export const AssistantResult = ({ result, loading, onOpen }: AssistantResultProps) => {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  if (loading || !result) {
    return (
      <Flex justify="center" style={{ padding: 24 }}>
        <Spin />
      </Flex>
    );
  }

  const answer = result.answer;
  return (
    <Flex vertical gap={8} style={{ padding: 8 }}>
      <Flex justify="space-between" align="center" gap={8}>
        <Space>
          <Typography.Text strong>{result.title || t(`${prefix}.title`)}</Typography.Text>
          <AiBadge />
        </Space>
        {answer && <Typography.Text type="secondary">{t(`${prefix}.total`, { count: answer.total })}</Typography.Text>}
      </Flex>
      {answer ? (
        <>
          {answer.items.length === 0 ? (
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t(`${prefix}.no_rows`)} />
          ) : (
            <div role="list">
              {answer.items.map((item, index) => (
                <Flex
                  key={index}
                  role="listitem"
                  align="center"
                  gap={12}
                  onClick={() => item.link && onOpen(item.link)}
                  style={{
                    padding: '8px 12px',
                    borderRadius: token.borderRadius,
                    cursor: item.link ? 'pointer' : undefined,
                  }}
                >
                  <Flex vertical style={{ flex: 1 }}>
                    <Typography.Text>{item.title}</Typography.Text>
                    {item.detail && (
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                        {item.detail}
                      </Typography.Text>
                    )}
                  </Flex>
                  {item.link && <RightOutlined style={{ color: token.colorTextTertiary }} />}
                </Flex>
              ))}
            </div>
          )}
          {answer.link && answer.total > 0 && (
            <Button type="link" style={{ alignSelf: 'flex-start' }} onClick={() => onOpen(answer.link!)}>
              {t(`${prefix}.see_all`)}
            </Button>
          )}
        </>
      ) : (
        <Typography.Paragraph style={{ margin: 0 }}>
          {result.message ?? t(`${prefix}.not_understood`)}
        </Typography.Paragraph>
      )}
      <Alert type="info" showIcon title={t(`${prefix}.notice`)} />
    </Flex>
  );
};
