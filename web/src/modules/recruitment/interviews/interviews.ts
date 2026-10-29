import type { EventInput } from '@fullcalendar/core';
import { useQueryClient } from '@tanstack/react-query';
import {
  getGetApplicationQueryKey,
  getGetInterviewQueryKey,
  getListInterviewsQueryKey,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type {
  EvaluationForm,
  InterviewListItem,
  InterviewStatus,
  InterviewType,
  Recommendation,
} from '@/shared/api/generated/model';

export const interviewTypes: InterviewType[] = ['Phone', 'Video', 'InPerson', 'Technical', 'Hr', 'Manager', 'Panel'];

export const interviewStatusColors: Record<InterviewStatus, string> = {
  Scheduled: 'processing',
  Completed: 'success',
  Cancelled: 'default',
  NoShow: 'warning',
};

/** The calendar colours of the states (antd's palette). */
const calendarColors: Record<InterviewStatus, string> = {
  Scheduled: '#1677ff',
  Completed: '#52c41a',
  Cancelled: '#bfbfbf',
  NoShow: '#faad14',
};

export const recommendations: Recommendation[] = ['StrongNo', 'No', 'Yes', 'StrongYes'];

export const recommendationColors: Record<Recommendation, string> = {
  StrongNo: 'red',
  No: 'volcano',
  Yes: 'green',
  StrongYes: 'cyan',
};

/** Scores run from 1 to 5; each has a description in the form (D52). */
export const SCORES = [1, 2, 3, 4, 5];

/** A weighted score with up to two decimals in the reader's language. */
export const formatScore = (score: number, locale: string) =>
  new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(score);

/** The criteria of a form in order, across its sections. */
export const criteriaOf = (form: EvaluationForm) => form.sections.flatMap((s) => s.criteria);

export const toCalendarEvents = (interviews: InterviewListItem[]): EventInput[] =>
  interviews.map((interview) => ({
    id: interview.id,
    title: `${interview.candidateName} · ${interview.openingTitle}`,
    start: interview.startsAt,
    end: interview.endsAt,
    color: calendarColors[interview.status],
  }));

/** Refreshes the interview lists, an interview and its application after a change. */
export const useInterviewRefresh = () => {
  const queryClient = useQueryClient();
  return (interviewId: string | undefined, applicationId: string) =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: getListInterviewsQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getGetApplicationQueryKey(applicationId) }),
      ...(interviewId ? [queryClient.invalidateQueries({ queryKey: getGetInterviewQueryKey(interviewId) })] : []),
    ]);
};
