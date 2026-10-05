import { useEffect } from 'react';
import { useAuth } from 'react-oidc-context';
import { useNavigate } from 'react-router';
import { FullPageSpinner } from '@/shared/components/FullPageSpinner';
import type { SignInState } from './oidc';

/** Keycloak redirects here; AuthProvider exchanges the code, then the user returns to where they started. */
export const AuthCallbackPage = () => {
  const auth = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    if (!auth.isLoading && auth.isAuthenticated) {
      const returnTo = (auth.user?.state as SignInState | undefined)?.returnTo ?? '/';
      void navigate(returnTo.startsWith('/auth/') ? '/' : returnTo, { replace: true });
    } else if (!auth.isLoading && auth.error) {
      void navigate('/', { replace: true });
    }
  }, [auth.isLoading, auth.isAuthenticated, auth.error, auth.user, navigate]);

  return <FullPageSpinner labelKey="web.auth.signing_in" />;
};
