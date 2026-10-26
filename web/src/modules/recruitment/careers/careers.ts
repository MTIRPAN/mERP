import type { CareerOpeningItem } from '@/shared/api/generated/model';

/** Whether an opening matches the text typed: its title, company or place. */
export const matches = (opening: CareerOpeningItem, search: string) => {
  const text = search.trim().toLocaleLowerCase('tr-TR');
  return (
    text === '' ||
    [opening.title, opening.companyName, opening.branchName ?? ''].some((v) =>
      v.toLocaleLowerCase('tr-TR').includes(text),
    )
  );
};
