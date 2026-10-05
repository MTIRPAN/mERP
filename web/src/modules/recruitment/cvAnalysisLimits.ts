/** Mirrors AnalyzeCvQuery limits; the API checks them again. */
export const cvAnalysisLimits = {
  maxFileBytes: 5 * 1024 * 1024,
  maxJobTitleLength: 200,
  maxJobRequirementsLength: 8000,
  maxCandidateNameLength: 200,
  accept: '.pdf,.docx',
} as const;
