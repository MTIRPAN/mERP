import { render } from '@testing-library/react';
import { App as AntApp, ConfigProvider } from 'antd';
import i18next from 'i18next';
import type { ReactElement } from 'react';
import { I18nextProvider, initReactI18next } from 'react-i18next';
import { MemoryRouter } from 'react-router';

/** i18n instance with the given texts and the production interpolation settings. */
export const createTestI18n = (texts: Record<string, string>) => {
  const instance = i18next.createInstance();
  void instance.use(initReactI18next).init({
    lng: 'tr-TR',
    resources: { 'tr-TR': { translation: texts } },
    keySeparator: false,
    nsSeparator: false,
    interpolation: { escapeValue: false, prefix: '{', suffix: '}' },
  });
  return instance;
};

export const renderWithProviders = (ui: ReactElement, texts: Record<string, string> = {}, route = '/') =>
  render(
    <I18nextProvider i18n={createTestI18n(texts)}>
      <ConfigProvider>
        <AntApp>
          <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
        </AntApp>
      </ConfigProvider>
    </I18nextProvider>,
  );
