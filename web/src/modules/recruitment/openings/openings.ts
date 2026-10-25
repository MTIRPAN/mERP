import { useQueryClient } from '@tanstack/react-query';
import {
  getGetOpeningQueryKey,
  getListOpeningsQueryKey,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type {
  OpeningQuestionInput,
  OpeningStatus,
  OpeningVisibility,
  QuestionType,
  WorkModel,
} from '@/shared/api/generated/model';

export const openingStatuses: OpeningStatus[] = ['Draft', 'Published', 'OnHold', 'Closed'];

export const workModels: WorkModel[] = ['OnSite', 'Remote', 'Hybrid'];

export const visibilities: OpeningVisibility[] = ['Public', 'Internal'];

export const questionTypes: QuestionType[] = ['Text', 'LongText', 'YesNo', 'SingleChoice', 'MultiChoice', 'Number'];

/** Mirrors OpeningQuestion.Yes / No: the answers of a yes/no question. */
export const YES = 'yes';
export const NO = 'no';

export const openingStatusColors: Record<OpeningStatus, string> = {
  Draft: 'default',
  Published: 'success',
  OnHold: 'warning',
  Closed: 'default',
};

/** Questions with options or yes/no answers; only they may be knockout questions. */
export const isChoice = (type: QuestionType | undefined) => type === 'SingleChoice' || type === 'MultiChoice';

export const canKnockOut = (type: QuestionType | undefined) => type === 'YesNo' || isChoice(type);

/** The answers a knockout question may accept: its options, or yes and no. */
export const answersOf = (question: Partial<OpeningQuestionInput>): string[] =>
  question.type === 'YesNo'
    ? [YES, NO]
    : isChoice(question.type)
      ? (question.options ?? []).map((o) => o?.trim()).filter((o): o is string => !!o)
      : [];

/** Refreshes the list and an opening after a change. */
export const useOpeningRefresh = () => {
  const queryClient = useQueryClient();
  return (id?: string) =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: getListOpeningsQueryKey() }),
      ...(id ? [queryClient.invalidateQueries({ queryKey: getGetOpeningQueryKey(id) })] : []),
    ]);
};
