/** Mirrors Merp.Modules.Recruitment.Application.RecruitmentPermissions. */
export const recruitmentPermissions = {
  cvAnalyze: 'recruitment.cv.analyze',
  settingsRead: 'recruitment.settings.read',
  settingsManage: 'recruitment.settings.manage',
  requisitionsRead: 'recruitment.requisitions.read',
  requisitionsManage: 'recruitment.requisitions.manage',
  selfApply: 'recruitment.self.apply',
  selfHiring: 'recruitment.self.hiring',
  openingsRead: 'recruitment.openings.read',
  openingsManage: 'recruitment.openings.manage',
  applicationsRead: 'recruitment.applications.read',
  applicationsManage: 'recruitment.applications.manage',
  candidatesRead: 'recruitment.candidates.read',
  candidatesManage: 'recruitment.candidates.manage',
  interviewsRead: 'recruitment.interviews.read',
  interviewsManage: 'recruitment.interviews.manage',
  feedbackSubmit: 'recruitment.feedback.submit',
} as const;

/** Mirrors Merp.Modules.Recruitment.Application.RecruitmentAiFeatures. */
export const recruitmentAiFeatures = {
  cvAnalysis: 'recruitment.cv_analysis',
} as const;
