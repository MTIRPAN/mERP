import { Route, Routes } from 'react-router';
import { platformPermissions } from '@/modules/platform/permissions';
import { DashboardPage } from '@/modules/platform/DashboardPage';
import { JobsPage } from '@/modules/platform/JobsPage';
import { SystemInfoPage } from '@/modules/platform/SystemInfoPage';
import { ForbiddenPage } from '@/shared/pages/ForbiddenPage';
import { NotFoundPage } from '@/shared/pages/NotFoundPage';
import { AuthCallbackPage } from './auth/AuthCallbackPage';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { RequirePermission } from './auth/RequirePermission';
import { MeProvider } from './me/MeProvider';
import { AppLayout } from './shell/AppLayout';

export const AppRoutes = () => (
  <Routes>
    <Route path="/auth/callback" element={<AuthCallbackPage />} />
    <Route element={<ProtectedRoute />}>
      <Route
        element={
          <MeProvider>
            <AppLayout />
          </MeProvider>
        }
      >
        <Route index element={<DashboardPage />} />
        <Route
          path="system/info"
          element={
            <RequirePermission permission={platformPermissions.systemInfoRead}>
              <SystemInfoPage />
            </RequirePermission>
          }
        />
        <Route
          path="system/jobs"
          element={
            <RequirePermission permission={platformPermissions.jobsView}>
              <JobsPage />
            </RequirePermission>
          }
        />
        <Route path="403" element={<ForbiddenPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Route>
  </Routes>
);
