import { useContext } from 'react';
import { MeContext } from '@/app/me/meContext';

/**
 * True when the tenant has AI on and the feature enabled (D24). It reads the profile without requiring it, so
 * components using it also render where no profile is loaded.
 */
export const useAiFeature = (feature: string) => {
  const me = useContext(MeContext);
  return !!me?.ai.enabled && me.ai.features.includes(feature);
};
