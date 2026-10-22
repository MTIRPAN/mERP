import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { getListMyProfileChangesQueryKey } from '@/shared/api/generated/endpoints/hr/hr';

/** Reloads the user's change requests after one is sent, corrected or withdrawn. */
export const useProfileChangesRefresh = () => {
  const queryClient = useQueryClient();
  return useCallback(
    () => queryClient.invalidateQueries({ queryKey: getListMyProfileChangesQueryKey() }),
    [queryClient],
  );
};
