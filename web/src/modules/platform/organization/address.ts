import type { AddressDto, AddressInput } from '@/shared/api/generated/model';

/** Form values of an address; Turkish addresses pick the province and district from the reference data. */
export interface AddressValues {
  line1?: string;
  line2?: string;
  provinceCode?: number;
  districtCode?: number;
  postalCode?: string;
}

const blank = (value?: string | null) => (value?.trim() ? value.trim() : null);

/** Request body of an address; an untouched address section is sent as null. */
export const toAddressInput = (values?: AddressValues): AddressInput | null =>
  values && (values.line1?.trim() || values.provinceCode)
    ? {
        line1: blank(values.line1),
        line2: blank(values.line2),
        provinceCode: values.provinceCode ?? null,
        districtCode: values.districtCode ?? null,
        postalCode: blank(values.postalCode),
        countryCode: 'TR',
      }
    : null;

export const fromAddressDto = (address?: AddressDto | null): AddressValues | undefined =>
  address
    ? {
        line1: address.line1,
        line2: address.line2 ?? undefined,
        provinceCode: address.provinceCode ?? undefined,
        districtCode: address.districtCode ?? undefined,
        postalCode: address.postalCode ?? undefined,
      }
    : undefined;
