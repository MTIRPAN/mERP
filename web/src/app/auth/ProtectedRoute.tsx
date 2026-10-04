import { Button, Result } from 'antd';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { hasAuthParams, useAuth } from 'react-oidc-context';
import { Outlet, useLocation } from 'react-router';
import { FullPageSpinner } from '@/shared/components/FullPageSpinner';
import { signIn } from './oidc';

/** Children render only for a signed-in user; otherwise the browser is sent to Keycloak. */
export const ProtectedRoute = () => {
  const { t } = useTranslation();
  const auth = useAuth();
  const location = useLocation();
  const mustSignIn =
    !auth.isLoading && !auth.isAuthenticated && !auth.activeNavigator && !auth.error && !hasAuthParams();

  useEffect(() => {
    if (mustSignIn) {
      void signIn(location.pathname + location.search);
    }
  }, [mustSignIn, location.pathname, location.search]);

  if (auth.error) {
    return (
      <Result
        status="warning"
        title={t('web.auth.signed_out')}
        extra={
          <Button type="primary" onClick={() => void signIn(location.pathname)}>
            {t('web.auth.sign_in')}
          </Button>
        }
      />
    );
  }

  return auth.isAuthenticated ? <Outlet /> : <FullPageSpinner labelKey="web.auth.signing_in" />;
};
