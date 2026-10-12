import { describe, expect, it } from 'vitest';
import { delegationState } from './delegations';

describe('delegationState', () => {
  const delegation = { from: '2026-10-10', to: '2026-10-15', isRevoked: false };

  it('counts both dates in', () => {
    expect(delegationState(delegation, '2026-10-09')).toBe('upcoming');
    expect(delegationState(delegation, '2026-10-10')).toBe('active');
    expect(delegationState(delegation, '2026-10-15')).toBe('active');
    expect(delegationState(delegation, '2026-10-16')).toBe('ended');
  });

  it('shows a revoked delegation as revoked', () => {
    expect(delegationState({ ...delegation, isRevoked: true }, '2026-10-12')).toBe('revoked');
  });
});
