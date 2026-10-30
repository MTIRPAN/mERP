import { lazy, type ComponentType } from 'react';
import { Route, Routes } from 'react-router';
import { platformPermissions } from '@/modules/platform/permissions';
import { DashboardPage } from '@/modules/platform/DashboardPage';
import { hrPermissions } from '@/modules/hr/permissions';
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
const ApprovalInboxPage = page(() => import('@/modules/platform/approvals/ApprovalInboxPage'), 'ApprovalInboxPage');
const ApprovalRequestPage = page(
  () => import('@/modules/platform/approvals/ApprovalRequestPage'),
  'ApprovalRequestPage',
);
const DelegationsPage = page(() => import('@/modules/platform/approvals/DelegationsPage'), 'DelegationsPage');
const ApprovalFlowsPage = page(() => import('@/modules/platform/approvals/ApprovalFlowsPage'), 'ApprovalFlowsPage');
const ApprovalRequestsPage = page(
  () => import('@/modules/platform/approvals/ApprovalRequestsPage'),
  'ApprovalRequestsPage',
);
const SettingsPage = page(() => import('@/modules/platform/settings/SettingsPage'), 'SettingsPage');
const DocumentTemplatesPage = page(
  () => import('@/modules/platform/documents/DocumentTemplatesPage'),
  'DocumentTemplatesPage',
);
const DocumentTemplateEditorPage = page(
  () => import('@/modules/platform/documents/DocumentTemplateEditorPage'),
  'DocumentTemplateEditorPage',
);
const NotificationTemplatesPage = page(
  () => import('@/modules/platform/documents/NotificationTemplatesPage'),
  'NotificationTemplatesPage',
);
const MyLeavePage = page(() => import('@/modules/hr/leave/requests/MyLeavePage'), 'MyLeavePage');
const LeaveRequestsPage = page(() => import('@/modules/hr/leave/requests/LeaveRequestsPage'), 'LeaveRequestsPage');
const LeaveCalendarPage = page(() => import('@/modules/hr/leave/calendar/LeaveCalendarPage'), 'LeaveCalendarPage');
const LeaveReportsPage = page(() => import('@/modules/hr/leave/reports/LeaveReportsPage'), 'LeaveReportsPage');
const RecruitmentSettingsPage = page(
  () => import('@/modules/recruitment/settings/RecruitmentSettingsPage'),
  'RecruitmentSettingsPage',
);
const OrgStructurePage = page(() => import('@/modules/hr/organization/OrgStructurePage'), 'OrgStructurePage');
const PositionsPage = page(() => import('@/modules/hr/positions/PositionsPage'), 'PositionsPage');
const EmployeesPage = page(() => import('@/modules/hr/employees/EmployeesPage'), 'EmployeesPage');
const PreHiresPage = page(() => import('@/modules/hr/preHires/PreHiresPage'), 'PreHiresPage');
const EmployeeCardPage = page(() => import('@/modules/hr/employees/EmployeeCardPage'), 'EmployeeCardPage');
const MyCardPage = page(() => import('@/modules/hr/selfService/MyCardPage'), 'MyCardPage');
const LeaveSettingsPage = page(() => import('@/modules/hr/leave/LeaveSettingsPage'), 'LeaveSettingsPage');
const DocumentTypesPage = page(() => import('@/modules/hr/documentTypes/DocumentTypesPage'), 'DocumentTypesPage');
const JobTitlesPage = page(() => import('@/modules/hr/jobTitles/JobTitlesPage'), 'JobTitlesPage');
const RequisitionsPage = page(() => import('@/modules/recruitment/requisitions/RequisitionsPage'), 'RequisitionsPage');
const OpeningsPage = page(() => import('@/modules/recruitment/openings/OpeningsPage'), 'OpeningsPage');
const MyOpeningsPage = page(() => import('@/modules/recruitment/careers/MyOpeningsPage'), 'MyOpeningsPage');
const PipelinePage = page(() => import('@/modules/recruitment/pipeline/PipelinePage'), 'PipelinePage');
const HiringPipelinePage = page(() => import('@/modules/recruitment/pipeline/PipelinePage'), 'HiringPipelinePage');
const CandidatesPage = page(() => import('@/modules/recruitment/candidates/CandidatesPage'), 'CandidatesPage');
const CandidatePage = page(() => import('@/modules/recruitment/candidates/CandidatePage'), 'CandidatePage');
const HiringCandidatePage = page(() => import('@/modules/recruitment/candidates/CandidatePage'), 'HiringCandidatePage');
const CareerPage = page(() => import('@/modules/recruitment/careers/CareerPage'), 'CareerPage');
const CareerOpeningPage = page(() => import('@/modules/recruitment/careers/CareerOpeningPage'), 'CareerOpeningPage');
const CvAnalysisPage = page(() => import('@/modules/recruitment/CvAnalysisPage'), 'CvAnalysisPage');
const MyInterviewsPage = page(() => import('@/modules/recruitment/interviews/MyInterviewsPage'), 'MyInterviewsPage');
const OffersPage = page(() => import('@/modules/recruitment/offers/OffersPage'), 'OffersPage');
const OfferLinkPage = page(() => import('@/modules/recruitment/offers/OffersPage'), 'OfferLinkPage');
const OfferResponsePage = page(() => import('@/modules/recruitment/offers/OfferResponsePage'), 'OfferResponsePage');

export const AppRoutes = () => (
  <Routes>
    <Route path="/auth/callback" element={<AuthCallbackPage />} />
    {/* The career page is public: no sign-in, no shell (D49) */}
    <Route path="/careers/:tenant" element={<CareerPage />} />
    <Route path="/careers/:tenant/openings/:id" element={<CareerOpeningPage />} />
    <Route path="/careers/:tenant/offers/:token" element={<OfferResponsePage />} />
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
        <Route path="approvals" element={<ApprovalInboxPage />} />
        <Route path="approvals/requests/:id" element={<ApprovalRequestPage />} />
        <Route path="approvals/delegations" element={<DelegationsPage />} />
        <Route
          path="system/approval-flows"
          element={
            <RequirePermission permission={platformPermissions.approvalDefinitionsRead}>
              <ApprovalFlowsPage />
            </RequirePermission>
          }
        />
        <Route
          path="system/settings"
          element={
            <RequirePermission permission={platformPermissions.settingsRead}>
              <SettingsPage />
            </RequirePermission>
          }
        />
        <Route
          path="system/approval-requests"
          element={
            <RequirePermission permission={platformPermissions.approvalsAdmin}>
              <ApprovalRequestsPage />
            </RequirePermission>
          }
        />
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
          path="system/document-templates"
          element={
            <RequirePermission permission={platformPermissions.documentTemplatesRead}>
              <DocumentTemplatesPage />
            </RequirePermission>
          }
        />
        <Route
          path="system/document-templates/new"
          element={
            <RequirePermission permission={platformPermissions.documentTemplatesManage}>
              <DocumentTemplateEditorPage />
            </RequirePermission>
          }
        />
        <Route
          path="system/document-templates/:id"
          element={
            <RequirePermission permission={platformPermissions.documentTemplatesRead}>
              <DocumentTemplateEditorPage />
            </RequirePermission>
          }
        />
        <Route
          path="system/notification-templates"
          element={
            <RequirePermission permission={platformPermissions.notificationTemplatesRead}>
              <NotificationTemplatesPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/organization"
          element={
            <RequirePermission permission={hrPermissions.orgRead}>
              <OrgStructurePage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/pre-hires"
          element={
            <RequirePermission permission={hrPermissions.employeesCreate}>
              <PreHiresPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/employees"
          element={
            <RequirePermission permission={hrPermissions.employeesRead}>
              <EmployeesPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/employees/:id"
          element={
            <RequirePermission permission={hrPermissions.employeesRead}>
              <EmployeeCardPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/me"
          element={
            <RequirePermission permission={hrPermissions.selfProfileRead}>
              <MyCardPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/me/leave"
          element={
            <RequirePermission permission={hrPermissions.selfLeaveRead}>
              <MyLeavePage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/leave-requests"
          element={
            <RequirePermission permission={hrPermissions.leaveRequestsRead}>
              <LeaveRequestsPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/leave-requests/:id"
          element={
            <RequirePermission permission={hrPermissions.leaveRequestsRead}>
              <LeaveRequestsPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/leave-calendar"
          element={
            <RequirePermission permission={hrPermissions.leaveCalendarRead}>
              <LeaveCalendarPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/leave-reports"
          element={
            <RequirePermission permission={hrPermissions.leaveReportsRead}>
              <LeaveReportsPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/leave-settings"
          element={
            <RequirePermission permission={hrPermissions.leaveSettingsRead}>
              <LeaveSettingsPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/document-types"
          element={
            <RequirePermission permission={hrPermissions.documentsManage}>
              <DocumentTypesPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/positions"
          element={
            <RequirePermission permission={hrPermissions.orgRead}>
              <PositionsPage />
            </RequirePermission>
          }
        />
        <Route
          path="hr/job-titles"
          element={
            <RequirePermission permission={hrPermissions.orgRead}>
              <JobTitlesPage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/requisitions"
          element={
            <RequirePermission permission={recruitmentPermissions.requisitionsRead}>
              <RequisitionsPage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/requisitions/:id"
          element={
            <RequirePermission permission={recruitmentPermissions.requisitionsRead}>
              <RequisitionsPage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/openings"
          element={
            <RequirePermission permission={recruitmentPermissions.openingsRead}>
              <OpeningsPage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/openings/:id"
          element={
            <RequirePermission permission={recruitmentPermissions.openingsRead}>
              <OpeningsPage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/openings/:id/pipeline"
          element={
            <RequirePermission permission={recruitmentPermissions.applicationsRead}>
              <PipelinePage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/hiring/:id"
          element={
            <RequirePermission permission={recruitmentPermissions.selfHiring}>
              <HiringPipelinePage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/candidates"
          element={
            <RequirePermission permission={recruitmentPermissions.candidatesRead}>
              <CandidatesPage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/offers"
          element={
            <RequirePermission permission={recruitmentPermissions.offersRead}>
              <OffersPage />
            </RequirePermission>
          }
        />
        {/* The approval inbox's link: the hiring manager opens it from self service too (D53) */}
        <Route path="recruitment/offers/:id" element={<OfferLinkPage />} />
        <Route
          path="recruitment/my-interviews"
          element={
            <RequirePermission permission={recruitmentPermissions.interviewsRead}>
              <MyInterviewsPage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/candidates/:id"
          element={
            <RequirePermission permission={recruitmentPermissions.candidatesRead}>
              <CandidatePage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/hiring/candidates/:id"
          element={
            <RequirePermission permission={recruitmentPermissions.selfHiring}>
              <HiringCandidatePage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/my-openings"
          element={
            <RequirePermission permission={recruitmentPermissions.selfApply}>
              <MyOpeningsPage />
            </RequirePermission>
          }
        />
        <Route
          path="recruitment/settings"
          element={
            <RequirePermission permission={recruitmentPermissions.settingsRead}>
              <RecruitmentSettingsPage />
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
