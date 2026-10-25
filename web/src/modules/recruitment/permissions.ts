/** Mirrors Merp.Modules.Recruitment.Application.RecruitmentPermissions. */
export const recruitmentPermissions = {
  cvAnalyze: 'recruitment.cv.analyze',
  settingsRead: 'recruitment.settings.read',
  settingsManage: 'recruitment.settings.manage',
  requisitionsRead: 'recruitment.requisitions.read',
  requisitionsManage: 'recruitment.requisitions.manage',
  openingsRead: 'recruitment.openings.read',
  openingsManage: 'recruitment.openings.manage',
} as const;

/** Mirrors Merp.Modules.Recruitment.Application.RecruitmentAiFeatures. */
export const recruitmentAiFeatures = {
  cvAnalysis: 'recruitment.cv_analysis',
} as const;
