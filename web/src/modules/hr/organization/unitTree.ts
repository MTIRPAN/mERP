import type { OrgUnitTreeNode } from '@/shared/api/generated/model';

/** A unit with its sub units, built from the flat list of the tree endpoint. */
export interface UnitNode {
  unit: OrgUnitTreeNode;
  children: UnitNode[];
}

/**
 * Nests the units under their parents, keeping the server's order (by name). Units whose parent is outside the
 * caller's scope come without a parent and become top units.
 */
export const buildUnitTree = (units: OrgUnitTreeNode[]): UnitNode[] => {
  const nodes = new Map(units.map((unit) => [unit.id, { unit, children: [] as UnitNode[] }]));
  const roots: UnitNode[] = [];
  for (const node of nodes.values()) {
    const parent = node.unit.parentId ? nodes.get(node.unit.parentId) : undefined;
    if (parent) {
      parent.children.push(node);
    } else {
      roots.push(node);
    }
  }

  return roots;
};

/** The unit and all of its sub units: a unit cannot move under them. */
export const subtreeIds = (units: OrgUnitTreeNode[], unitId: string): Set<string> => {
  const ids = new Set([unitId]);
  let grew = true;
  while (grew) {
    grew = false;
    for (const unit of units) {
      if (unit.parentId && ids.has(unit.parentId) && !ids.has(unit.id)) {
        ids.add(unit.id);
        grew = true;
      }
    }
  }

  return ids;
};

/** Staffing of the whole company: active seats, taken seats and vacancies. */
export const totals = (units: OrgUnitTreeNode[]) =>
  units.reduce(
    (sum, unit) => ({ norm: sum.norm + unit.norm, filled: sum.filled + unit.filled, vacant: sum.vacant + unit.vacant }),
    { norm: 0, filled: 0, vacant: 0 },
  );

/** Up to two initials, shown until photos come with the employee card (D37). */
export const initials = (name: string, locale: string): string =>
  name
    .split(/\s+/)
    .filter((word) => word.length > 0)
    .slice(0, 2)
    .map((word) => word.charAt(0).toLocaleUpperCase(locale))
    .join('');

/** Tree options of antd TreeSelect, without the given units. */
export interface UnitOption {
  value: string;
  title: string;
  children: UnitOption[];
}

export const toUnitOptions = (nodes: UnitNode[], excluded: Set<string> = new Set()): UnitOption[] =>
  nodes
    .filter((node) => !excluded.has(node.unit.id))
    .map((node) => ({
      value: node.unit.id,
      title: `${node.unit.name} (${node.unit.code})`,
      children: toUnitOptions(node.children, excluded),
    }));
