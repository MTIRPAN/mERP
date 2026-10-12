import { useQueryClient } from '@tanstack/react-query';
import { App } from 'antd';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { isApiError } from '@/shared/api/problem';

/**
 * Shows API errors of queries and mutations as notifications, using the localized ProblemDetails title and detail.
 * 401 and 403 are handled by navigation; validation errors with field details are shown on the form instead.
 */
export const ApiErrorNotifier = () => {
  const queryClient = useQueryClient();
  const { notification } = App.useApp();
  const { t } = useTranslation();

  useEffect(() => {
    const notify = (error: unknown) => {
      if (!isApiError(error)) {
        notification.error({
          title: t('web.errors.unexpected.title'),
          description: t('web.errors.unexpected.description'),
        });
        return;
      }

      if (error.status === 401 || error.status === 403 || error.problem?.fieldErrors?.length) {
        return;
      }

      notification.error({
        title:
          error.status === 0 ? t('web.errors.network') : (error.problem?.title ?? t('web.errors.unexpected.title')),
        description: error.status === 0 ? undefined : error.problem?.detail,
      });
    };

    const unsubscribeQueries = queryClient.getQueryCache().subscribe((event) => {
      // Queries marked silent show their own state, e.g. a timeline of a document never sent (404)
      if (event.type === 'updated' && event.action.type === 'error' && !event.query.meta?.silent) {
        notify(event.action.error);
      }
    });
    const unsubscribeMutations = queryClient.getMutationCache().subscribe((event) => {
      if (event.type === 'updated' && event.action.type === 'error') {
        notify(event.action.error);
      }
    });

    return () => {
      unsubscribeQueries();
      unsubscribeMutations();
    };
  }, [queryClient, notification, t]);

  return null;
};
