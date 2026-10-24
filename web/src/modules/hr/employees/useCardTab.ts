import { useCallback } from 'react';
import { useSearchParams } from 'react-router';

/**
 * The open tab of a card in the URL (<c>?tab=requests</c>), so links from notifications and approvals open it; an
 * unknown tab falls back to the first one.
 */
export const useCardTab = (keys: string[]) => {
  const [params, setParams] = useSearchParams();
  const requested = params.get('tab');
  const tab = requested && keys.includes(requested) ? requested : keys[0];
  const select = useCallback(
    (key: string) =>
      setParams(
        (current) => {
          const next = new URLSearchParams(current);
          next.set('tab', key);
          return next;
        },
        { replace: true },
      ),
    [setParams],
  );
  return [tab, select] as const;
};
