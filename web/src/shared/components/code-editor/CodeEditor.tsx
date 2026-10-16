import Editor, { type OnMount } from '@monaco-editor/react';
import { theme } from 'antd';
import { useEffect, useState } from 'react';
import { useThemeMode } from '@/app/theme/themeModeContext';
import { monaco } from './monaco';

type MonacoEditor = Parameters<OnMount>[0];

export interface CodeEditorProps {
  language: 'html' | 'css';
  value: string;
  onChange: (value: string) => void;
  /** Accessible name of the editor. */
  label: string;
  height?: number | string;
  readOnly?: boolean;
  /** A template error to mark: 1-based line and the message shown on hover. */
  error?: { line: number; message: string };
}

/** Monaco for Liquid templates (D36): HTML or CSS with an optional error marker, following the light or dark theme. */
export const CodeEditor = ({ language, value, onChange, label, height = 420, readOnly, error }: CodeEditorProps) => {
  const { token } = theme.useToken();
  const { resolved } = useThemeMode();
  // State, not a ref: the markers wait until Monaco has mounted
  const [editor, setEditor] = useState<MonacoEditor | null>(null);

  useEffect(() => {
    const model = editor?.getModel();
    if (!model) {
      return;
    }

    const line = error ? Math.min(Math.max(error.line, 1), model.getLineCount()) : 0;
    monaco.editor.setModelMarkers(
      model,
      'template',
      error
        ? [
            {
              severity: monaco.MarkerSeverity.Error,
              message: error.message,
              startLineNumber: line,
              endLineNumber: line,
              startColumn: 1,
              endColumn: model.getLineMaxColumn(line),
            },
          ]
        : [],
    );
  }, [editor, error, value]);

  return (
    <div style={{ border: `1px solid ${token.colorBorder}`, borderRadius: token.borderRadius, overflow: 'hidden' }}>
      <Editor
        height={height}
        language={language}
        value={value}
        theme={resolved === 'dark' ? 'vs-dark' : 'light'}
        onChange={(next) => onChange(next ?? '')}
        onMount={setEditor}
        options={{
          ariaLabel: label,
          readOnly,
          minimap: { enabled: false },
          wordWrap: 'on',
          fontSize: 13,
          tabSize: 2,
          scrollBeyondLastLine: false,
          automaticLayout: true,
        }}
      />
    </div>
  );
};
