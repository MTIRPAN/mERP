import { describe, expect, it } from 'vitest';
import type { ApprovalDefinitionDetails } from '@/shared/api/generated/model';
import { newStep, toCommand, toFormValues } from './flowForm';

const definition: ApprovalDefinitionDetails = {
  id: 'd1',
  documentType: 'hr.leave-request',
  companyId: null,
  name: { 'tr-TR': 'İzin onayı', 'en-US': 'Leave approval' },
  priority: 0,
  entryCondition: null,
  steps: [
    {
      name: { 'tr-TR': 'Yönetici', 'en-US': 'Manager' },
      approver: { resolver: 'line-manager', parameters: { level: '1' } },
      mode: 'Any',
      condition: null,
      dueHours: 24,
      escalation: 'EscalateToManager',
      skipIfApproverIsRequester: true,
      skipIfAlreadyApproved: true,
      allowReturn: false,
    },
    {
      name: { 'tr-TR': 'İK' },
      approver: { resolver: 'role', parameters: { roleId: 'r1' } },
      mode: 'All',
      condition: { any: [{ fact: 'days', op: 'gt', value: 5 }] },
      dueHours: null,
      escalation: 'None',
      skipIfApproverIsRequester: true,
      skipIfAlreadyApproved: false,
      allowReturn: true,
    },
  ],
  fallback: { resolver: 'user', parameters: { userId: 'u1' } },
  isActive: true,
  revision: 3,
  version: 7,
};

describe('flowForm', () => {
  it('round-trips a definition, keeping texts of other cultures', () => {
    const values = toFormValues(definition, 'tr-TR');
    expect(values.steps![0]!.name).toBe('Yönetici');

    values.steps![0]!.name = 'Birim yöneticisi';
    const command = toCommand(values, 'tr-TR');

    expect(command.name).toEqual(definition.name);
    expect(command.steps[0]!.name).toEqual({ 'tr-TR': 'Birim yöneticisi', 'en-US': 'Manager' });
    expect(command.steps[1]!.condition).toEqual(definition.steps[1]!.condition);
    expect(command.steps[1]!).toMatchObject({
      mode: 'All',
      allowReturn: true,
      skipIfAlreadyApproved: false,
      dueHours: null,
    });
    expect(command.fallback).toEqual(definition.fallback);
    expect(command.entryCondition).toBeNull();
  });

  it('drops empty parameters and a fallback without resolver', () => {
    const command = toCommand(
      {
        name: { 'tr-TR': ' Akış ', 'en-US': '' },
        steps: [
          { ...newStep(), name: 'Adım', approver: { resolver: 'user', parameters: { userId: 'u1', other: '' } } },
        ],
        fallback: { resolver: undefined },
      },
      'tr-TR',
    );

    expect(command.name).toEqual({ 'tr-TR': 'Akış' });
    expect(command.steps[0]!.approver).toEqual({ resolver: 'user', parameters: { userId: 'u1' } });
    expect(command.fallback).toBeNull();
    expect(command.priority).toBe(0);
  });
});
