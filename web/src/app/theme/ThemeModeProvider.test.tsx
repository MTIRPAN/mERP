import { act, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { THEME_STORAGE_KEY, ThemeModeProvider } from './ThemeModeProvider';
import { useThemeMode } from './themeModeContext';

const Probe = () => {
  const { mode, resolved, setMode } = useThemeMode();
  return (
    <button type="button" onClick={() => setMode('dark')}>
      {mode}:{resolved}
    </button>
  );
};

describe('ThemeModeProvider', () => {
  it('follows the system by default and remembers an explicit choice', () => {
    render(
      <ThemeModeProvider>
        <Probe />
      </ThemeModeProvider>,
    );

    expect(screen.getByRole('button')).toHaveTextContent('system:light');

    act(() => screen.getByRole('button').click());

    expect(screen.getByRole('button')).toHaveTextContent('dark:dark');
    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
  });
});
