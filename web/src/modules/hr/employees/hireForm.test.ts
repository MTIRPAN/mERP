import dayjs from 'dayjs';
import { describe, expect, it } from 'vitest';
import { toStatusParam } from './employeeFilters';
import { stepOfField, toHireCommand } from './hireForm';

describe('toHireCommand', () => {
  const base = {
    tckn: ' 10000000146 ',
    firstName: ' Ayşe ',
    lastName: 'Yılmaz',
    nationality: 'tr',
    companyId: 'c1',
    hireDate: dayjs('2026-02-01'),
    positionId: 'p1',
  };

  it('trims the text, formats the days and leaves the defaults to the server', () => {
    const command = toHireCommand({ ...base, employmentType: 'Indefinite', level: '  ' }, {});

    expect(command).toMatchObject({
      tckn: '10000000146',
      firstName: 'Ayşe',
      nationality: 'TR',
      hireDate: '2026-02-01',
      employmentType: 'Indefinite',
      seniorityDate: null,
      level: null,
      contractEndDate: null,
      weeklyHours: null,
    });
  });

  it('sends the contract end and the weekly hours only with their employment type', () => {
    const values = { ...base, contractEndDate: dayjs('2026-12-31'), weeklyHours: 20 };

    expect(toHireCommand({ ...values, employmentType: 'FixedTerm' }, {})).toMatchObject({
      contractEndDate: '2026-12-31',
      weeklyHours: null,
    });
    expect(toHireCommand({ ...values, employmentType: 'PartTime' }, {})).toMatchObject({
      contractEndDate: null,
      weeklyHours: 20,
    });
  });

  it('sends an earlier seniority date with its reason, and drops one equal to the hire date', () => {
    expect(
      toHireCommand({ ...base, seniorityDate: dayjs('2020-01-01'), seniorityReason: ' Grup şirketi ' }, {}),
    ).toMatchObject({ seniorityDate: '2020-01-01', seniorityReason: 'Grup şirketi' });
    expect(toHireCommand({ ...base, seniorityDate: dayjs('2026-02-01'), seniorityReason: 'x' }, {})).toMatchObject({
      seniorityDate: null,
      seniorityReason: null,
    });
  });
});

describe('stepOfField', () => {
  it('opens the step that shows the field of an API error', () => {
    expect(stepOfField('tckn')).toBe(0);
    expect(stepOfField('seniorityReason')).toBe(1);
    expect(stepOfField('positionId')).toBe(2);
    expect(stepOfField('customFields.shoe_size')).toBe(2);
    expect(stepOfField('unknown')).toBe(0);
  });
});

describe('toStatusParam', () => {
  it('lists who works today unless another state is chosen', () => {
    expect(toStatusParam('active')).toBeUndefined();
    expect(toStatusParam('terminated')).toEqual(['Terminated']);
    expect(toStatusParam('all')).toEqual(['Upcoming', 'Active', 'Terminated']);
  });
});
