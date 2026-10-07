/** Mirrors Merp.Modules.Platform.Application.PlatformPermissions. */
export const platformPermissions = {
  systemInfoRead: 'platform.system.read',
  jobsView: 'platform.jobs.view',
  aiUsageRead: 'platform.ai_usage.read',
  companiesRead: 'platform.companies.read',
  companiesManage: 'platform.companies.manage',
  branchesRead: 'platform.branches.read',
  branchesManage: 'platform.branches.manage',
  usersRead: 'platform.users.read',
  usersManage: 'platform.users.manage',
  rolesRead: 'platform.roles.read',
  rolesManage: 'platform.roles.manage',
  auditRead: 'platform.audit.read',
  localizationRead: 'platform.localization.read',
  localizationManage: 'platform.localization.manage',
} as const;
