import type { ApprovalFactItem, ApprovalFactType } from '@/shared/api/generated/model';

/** Operators of the condition language (docs/domain/yetki-ve-onay.md §2.3). */
export const OPERATORS = [
  'eq',
  'ne',
  'gt',
  'gte',
  'lt',
  'lte',
  'in',
  'nin',
  'contains',
  'isEmpty',
  'isNotEmpty',
] as const;

export type Operator = (typeof OPERATORS)[number];

export type ConditionValue = string | number | boolean | (string | number)[] | null | undefined;

export interface RuleModel {
  kind: 'rule';
  fact?: string;
  op?: Operator;
  value?: ConditionValue;
}

export interface GroupModel {
  kind: 'group';
  combinator: 'all' | 'any';
  items: ConditionModel[];
}

export type ConditionModel = RuleModel | GroupModel;

/** Groups nest at most this deep in the editor; the server allows more. */
export const MAX_GROUP_DEPTH = 2;

/** Operators the server accepts for a fact type (ApprovalConditions.Supports). */
export const operatorsFor = (type: ApprovalFactType | undefined): Operator[] =>
  OPERATORS.filter((op) => {
    switch (op) {
      case 'gt':
      case 'gte':
      case 'lt':
      case 'lte':
        return type === 'Number' || type === 'Date';
      case 'contains':
        return type === 'Text' || type === 'TextList';
      case 'in':
      case 'nin':
        return type !== 'TextList';
      default:
        return true;
    }
  });

/** Operators that compare with nothing. */
export const takesNoValue = (op: Operator | undefined) => op === 'isEmpty' || op === 'isNotEmpty';

/** Operators that compare with a list. */
export const takesList = (op: Operator | undefined) => op === 'in' || op === 'nin';

export const emptyGroup = (combinator: GroupModel['combinator'] = 'all'): GroupModel => ({
  kind: 'group',
  combinator,
  items: [{ kind: 'rule' }],
});

const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

const readNode = (node: unknown): ConditionModel | undefined => {
  if (!isObject(node)) {
    return undefined;
  }

  for (const combinator of ['all', 'any'] as const) {
    const items = node[combinator];
    if (Array.isArray(items)) {
      return {
        kind: 'group',
        combinator,
        items: items.map(readNode).filter((item): item is ConditionModel => item !== undefined),
      };
    }
  }

  return {
    kind: 'rule',
    fact: typeof node.fact === 'string' ? node.fact : undefined,
    op: OPERATORS.includes(node.op as Operator) ? (node.op as Operator) : undefined,
    value: node.value as ConditionValue,
  };
};

/** The JSON rule tree of a definition → editor model; a single rule is shown in an "all" group. */
export const fromJson = (json: unknown): GroupModel | undefined => {
  const node = readNode(json);
  if (!node) {
    return undefined;
  }

  return node.kind === 'group' ? node : { kind: 'group', combinator: 'all', items: [node] };
};

const writeNode = (node: ConditionModel): object | undefined => {
  if (node.kind === 'group') {
    const items = node.items.map(writeNode).filter((item): item is object => item !== undefined);
    return items.length > 0 ? { [node.combinator]: items } : undefined;
  }

  if (!node.fact || !node.op) {
    return undefined;
  }

  return takesNoValue(node.op)
    ? { fact: node.fact, op: node.op }
    : { fact: node.fact, op: node.op, value: node.value ?? null };
};

/** Editor model → JSON rule tree; rules without a fact or operator are left out, an empty tree is null. */
export const toJson = (group: GroupModel | undefined): object | null => (group ? (writeNode(group) ?? null) : null);

/** A typed value from the editor's text, e.g. "12" → 12 for a number fact; lists are split on commas. */
export const parseValue = (
  raw: string,
  type: ApprovalFactType | undefined,
  op: Operator | undefined,
): ConditionValue => {
  const one = (text: string): string | number => {
    const trimmed = text.trim();
    return type === 'Number' && trimmed !== '' && !Number.isNaN(Number(trimmed)) ? Number(trimmed) : trimmed;
  };

  if (takesList(op)) {
    return raw
      .split(',')
      .map(one)
      .filter((item) => item !== '');
  }

  return one(raw);
};

/** A value as editor text; lists are joined with commas. */
export const formatValue = (value: ConditionValue): string =>
  Array.isArray(value) ? value.join(', ') : value === null || value === undefined ? '' : String(value);

export const factType = (facts: ApprovalFactItem[], name: string | undefined) =>
  facts.find((f) => f.name === name)?.type;
