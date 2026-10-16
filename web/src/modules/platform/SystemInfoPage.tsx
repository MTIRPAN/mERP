import { AuditOutlined, ExportOutlined, SettingOutlined } from '@ant-design/icons';
import { Button, Card, Descriptions, Flex, Skeleton } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { platformPermissions } from '@/modules/platform/permissions';
import { useGetSystemInfo } from '@/shared/api/generated/endpoints/platform/platform';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { useOpenJobsDashboard } from './useOpenJobsDashboard';

/** Version, environment and tenant, with the administration tools the user may open (PLAN P13). */
export const SystemInfoPage = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const info = useGetSystemInfo();
  const jobs = useOpenJobsDashboard();
  const canViewJobs = useHasPermission(platformPermissions.jobsView);
  const canReadAudit = useHasPermission(platformPermissions.auditRead);
  const canReadSettings = useHasPermission(platformPermissions.settingsRead);

  return (
    <>
      <PageHeader title={t('web.system_info.title')} />
      <Flex vertical gap={16}>
        <Card>
          {info.data ? (
            <Descriptions
              column={1}
              bordered
              items={[
                { key: 'version', label: t('web.system_info.version'), children: info.data.version },
                { key: 'environment', label: t('web.system_info.environment'), children: info.data.environment },
                {
                  key: 'tenant',
                  label: t('web.system_info.tenant'),
                  children: `${info.data.tenantName} (${info.data.tenantCode})`,
                },
                { key: 'user', label: t('web.system_info.user'), children: info.data.userName ?? info.data.subject },
                { key: 'culture', label: t('web.system_info.culture'), children: info.data.culture },
              ]}
            />
          ) : (
            <Skeleton active />
          )}
        </Card>
        {(canViewJobs || canReadAudit || canReadSettings) && (
          <Card title={t('web.system_info.tools')}>
            <Flex gap={8} wrap>
              {canViewJobs && (
                <Button icon={<ExportOutlined />} loading={jobs.pending} onClick={() => void jobs.open()}>
                  {t('web.system_info.jobs')}
                </Button>
              )}
              {canReadAudit && (
                <Button icon={<AuditOutlined />} onClick={() => void navigate('/system/audit')}>
                  {t('web.shell.menu.audit')}
                </Button>
              )}
              {canReadSettings && (
                <Button icon={<SettingOutlined />} onClick={() => void navigate('/system/settings')}>
                  {t('web.shell.menu.settings')}
                </Button>
              )}
            </Flex>
          </Card>
        )}
      </Flex>
    </>
  );
};
