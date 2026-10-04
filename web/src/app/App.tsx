import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App as AntApp, ConfigProvider } from 'antd';
import trTR from 'antd/locale/tr_TR';
import { useState, type ReactNode } from 'react';
import { AuthProvider } from 'react-oidc-context';
import { BrowserRouter } from 'react-router';
import { isApiError } from '@/shared/api/problem';
import { ErrorBoundary } from '@/shared/components/ErrorBoundary';
import { ApiErrorNotifier } from './ApiErrorNotifier';
import { AppRoutes } from './AppRoutes';
import { HttpBridge } from './HttpBridge';
import { userManager } from './auth/oidc';
import { ThemeModeProvider } from './theme/ThemeModeProvider';
import { useThemeMode } from './theme/themeModeContext';
import { darkTheme, lightTheme } from './theme/tokens';

const createQueryClient = () =>
  new QueryClient({
    queryCache: new QueryCache(),
    mutationCache: new MutationCache(),
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: false,
        // Retry only transient failures (network, 5xx), never business errors.
        retry: (failureCount, error) =>
          failureCount < 2 && (!isApiError(error) || error.status === 0 || error.status >= 500),
      },
    },
  });

const ThemedApp = ({ children }: { children: ReactNode }) => {
  const { resolved } = useThemeMode();
  return (
    <ConfigProvider locale={trTR} theme={resolved === 'dark' ? darkTheme : lightTheme}>
      <AntApp>{children}</AntApp>
    </ConfigProvider>
  );
};

export const App = () => {
  const [queryClient] = useState(createQueryClient);
  return (
    <ThemeModeProvider>
      <ThemedApp>
        <QueryClientProvider client={queryClient}>
          <AuthProvider userManager={userManager}>
            <BrowserRouter>
              <HttpBridge />
              <ApiErrorNotifier />
              <ErrorBoundary>
                <AppRoutes />
              </ErrorBoundary>
            </BrowserRouter>
          </AuthProvider>
        </QueryClientProvider>
      </ThemedApp>
    </ThemeModeProvider>
  );
};
