import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';

/** Reloads every HR list and record after an import: jobs, units, positions and employees change together (D40). */
export const useHrRefresh = () => {
  const queryClient = useQueryClient();
  return useCallback(
    () =>
      void queryClient.invalidateQueries({
        predicate: (query) => typeof query.queryKey[0] === 'string' && query.queryKey[0].startsWith('/api/hr/'),
      }),
    [queryClient],
  );
};
