import type {
  CvEducationLevel,
  CvLanguageLevel,
  CvRecommendation,
  CvRequirementStatus,
  CvSeniority,
  CvSkillCategory,
  CvSkillLevel,
} from '@/shared/api/generated/model';

// Localization keys of the analysis enums; typed so a new server value fails the type check.
const prefix = 'web.recruitment.cv_analysis';

export const seniorityKeys: Record<CvSeniority, string> = {
  Unknown: `${prefix}.seniority.unknown`,
  Intern: `${prefix}.seniority.intern`,
  Junior: `${prefix}.seniority.junior`,
  MidLevel: `${prefix}.seniority.mid_level`,
  Senior: `${prefix}.seniority.senior`,
  Lead: `${prefix}.seniority.lead`,
  Manager: `${prefix}.seniority.manager`,
  Director: `${prefix}.seniority.director`,
  Executive: `${prefix}.seniority.executive`,
};

export const recommendationKeys: Record<CvRecommendation, string> = {
  StrongMatch: `${prefix}.recommendation.strong_match`,
  Match: `${prefix}.recommendation.match`,
  PartialMatch: `${prefix}.recommendation.partial_match`,
  WeakMatch: `${prefix}.recommendation.weak_match`,
};

export const recommendationColors: Record<CvRecommendation, string> = {
  StrongMatch: 'green',
  Match: 'blue',
  PartialMatch: 'gold',
  WeakMatch: 'default',
};

export const requirementStatusKeys: Record<CvRequirementStatus, string> = {
  Met: `${prefix}.requirement_status.met`,
  Partial: `${prefix}.requirement_status.partial`,
  NotMet: `${prefix}.requirement_status.not_met`,
  Unclear: `${prefix}.requirement_status.unclear`,
};

export const requirementStatusColors: Record<CvRequirementStatus, string> = {
  Met: 'success',
  Partial: 'warning',
  NotMet: 'error',
  Unclear: 'default',
};

export const skillCategoryKeys: Record<CvSkillCategory, string> = {
  Technical: `${prefix}.skill_category.technical`,
  Domain: `${prefix}.skill_category.domain`,
  Tool: `${prefix}.skill_category.tool`,
  Soft: `${prefix}.skill_category.soft`,
  Other: `${prefix}.skill_category.other`,
};

export const skillLevelKeys: Record<CvSkillLevel, string> = {
  Unknown: `${prefix}.skill_level.unknown`,
  Basic: `${prefix}.skill_level.basic`,
  Intermediate: `${prefix}.skill_level.intermediate`,
  Advanced: `${prefix}.skill_level.advanced`,
  Expert: `${prefix}.skill_level.expert`,
};

export const educationLevelKeys: Record<CvEducationLevel, string> = {
  HighSchool: `${prefix}.education_level.high_school`,
  Associate: `${prefix}.education_level.associate`,
  Bachelor: `${prefix}.education_level.bachelor`,
  Master: `${prefix}.education_level.master`,
  Doctorate: `${prefix}.education_level.doctorate`,
  Certificate: `${prefix}.education_level.certificate`,
  Other: `${prefix}.education_level.other`,
};

/** CEFR levels are shown as they are; only the two words are translated. */
export const languageLevelKey = (level: CvLanguageLevel): string | undefined =>
  level === 'Native'
    ? `${prefix}.language_level.native`
    : level === 'Unknown'
      ? `${prefix}.language_level.unknown`
      : undefined;

export const redactionKindKeys: Record<string, string> = {
  email: `${prefix}.redaction.email`,
  phone: `${prefix}.redaction.phone`,
  national_id: `${prefix}.redaction.national_id`,
  iban: `${prefix}.redaction.iban`,
  url: `${prefix}.redaction.url`,
  name: `${prefix}.redaction.name`,
  sensitive_field: `${prefix}.redaction.sensitive_field`,
};
