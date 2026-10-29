import { useQueryClient } from '@tanstack/react-query';
import dayjs from 'dayjs';
import {
  downloadApplicationCv,
  downloadMyHiringApplicationCv,
  getApplication,
  getGetApplicationQueryKey,
  getGetOpeningPipelineQueryKey,
  getListApplicationsQueryKey,
  getListOpeningsQueryKey,
  moveApplication,
  rejectApplication,
  reopenApplication,
  useGetApplication,
  useGetMyHiringApplication,
  useGetMyHiringOpeningPipeline,
  useGetOpeningPipeline,
  useListApplications,
  useListMyHiringApplications,
  withdrawApplication,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { ApplicationListItem, ApplicationStatus, StageType } from '@/shared/api/generated/model';
import { saveFile } from '@/shared/api/download';

/** The kanban shows at most one page of applications; the list view pages through all. */
export const KANBAN_PAGE_SIZE = 200;

export const applicationStatuses: ApplicationStatus[] = ['Active', 'Hired', 'Rejected', 'Withdrawn'];

export const applicationStatusColors: Record<ApplicationStatus, string> = {
  Active: 'processing',
  Hired: 'success',
  Rejected: 'error',
  Withdrawn: 'default',
};

/** The days the rejection e-mail may wait (0 sends it now; at most 14, D50). */
export const emailDelays = [0, 1, 2, 3, 7, 14];

/** Dropping an application here needs a reason. */
export const needsReason = (type: StageType | null | undefined) => type === 'Rejected' || type === 'Withdrawn';

/** An active application past its stage's SLA. */
export const isOverdue = (application: ApplicationListItem, now = dayjs()) =>
  !!application.slaDueAt && dayjs(application.slaDueAt).isBefore(now);

/** Whole days since the application came to its stage. */
export const daysInStage = (application: ApplicationListItem, now = dayjs()) =>
  now.diff(dayjs(application.stageEnteredAt), 'day');

/**
 * An opening's pipeline: the recruiters' endpoints, or from self service the hiring team's (D50). Both hooks are
 * called so the rules of hooks hold; only one fetches.
 */
export const usePipeline = (openingId: string, team: boolean) => {
  const recruiter = useGetOpeningPipeline(openingId, { query: { enabled: !team } });
  const member = useGetMyHiringOpeningPipeline(openingId, { query: { enabled: team } });
  return team ? member : recruiter;
};

export const useOpeningApplications = (openingId: string, team: boolean) => {
  const params = { openingId, pageSize: KANBAN_PAGE_SIZE };
  const recruiter = useListApplications(params, { query: { enabled: !team } });
  const member = useListMyHiringApplications(params, { query: { enabled: team } });
  return team ? member : recruiter;
};

export const useApplicationDetails = (id: string, team: boolean) => {
  const recruiter = useGetApplication(id, { query: { enabled: !team } });
  const member = useGetMyHiringApplication(id, { query: { enabled: team } });
  return team ? member : recruiter;
};

export const downloadCv = (id: string, team: boolean, fileName: string) =>
  saveFile(team ? downloadMyHiringApplicationCv(id) : downloadApplicationCv(id), fileName);

export interface LeaveValues {
  reasonCode: string;
  note?: string;
  sendEmail?: boolean;
  emailDelayDays?: number;
}

/**
 * Moves, rejects, withdraws and reopens the applications of an opening, then refreshes the pipeline, its
 * applications and the openings' counts. A card dropped on the kanban carries no version, so each change sends the
 * version just read.
 */
export const useApplicationActions = (openingId: string) => {
  const queryClient = useQueryClient();
  const refresh = (applicationId: string) =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: getGetOpeningPipelineQueryKey(openingId) }),
      queryClient.invalidateQueries({ queryKey: getListApplicationsQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getListOpeningsQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getGetApplicationQueryKey(applicationId) }),
    ]);
  const change = async (id: string, send: (version: number) => Promise<unknown>) => {
    const { version } = await getApplication(id);
    await send(version);
    await refresh(id);
  };

  return {
    move: (id: string, stageId: string) =>
      change(id, (version) => moveApplication(id, { version, stageId, note: null })),
    reject: (id: string, values: LeaveValues) =>
      change(id, (version) =>
        rejectApplication(id, {
          version,
          reasonCode: values.reasonCode,
          note: values.note ?? null,
          sendEmail: !!values.sendEmail,
          emailDelayDays: values.sendEmail ? (values.emailDelayDays ?? 0) : 0,
        }),
      ),
    withdraw: (id: string, values: LeaveValues) =>
      change(id, (version) =>
        withdrawApplication(id, { version, reasonCode: values.reasonCode, note: values.note ?? null }),
      ),
    reopen: (id: string) => change(id, (version) => reopenApplication(id, { version, note: null })),
  };
};
