import { useQueryClient } from '@tanstack/react-query';
import { changeCulture } from '@/app/i18n/i18n';
import { useUpdateMyPreferences } from '@/shared/api/generated/endpoints/platform/platform';

/**
 * Saves the user's language on the server (it wins over Accept-Language, D26) and switches the UI to it. Null
 * clears the preference: the API then follows the language this device sends.
 */
export const useCulturePreference = () => {
  const queryClient = useQueryClient();
  const update = useUpdateMyPreferences();

  return async (culture: string | null) => {
    await update.mutateAsync({ data: { culture } });
    if (culture) {
      await changeCulture(culture);
    }

    await queryClient.invalidateQueries();
  };
};
