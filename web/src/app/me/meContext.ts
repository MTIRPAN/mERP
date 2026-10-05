import { createContext, useContext } from 'react';
import type { MeResponse } from '@/shared/api/generated/model';

export const MeContext = createContext<MeResponse | undefined>(undefined);

/** The signed-in user's profile, tenant, permissions and menu from GET /api/platform/me. */
export const useMe = (): MeResponse => {
  const me = useContext(MeContext);
  if (!me) {
    throw new Error('useMe must be used inside MeProvider');
  }

  return me;
};
