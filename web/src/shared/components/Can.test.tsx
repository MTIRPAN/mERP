import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PermissionProvider } from '@/shared/auth/PermissionProvider';
import { renderWithProviders } from '@/test/render';
import { Can } from './Can';

describe('Can', () => {
  it('renders children only with the permission', () => {
    renderWithProviders(
      <PermissionProvider permissions={['platform.system.read']}>
        <Can permission="platform.system.read">allowed</Can>
        <Can permission="platform.jobs.view" fallback={<span>denied</span>}>
          hidden
        </Can>
      </PermissionProvider>,
    );

    expect(screen.getByText('allowed')).toBeInTheDocument();
    expect(screen.getByText('denied')).toBeInTheDocument();
    expect(screen.queryByText('hidden')).not.toBeInTheDocument();
  });
});
