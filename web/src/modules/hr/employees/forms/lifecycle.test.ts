import dayjs from 'dayjs';
import { describe, expect, it } from 'vitest';
import { changeReasons, toAssignment, toTerms } from './lifecycle';

describe('lifecycle commands', () => {
  it('never offers "Hire" as the reason of an assignment change', () => {
    expect(changeReasons).not.toContain('Hire');
    expect(changeReasons).toContain('Promotion');
  });

  it('sends the assignment with blank texts as null, so the server copies them from the position', () => {
    expect(toAssignment({ positionId: 'p1', level: ' ', costCenterCode: ' MM-1 ' })).toEqual({
      positionId: 'p1',
      branchId: null,
      managerPositionId: null,
      level: null,
      costCenterCode: 'MM-1',
    });
  });

  it('sends an earlier seniority date with its reason, unless the transfer keeps the seniority', () => {
    const values = {
      employmentType: 'FixedTerm' as const,
      contractEndDate: dayjs('2026-12-31'),
      seniorityDate: dayjs('2020-01-01'),
      seniorityReason: ' Önceki hizmet ',
    };

    expect(toTerms(values, '2026-02-01')).toMatchObject({
      contractEndDate: '2026-12-31',
      weeklyHours: null,
      seniorityDate: '2020-01-01',
      seniorityReason: 'Önceki hizmet',
    });
    expect(toTerms(values, '2026-02-01', false)).toMatchObject({ seniorityDate: null, seniorityReason: null });
    expect(toTerms({ ...values, seniorityDate: dayjs('2026-02-01') }, '2026-02-01')).toMatchObject({
      seniorityDate: null,
    });
  });
});
