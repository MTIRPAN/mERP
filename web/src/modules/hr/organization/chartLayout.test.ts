import { describe, expect, it } from 'vitest';
import { CARD_HEIGHT, CARD_WIDTH, fitView, layoutOrgChart, linkPath } from './chartLayout';
import { unit } from '@/test/orgUnits';

describe('layoutOrgChart', () => {
  it('places three levels top down with a connector for each parent and child', () => {
    const layout = layoutOrgChart([unit('gm', null), unit('satis', 'gm'), unit('ik', 'gm'), unit('ist', 'satis')]);

    const at = (id: string) => layout.nodes.find((n) => n.unit.id === id)!;
    expect(at('gm').y).toBe(0);
    expect(at('satis').y).toBe(at('ik').y);
    expect(at('ist').y).toBeGreaterThan(at('satis').y);
    // Children sit side by side, their parent centred above them
    expect(Math.abs(at('satis').x - at('ik').x)).toBeGreaterThanOrEqual(CARD_WIDTH);
    expect(at('gm').x).toBe((at('satis').x + at('ik').x) / 2);
    expect(Math.min(...layout.nodes.map((n) => n.x))).toBe(0);

    expect(layout.links.map((l) => l.id).sort()).toEqual(['gm-ik', 'gm-satis', 'satis-ist']);
    const link = layout.links.find((l) => l.id === 'satis-ist')!;
    expect(link).toMatchObject({
      x1: at('satis').x + CARD_WIDTH / 2,
      y1: at('satis').y + CARD_HEIGHT,
      y2: at('ist').y,
    });
    expect(layout.height).toBe(at('ist').y + CARD_HEIGHT);
  });

  it('draws several top units side by side without the hidden root', () => {
    const layout = layoutOrgChart([unit('a', null), unit('b', null)]);

    expect(layout.nodes.map((n) => n.y)).toEqual([0, 0]);
    expect(layout.links).toEqual([]);
    expect(layout.width).toBeGreaterThanOrEqual(2 * CARD_WIDTH);
  });

  it('is empty without units', () => {
    expect(layoutOrgChart([])).toEqual({ nodes: [], links: [], width: 0, height: 0 });
  });
});

describe('linkPath', () => {
  it('bends halfway between the cards', () => {
    expect(linkPath({ id: 'x', x1: 0, y1: 0, x2: 100, y2: 40 })).toBe('M 0 0 C 0 20, 100 20, 100 40');
  });
});

describe('fitView', () => {
  const layout = { nodes: [], links: [], width: 2000, height: 400 };

  it('shrinks a wide chart into the container and centres it', () => {
    const view = fitView(layout, 1048, 560);

    expect(view.scale).toBe(0.5);
    expect(view.x).toBe((1048 - 1000) / 2);
  });

  it('never enlarges a small chart', () => {
    expect(fitView({ ...layout, width: 300, height: 100 }, 1000, 560).scale).toBe(1);
  });
});
