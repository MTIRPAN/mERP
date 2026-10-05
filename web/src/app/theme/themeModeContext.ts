import { createContext, useContext } from 'react';

export type ThemeMode = 'light' | 'dark' | 'system';
export type ResolvedTheme = 'light' | 'dark';

export interface ThemeModeValue {
  mode: ThemeMode;
  resolved: ResolvedTheme;
  setMode: (mode: ThemeMode) => void;
}

export const ThemeModeContext = createContext<ThemeModeValue | undefined>(undefined);

export const useThemeMode = (): ThemeModeValue => {
  const value = useContext(ThemeModeContext);
  if (!value) {
    throw new Error('useThemeMode must be used inside ThemeModeProvider');
  }

  return value;
};
