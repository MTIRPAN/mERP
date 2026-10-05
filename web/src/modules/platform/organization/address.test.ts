import { describe, expect, it } from 'vitest';
import { fromAddressDto, toAddressInput } from './address';
import { toActiveParam } from './filters';

describe('address', () => {
  it('sends an untouched address as null', () => {
    expect(toAddressInput(undefined)).toBeNull();
    expect(toAddressInput({ line1: '  ' })).toBeNull();
  });

  it('trims the text and keeps the codes of a Turkish address', () => {
    expect(toAddressInput({ line1: ' Atatürk Cad. 1 ', provinceCode: 34, districtCode: 1421, postalCode: '' })).toEqual(
      {
        line1: 'Atatürk Cad. 1',
        line2: null,
        provinceCode: 34,
        districtCode: 1421,
        postalCode: null,
        countryCode: 'TR',
      },
    );
  });

  it('round-trips the API address into form values', () => {
    expect(
      fromAddressDto({
        line1: 'OSB 3. Cad.',
        line2: null,
        provinceCode: 41,
        districtCode: 1338,
        city: 'Kocaeli',
        district: 'Gebze',
        postalCode: null,
        countryCode: 'TR',
      }),
    ).toEqual({ line1: 'OSB 3. Cad.', line2: undefined, provinceCode: 41, districtCode: 1338, postalCode: undefined });
  });

  it('maps the active filter to the query parameter', () => {
    expect(toActiveParam('active')).toBe(true);
    expect(toActiveParam('inactive')).toBe(false);
    expect(toActiveParam('all')).toBeUndefined();
  });
});
