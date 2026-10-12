import { describe, expect, it } from 'vitest';
import { fieldPath } from './formErrors';

describe('fieldPath', () => {
  it('splits dotted paths and list indexes', () => {
    expect(fieldPath('address.city')).toEqual(['address', 'city']);
    expect(fieldPath('steps[0].approver.parameters.userId')).toEqual(['steps', 0, 'approver', 'parameters', 'userId']);
    expect(fieldPath('items[2][1]')).toEqual(['items', 2, 1]);
    expect(fieldPath('customFields.shoe_size')).toEqual(['customFields', 'shoe_size']);
  });
});
