import { lazy, type ComponentType } from 'react';
import { Route, Routes } from 'react-router';
import { platformPermissions } from '@/modules/platform/permissions';
import { DashboardPage } from '@/modules/platform/DashboardPage';
import { recruitmentPermissions } from '@/modules/recruitment/permissions';
import { ForbiddenPage } from '@/shared/pages/ForbiddenPage';
import { NotFoundPage } from '@/shared/pages/NotFoundPage';
import { AuthCallbackPage } from './auth/AuthCallbackPage';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { RequirePermission } from './auth/RequirePermission';
import { MeProvider } from './me/MeProvider';
import { AppLayout } from './shell/AppLayout';

/** Module pages load on first visit, so the start-up bundle does not grow with every module. */
const page = <K extends string>(load: () => Promise<Record<K, ComponentType>>, name: K) =>
  lazy(async () => ({ default: (await load())[name] }));

const RoleEditorPage = page(() => import('@/modules/platform/access/RoleEditorPage'), 'RoleEditorPage');
const RolesPage = page(() => import('@/modules/platform/access/RolesPage'), 'RolesPage');
const UserDetailPage = page(() => import('@/modules/platform/access/UserDetailPage'), 'UserDetailPage');
const UsersPage = page(() => import('@/modules/platform/access/UsersPage'), 'UsersPage');
const AiUsagePage = page(() => import('@/modules/platform/AiUsagePage'), 'AiUsagePage');
const JobsPage = page(() => import('@/modules/platform/JobsPage'), 'JobsPage');
const BranchesPage = page(() => import('@/modules/platform/organization/BranchesPage'), 'BranchesPage');
const CompaniesPage = page(() => import('@/modules/platform/organization/CompaniesPage'), 'CompaniesPage');
const ProfilePage = page(() => import('@/modules/platform/ProfilePage'), 'ProfilePage');
const NotificationsPage = page(() => import('@/modules/platform/notifications/NotificationsPage'), 'NotificationsPage');
const SystemInfoPage = page(() => import('@/modules/platform/SystemInfoPage'), 'SystemInfoPage');
const AuditLogPage = page(() => import('@/modules/platform/audit/AuditLogPage'), 'AuditLogPage');
const LocalizationPage = page(() => import('@/modules/platform/localization/LocalizationPage'), 'LocalizationPage');
const NumberingPage = page(() => import('@/modules/platform/numbering/NumberingPage'), 'NumberingPage');
const CustomFieldsPage = page(() => import('@/modules/platform/customFields/CustomFieldsPage'), 'CustomFieldsPage');
const HolidaysPage = page(() => import('@/modules/platform/calendars/HolidaysPage'), 'HolidaysPage');
const WorkSchedulesPage = page(() => import('@/modules/platform/calendars/WorkSchedulesPage'), 'WorkSchedulesPage');
const CvAnalysisPage = page(() => import('@/modules/recruitment/CvAnalysisPage'), 'CvAnalysisPage');

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
          path="organization/companies"
          element={
            <RequirePermission permission={platformPermissions.companiesRead}>
              <CompaniesPage />
            </RequirePermission>
          }
        />
        <Route
          path="organization/branches"
          element={
            <RequirePermission permission={platformPermissions.branchesRead}>
              <BranchesPage />
            </RequirePermission>
          }
        />
        <Route
          path="access/users"
          element={
            <RequirePermission permission={platformPermissions.usersRead}>
              <UsersPage />
            </RequirePermission>
          }
        />
        <Route
          path="access/users/:id"
          element={
            <RequirePermission permission={platformPermissions.usersRead}>
              <UserDetailPage />
            </RequirePermission>
          }
        />
        <Route
          path="access/roles"
          element={
            <RequirePermission permission={platformPermissions.rolesRead}>
              <RolesPage />
            </RequirePermission>
          }
        />
        <Route
          path="access/roles/:id"
          element={
            <RequirePermission permission={platformPermissions.rolesRead}>
              <RoleEditorPage />
            </RequirePermission>
          }
        />
        <Route path="profile" element={<ProfilePage />} />
        <Route path="notifications" element={<NotificationsPage />} />
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
        <Route
          path="system/ai-usage"
          element={
            <RequirePermission permission={platformPermissions.aiUsageRead}>
              <AiUsagePage />
            </RequirePermission>
          }
        />
        <Route
          path="system/audit"
          element={
            <RequirePermission permission={platformPermissions.auditRead}>
              <AuditLogPage />
            </RequirePermission>
          }
        />
        <Route
          path="system/localization"
          element={
            <RequirePermission permission={platformPermissions.localizationRead}>
              <LocalizationPage />
            </RequirePermission>
          }
        />
        <Route
          path="system/numbering"
          element={
            <RequirePermission permission={platformPermissions.numberingRead}>
              <NumberingPage />
            </RequirePermission>
          }
        />
        <Route
          path="system/custom-fields"
          element={
            <RequirePermission permission={platformPermissions.customFieldsRead}>
              <CustomFieldsPage />
            </RequirePermission>
          }
        />
        <Route
          path="system/holidays"
          element={
            <RequirePermission permission={platformPermissions.calendarsRead}>
              <HolidaysPage />
            </RequirePermission>
          }
        />
        <Route
          path="system/work-schedules"
          element={
            <RequirePermission permission={platformPermissions.calendarsRead}>
              <WorkSchedulesPage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/cv-analysis"
          element={
            <RequirePermission permission={recruitmentPermissions.cvAnalyze}>
              <CvAnalysisPage />
            </RequirePermission>
          }
        />
        <Route path="403" element={<ForbiddenPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Route>
  </Routes>
);
