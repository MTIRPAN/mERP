import { useQueryClient } from '@tanstack/react-query';
import {
  addCandidateNote,
  addMyHiringCandidateNote,
  getGetCandidateQueryKey,
  getGetMyHiringCandidateQueryKey,
  getListCandidatesQueryKey,
  removeCandidateNote,
  removeMyHiringCandidateNote,
  useGetCandidate,
  useGetMyHiringCandidate,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { CandidateDetails } from '@/shared/api/generated/model';

/** The API code of an e-mail another candidate has (D50); the form offers that candidate. */
export const EMAIL_TAKEN = 'recruitment.candidates.errors.email_taken';

/** The source whose candidates name the employee who referred them. */
export const REFERRAL = 'REFERRAL';

/**
 * A candidate's card: the recruiters' endpoint, or from self service the hiring team's (D50). Both hooks are called
 * so the rules of hooks hold; only one fetches.
 */
export const useCandidateCard = (id: string, team: boolean) => {
  const recruiter = useGetCandidate(id, { query: { enabled: !team } });
  const member = useGetMyHiringCandidate(id, { query: { enabled: team } });
  return team ? member : recruiter;
};

/** Refreshes the card and the pool after a change. */
export const useCandidateRefresh = () => {
  const queryClient = useQueryClient();
  return (id: string) =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: getGetCandidateQueryKey(id) }),
      queryClient.invalidateQueries({ queryKey: getGetMyHiringCandidateQueryKey(id) }),
      queryClient.invalidateQueries({ queryKey: getListCandidatesQueryKey() }),
    ]);
};

export const addNote = (id: string, team: boolean, body: string, mentionedUserIds: string[]) =>
  team ? addMyHiringCandidateNote(id, { body, mentionedUserIds }) : addCandidateNote(id, { body, mentionedUserIds });

export const removeNote = (id: string, noteId: string, team: boolean) =>
  team ? removeMyHiringCandidateNote(id, noteId) : removeCandidateNote(id, noteId);

/**
 * The users still mentioned in a note: those picked from the list whose <c>@name</c> is still in the text (a name
 * deleted from the text is not notified).
 */
export const mentionedIn = (body: string, picked: Map<string, string>): string[] =>
  [...picked].filter(([name]) => body.includes(`@${name}`)).map(([, userId]) => userId);

export const fullName = (candidate: Pick<CandidateDetails, 'firstName' | 'lastName'>) =>
  `${candidate.firstName} ${candidate.lastName}`;
