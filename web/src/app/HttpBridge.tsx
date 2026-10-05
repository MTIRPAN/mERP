import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { getActiveCompanyId } from '@/app/company/companyStore';
import { configureHttp } from '@/shared/api/http';
import { getAccessToken, signIn } from './auth/oidc';

/** Connects the generated API client to authentication, company selection, culture and navigation. */
export const HttpBridge = () => {
  const { i18n } = useTranslation();
  const navigate = useNavigate();

  useEffect(() => {
    configureHttp({
      getAccessToken,
      getCompanyId: getActiveCompanyId,
      getCulture: () => i18n.language,
      onUnauthorized: () => void signIn(),
      onForbidden: () => void navigate('/403'),
    });
  }, [i18n, navigate]);

  return null;
};
