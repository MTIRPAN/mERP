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
  candidatesPrivacy: 'recruitment.candidates.privacy',
  interviewsRead: 'recruitment.interviews.read',
  interviewsManage: 'recruitment.interviews.manage',
  feedbackSubmit: 'recruitment.feedback.submit',
  offersRead: 'recruitment.offers.read',
  offersManage: 'recruitment.offers.manage',
  reportsRead: 'recruitment.reports.read',
} as const;

/** Mirrors Merp.Modules.Recruitment.Application.RecruitmentAiFeatures. */
export const recruitmentAiFeatures = {
  cvAnalysis: 'recruitment.cv_analysis',
  openingText: 'recruitment.opening_text',
} as const;
