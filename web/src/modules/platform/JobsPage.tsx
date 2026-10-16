import { ExportOutlined, ScheduleOutlined } from '@ant-design/icons';
import { Button, Card, Flex, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { PageHeader } from '@/shared/components/PageHeader';
import { useOpenJobsDashboard } from './useOpenJobsDashboard';

/** Opens the Hangfire dashboard in a new tab. */
export const JobsPage = () => {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const jobs = useOpenJobsDashboard();

  return (
    <>
      <PageHeader title={t('web.jobs.title')} />
      <Card>
        <Flex align="center" gap={token.margin} wrap>
          <ScheduleOutlined style={{ fontSize: 28, color: token.colorPrimary }} />
          <Typography.Text type="secondary" style={{ flex: 1, minWidth: 240 }}>
            {t('web.jobs.description')}
          </Typography.Text>
          <Button type="primary" icon={<ExportOutlined />} loading={jobs.pending} onClick={() => void jobs.open()}>
            {t('web.jobs.open')}
          </Button>
        </Flex>
      </Card>
    </>
  );
};
