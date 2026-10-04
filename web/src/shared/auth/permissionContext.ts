import { createContext, useContext } from 'react';

export const PermissionContext = createContext<ReadonlySet<string>>(new Set());

/** True when the signed-in user holds the permission (or no permission is required). */
export const useHasPermission = (permission?: string): boolean => {
  const permissions = useContext(PermissionContext);
  return !permission || permissions.has(permission);
};
