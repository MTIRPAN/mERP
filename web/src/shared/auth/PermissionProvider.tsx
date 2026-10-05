import { useMemo, type ReactNode } from 'react';
import { PermissionContext } from './permissionContext';

export const PermissionProvider = ({
  permissions,
  children,
}: {
  permissions: readonly string[];
  children: ReactNode;
}) => {
  const value = useMemo(() => new Set(permissions), [permissions]);
  return <PermissionContext.Provider value={value}>{children}</PermissionContext.Provider>;
};
