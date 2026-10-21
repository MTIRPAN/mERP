import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import {
  getGetEmployeeQueryKey,
  getListEmployeesQueryKey,
  getListPositionsQueryKey,
} from '@/shared/api/generated/endpoints/hr/hr';

/** Reloads the card, the employee list and the seats of positions after a change of the person. */
export const useEmployeeRefresh = (employeeId: string) => {
  const queryClient = useQueryClient();
  return useCallback(
    () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: getGetEmployeeQueryKey(employeeId) }),
        queryClient.invalidateQueries({ queryKey: getListEmployeesQueryKey() }),
        queryClient.invalidateQueries({ queryKey: getListPositionsQueryKey() }),
      ]),
    [queryClient, employeeId],
  );
};
