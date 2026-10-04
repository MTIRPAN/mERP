import { ExportOutlined, ScheduleOutlined } from '@ant-design/icons';
import { Button, Card, Flex, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCreateJobsDashboardSession } from '@/shared/api/generated/endpoints/platform/platform';
import { PageHeader } from '@/shared/components/PageHeader';

/** Opens the Hangfire dashboard: the API first issues a short-lived cookie, then the dashboard opens in a new tab. */
export const JobsPage = () => {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const session = useCreateJobsDashboardSession();

  const open = async () => {
    await session.mutateAsync();
    window.open('/admin/jobs', '_blank', 'noopener');
  };

  return (
    <>
      <PageHeader title={t('web.jobs.title')} />
      <Card>
        <Flex align="center" gap={token.margin} wrap>
          <ScheduleOutlined style={{ fontSize: 28, color: token.colorPrimary }} />
          <Typography.Text type="secondary" style={{ flex: 1, minWidth: 240 }}>
            {t('web.jobs.description')}
          </Typography.Text>
          <Button type="primary" icon={<ExportOutlined />} loading={session.isPending} onClick={() => void open()}>
            {t('web.jobs.open')}
          </Button>
        </Flex>
      </Card>
    </>
  );
};
