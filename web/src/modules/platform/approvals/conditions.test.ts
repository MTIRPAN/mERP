import { describe, expect, it } from 'vitest';
import { formatValue, fromJson, operatorsFor, parseValue, toJson, type GroupModel } from './conditions';

const specExample = {
  all: [
    { fact: 'leaveTypeCode', op: 'in', value: ['YILLIK', 'UCRETSIZ'] },
    {
      any: [
        { fact: 'days', op: 'gt', value: 5 },
        { fact: 'isPeakSeason', op: 'eq', value: true },
      ],
    },
  ],
};

describe('conditions', () => {
  it('reads and writes the spec example unchanged', () => {
    expect(toJson(fromJson(specExample))).toEqual(specExample);
  });

  it('wraps a single rule in an all group and drops incomplete rules', () => {
    const group = fromJson({ fact: 'days', op: 'isEmpty' })!;
    expect(group).toEqual({
      kind: 'group',
      combinator: 'all',
      items: [{ kind: 'rule', fact: 'days', op: 'isEmpty', value: undefined }],
    });

    const partial: GroupModel = {
      kind: 'group',
      combinator: 'any',
      items: [{ kind: 'rule', fact: 'days' }, ...group.items],
    };
    expect(toJson(partial)).toEqual({ any: [{ fact: 'days', op: 'isEmpty' }] });
    expect(toJson({ kind: 'group', combinator: 'all', items: [{ kind: 'rule' }] })).toBeNull();
    expect(toJson(undefined)).toBeNull();
    expect(fromJson(null)).toBeUndefined();
  });

  it('offers only the operators the fact type allows', () => {
    expect(operatorsFor('Number')).toContain('gt');
    expect(operatorsFor('Text')).not.toContain('gt');
    expect(operatorsFor('Text')).toContain('contains');
    expect(operatorsFor('TextList')).not.toContain('in');
    expect(operatorsFor('Boolean')).toEqual(['eq', 'ne', 'in', 'nin', 'isEmpty', 'isNotEmpty']);
  });

  it('parses typed values and lists', () => {
    expect(parseValue('12', 'Number', 'gt')).toBe(12);
    expect(parseValue('YILLIK, UCRETSIZ', 'Text', 'in')).toEqual(['YILLIK', 'UCRETSIZ']);
    expect(parseValue('1, 2', 'Number', 'nin')).toEqual([1, 2]);
    expect(parseValue(' A ', 'Text', 'eq')).toBe('A');
    expect(formatValue(['A', 'B'])).toBe('A, B');
    expect(formatValue(undefined)).toBe('');
  });
});
