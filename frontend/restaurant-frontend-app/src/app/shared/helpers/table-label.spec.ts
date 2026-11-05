import { toTableLabel } from './table-label';

describe('toTableLabel', () => {
  const cases: [string | number | null | undefined, string][] = [
    ['Table 7', 'Table 7'],
    ['table 12', 'table 12'],
    ['7', 'Table 7'],
    [7, 'Table 7'],
    ['Unknown table', 'Unknown table'],
    ['', 'Unknown table'],
    ['   ', 'Unknown table'],
    [null, 'Unknown table'],
    [undefined, 'Unknown table'],
  ];
  for (const [input, expected] of cases) {
    it(`${JSON.stringify(input)} → ${expected}`, () => expect(toTableLabel(input)).toBe(expected));
  }
});
