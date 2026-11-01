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
  numberingRead: 'platform.numbering.read',
  numberingManage: 'platform.numbering.manage',
  customFieldsRead: 'platform.custom_fields.read',
  customFieldsManage: 'platform.custom_fields.manage',
  calendarsRead: 'platform.calendars.read',
  calendarsManage: 'platform.calendars.manage',
  approvalDefinitionsRead: 'platform.approvals.definitions.read',
  approvalDefinitionsManage: 'platform.approvals.definitions.manage',
  approvalsAdmin: 'platform.approvals.admin',
  settingsRead: 'platform.settings.read',
  settingsManage: 'platform.settings.manage',
  notificationTemplatesRead: 'platform.notification_templates.read',
  notificationTemplatesManage: 'platform.notification_templates.manage',
  documentTemplatesRead: 'platform.document_templates.read',
  documentTemplatesManage: 'platform.document_templates.manage',
} as const;

/** Mirrors Merp.Modules.Platform.Application.PlatformAiFeatures. */
export const platformAiFeatures = {
  translationDrafts: 'platform.translation_drafts',
  approvalSummary: 'platform.approval_summary',
} as const;
