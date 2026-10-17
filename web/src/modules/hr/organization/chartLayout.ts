import { hierarchy, tree } from 'd3-hierarchy';
import type { OrgUnitTreeNode } from '@/shared/api/generated/model';
import { buildUnitTree, type UnitNode } from './unitTree';

export const CARD_WIDTH = 220;
export const CARD_HEIGHT = 104;
const GAP_X = 24;
const GAP_Y = 48;

/** A unit card; x and y are its top left corner. */
export interface ChartNode {
  unit: OrgUnitTreeNode;
  x: number;
  y: number;
}

/** A connector from the bottom of a parent card to the top of a child card. */
export interface ChartLink {
  id: string;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

export interface ChartLayout {
  nodes: ChartNode[];
  links: ChartLink[];
  width: number;
  height: number;
}

type LayoutDatum = { node?: UnitNode; children: LayoutDatum[] };

/**
 * Top down tree layout of the units with d3-hierarchy (ISC, D18). Several top units share a hidden root, so the
 * whole company is one chart; the hidden level is not drawn.
 */
export const layoutOrgChart = (units: OrgUnitTreeNode[]): ChartLayout => {
  if (units.length === 0) {
    return { nodes: [], links: [], width: 0, height: 0 };
  }

  const wrap = (node: UnitNode): LayoutDatum => ({ node, children: node.children.map(wrap) });
  const root = hierarchy<LayoutDatum>({ children: buildUnitTree(units).map(wrap) }, (d) => d.children);
  const positioned = tree<LayoutDatum>().nodeSize([CARD_WIDTH + GAP_X, CARD_HEIGHT + GAP_Y])(root);
  const drawn = positioned.descendants().filter((d) => d.depth > 0);
  const minX = Math.min(...drawn.map((d) => d.x));
  const left = (x: number) => x - minX;
  const top = (depth: number) => (depth - 1) * (CARD_HEIGHT + GAP_Y);

  const nodes = drawn.map((d) => ({ unit: d.data.node!.unit, x: left(d.x), y: top(d.depth) }));
  const links = drawn
    .filter((d) => d.parent && d.parent.depth > 0)
    .map((d) => ({
      id: `${d.parent!.data.node!.unit.id}-${d.data.node!.unit.id}`,
      x1: left(d.parent!.x) + CARD_WIDTH / 2,
      y1: top(d.parent!.depth) + CARD_HEIGHT,
      x2: left(d.x) + CARD_WIDTH / 2,
      y2: top(d.depth),
    }));

  return {
    nodes,
    links,
    width: Math.max(...nodes.map((n) => n.x)) + CARD_WIDTH,
    height: Math.max(...nodes.map((n) => n.y)) + CARD_HEIGHT,
  };
};

/** Vertical S curve between two cards. */
export const linkPath = ({ x1, y1, x2, y2 }: ChartLink): string => {
  const middle = (y1 + y2) / 2;
  return `M ${x1} ${y1} C ${x1} ${middle}, ${x2} ${middle}, ${x2} ${y2}`;
};

const MIN_SCALE = 0.2;
const MAX_SCALE = 2;
const PADDING = 24;

/** Pan and zoom of the chart: the chart's top left corner on screen and its scale. */
export interface View {
  x: number;
  y: number;
  scale: number;
}

export const clamp = (scale: number) => Math.min(MAX_SCALE, Math.max(MIN_SCALE, scale));

/** The whole chart in view, centred horizontally; never larger than its natural size. */
export const fitView = (layout: ChartLayout, width: number, height: number): View => {
  if (width === 0 || layout.width === 0) {
    return { x: PADDING, y: PADDING, scale: 1 };
  }

  const scale = clamp(Math.min(1, (width - 2 * PADDING) / layout.width, (height - 2 * PADDING) / layout.height));
  return { x: (width - layout.width * scale) / 2, y: PADDING, scale };
};
