import type { ReactNode } from 'react';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { ForbiddenPage } from '@/shared/pages/ForbiddenPage';

/** Route guard: pages a user may not open show the 403 page. The API checks the permission again. */
export const RequirePermission = ({ permission, children }: { permission: string; children: ReactNode }) =>
  useHasPermission(permission) ? children : <ForbiddenPage />;
