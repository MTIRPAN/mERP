import dayjs from 'dayjs';
import 'dayjs/locale/tr';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from '@/app/App';
import { initI18n } from '@/app/i18n/i18n';
import '@/app/theme/global.css';

dayjs.locale('tr');

const root = document.getElementById('root');
if (!root) {
  throw new Error('Root element #root is missing');
}

// Texts are loaded from the API before the first render (D11).
void initI18n().then(() => {
  createRoot(root).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
});
