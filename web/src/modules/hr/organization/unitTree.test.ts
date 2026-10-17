import { describe, expect, it } from 'vitest';
import { unit } from '@/test/orgUnits';
import { buildUnitTree, initials, subtreeIds, toUnitOptions, totals } from './unitTree';

// Genel müdürlük → satış → İstanbul; pazarlama has its parent outside the caller's scope
const units = [unit('gm', null), unit('satis', 'gm'), unit('ist', 'satis'), unit('pazarlama', 'hidden')];

describe('buildUnitTree', () => {
  it('nests units under their parents and keeps orphans at the top', () => {
    const tree = buildUnitTree(units);

    expect(tree.map((n) => n.unit.id)).toEqual(['gm', 'pazarlama']);
    expect(tree[0]?.children.map((n) => n.unit.id)).toEqual(['satis']);
    expect(tree[0]?.children[0]?.children.map((n) => n.unit.id)).toEqual(['ist']);
  });
});

describe('subtreeIds', () => {
  it('holds the unit and every unit below it', () => {
    expect([...subtreeIds(units, 'satis')].sort()).toEqual(['ist', 'satis']);
    expect([...subtreeIds(units, 'gm')].sort()).toEqual(['gm', 'ist', 'satis']);
  });
});

describe('toUnitOptions', () => {
  it('leaves out the excluded units with their sub units', () => {
    const options = toUnitOptions(buildUnitTree(units), subtreeIds(units, 'satis'));

    expect(options.map((o) => o.value)).toEqual(['gm', 'pazarlama']);
    expect(options[0]?.children).toEqual([]);
    expect(options[0]?.title).toBe('Birim gm (GM)');
  });
});

describe('totals', () => {
  it('adds up the seats of every unit', () => {
    expect(
      totals([unit('a', null, { norm: 4, filled: 1, vacant: 3 }), unit('b', 'a', { norm: 2, vacant: 2 })]),
    ).toEqual({
      norm: 6,
      filled: 1,
      vacant: 5,
    });
  });
});

describe('initials', () => {
  it('takes the first letters of the first two words in the culture', () => {
    expect(initials('insan kaynakları müdürlüğü', 'tr-TR')).toBe('İK');
    expect(initials('  Satış  ', 'tr-TR')).toBe('S');
  });
});
