import type { ReactNode } from 'react';
import { useHasPermission } from '@/shared/auth/permissionContext';

interface CanProps {
  /** Permission code, e.g. "hr.employees.create". */
  permission: string;
  children: ReactNode;
  fallback?: ReactNode;
}

/** Renders its children only for users holding the permission. The API enforces it again. */
export const Can = ({ permission, children, fallback = null }: CanProps) =>
  useHasPermission(permission) ? children : fallback;
