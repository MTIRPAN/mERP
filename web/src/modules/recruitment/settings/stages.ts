import type { StageInput, StageType } from '@/shared/api/generated/model';

export const stageTypes: StageType[] = [
  'Applied',
  'Screening',
  'Interview',
  'Assessment',
  'Reference',
  'Offer',
  'Hired',
  'Rejected',
  'Withdrawn',
];

/** The stages an application leaves the pipeline at. */
export const finalTypes: StageType[] = ['Hired', 'Rejected', 'Withdrawn'];

export const stageTypeColors: Record<StageType, string> = {
  Applied: 'blue',
  Screening: 'cyan',
  Interview: 'geekblue',
  Assessment: 'purple',
  Reference: 'magenta',
  Offer: 'gold',
  Hired: 'green',
  Rejected: 'red',
  Withdrawn: 'default',
};

/** A stage as the stage list edits it; names are kept in every language though only the default one is edited. */
export interface StageValues {
  id?: string;
  name?: Record<string, string>;
  type?: StageType;
  slaDays?: number | null;
}

export const toStageInputs = (stages: StageValues[] | undefined): StageInput[] =>
  (stages ?? []).map((s) => ({ id: s.id ?? null, name: s.name ?? {}, type: s.type!, slaDays: s.slaDays ?? null }));
