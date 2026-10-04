import { useSyncExternalStore } from 'react';

/** Active company sent as X-Company-Id (D4). It narrows data and is remembered per browser. */
const STORAGE_KEY = 'merp.company';
const listeners = new Set<() => void>();
let current: string | undefined = localStorage.getItem(STORAGE_KEY) ?? undefined;

export const getActiveCompanyId = (): string | undefined => current;

export const setActiveCompanyId = (companyId: string | undefined) => {
  current = companyId;
  if (companyId) {
    localStorage.setItem(STORAGE_KEY, companyId);
  } else {
    localStorage.removeItem(STORAGE_KEY);
  }

  listeners.forEach((listener) => listener());
};

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

export const useActiveCompanyId = () => useSyncExternalStore(subscribe, getActiveCompanyId);
