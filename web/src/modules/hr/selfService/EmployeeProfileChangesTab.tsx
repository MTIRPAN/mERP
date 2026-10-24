import { useListEmployeeProfileChanges } from '@/shared/api/generated/endpoints/hr/hr';
import { ProfileChangeList } from './ProfileChangeList';

/** HR's view of a person's change requests on the card (D39); approvers open it from their task. */
export const EmployeeProfileChangesTab = ({ employeeId }: { employeeId: string }) => {
  const requests = useListEmployeeProfileChanges(employeeId);
  return <ProfileChangeList items={requests.data} loading={requests.isLoading} />;
};
