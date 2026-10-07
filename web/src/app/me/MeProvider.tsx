import { Button, Result } from 'antd';
import { useEffect, useRef, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { changeCulture, reloadTexts } from '@/app/i18n/i18n';
import { useGetMe } from '@/shared/api/generated/endpoints/platform/platform';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { FullPageSpinner } from '@/shared/components/FullPageSpinner';
import { MeContext } from './meContext';

export const MeProvider = ({ children }: { children: ReactNode }) => {
  const { t, i18n } = useTranslation();
  const me = useGetMe({ query: { staleTime: 60_000 } });
  const data = me.data;
  // The user's choice (also made on another device, D26) wins; otherwise this device's language if the tenant offers
  // it; otherwise the culture the server picked (D28)
  const target = data
    ? (data.preferences.preferredCulture ??
      (data.cultures.some((c) => c.code === i18n.language) ? i18n.language : data.preferences.culture))
    : undefined;
  const loaded = useRef<string>(undefined);

  // Signed in: load the texts again, now with the tenant's translations
  useEffect(() => {
    if (!target || loaded.current === target) {
      return;
    }

    loaded.current = target;
    void (target === i18n.language ? reloadTexts() : changeCulture(target));
  }, [target, i18n.language]);

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
