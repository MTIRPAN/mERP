import { Card, Descriptions, Skeleton } from 'antd';
import { useTranslation } from 'react-i18next';
import { useGetSystemInfo } from '@/shared/api/generated/endpoints/platform/platform';
import { PageHeader } from '@/shared/components/PageHeader';

export const SystemInfoPage = () => {
  const { t } = useTranslation();
  const info = useGetSystemInfo();

  return (
    <>
      <PageHeader title={t('web.system_info.title')} />
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
    </>
  );
};
