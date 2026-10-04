import { Button, Result } from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useGetMe } from '@/shared/api/generated/endpoints/platform/platform';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { FullPageSpinner } from '@/shared/components/FullPageSpinner';
import { MeContext } from './meContext';

export const MeProvider = ({ children }: { children: ReactNode }) => {
  const { t } = useTranslation();
  const me = useGetMe({ query: { staleTime: 60_000 } });

  if (me.isPending) {
    return <FullPageSpinner />;
  }

  if (me.isError) {
    return (
      <Result
        status="warning"
        title={t('web.errors.network')}
        extra={
          <Button type="primary" onClick={() => void me.refetch()}>
            {t('web.common.retry')}
          </Button>
        }
      />
    );
  }

  return (
    <MeContext.Provider value={me.data}>
      <PermissionProvider permissions={me.data.permissions}>{children}</PermissionProvider>
    </MeContext.Provider>
  );
};
