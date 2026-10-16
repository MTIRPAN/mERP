import { loader } from '@monaco-editor/react';
import * as monaco from 'monaco-editor';
import EditorWorker from 'monaco-editor/editor/editor.worker?worker';
import CssWorker from 'monaco-editor/language/css/css.worker?worker';
import HtmlWorker from 'monaco-editor/language/html/html.worker?worker';

/**
 * Monaco from the application's own bundle, not from a CDN (D36): company networks often block outside CDNs. Only the
 * HTML and CSS workers are wired; every other language falls back to the base editor worker. This module is imported
 * by the code editor alone, so Monaco loads with the template pages and not at start-up.
 */
self.MonacoEnvironment = {
  getWorker: (_workerId: string, label: string) => {
    if (label === 'css') {
      return new CssWorker();
    }

    if (label === 'html') {
      return new HtmlWorker();
    }

    return new EditorWorker();
  },
};

loader.config({ monaco });

export { monaco };
