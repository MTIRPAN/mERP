import { EnterOutlined, SearchOutlined, StarFilled } from '@ant-design/icons';
import { Empty, Flex, Input, Modal, Typography, theme } from 'antd';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { useAiFeature } from '@/shared/ai/useAiFeature';
import { askAssistant } from '@/shared/api/generated/endpoints/platform/platform';
import type { AssistantResponse } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { AssistantResult } from './AssistantResult';
import { filterMenuEntries, menuIcon, type MenuEntry } from './menuItems';

/** Questions shorter than this only search the menu. */
const ASSISTANT_MIN_LENGTH = 3;

type PaletteOption = { kind: 'page'; entry: MenuEntry } | { kind: 'assistant' };

interface CommandPaletteProps {
  open: boolean;
  onClose: () => void;
  entries: MenuEntry[];
}

/**
 * Ctrl/⌘+K quick navigation. With the assistant on (PLAN A12, D65) the last option asks the question in natural
 * language: the answer's rows show in the palette, a page it picks opens at once.
 */
export const CommandPalette = ({ open, onClose, entries }: CommandPaletteProps) => {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { token } = theme.useToken();
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const [asking, setAsking] = useState(false);
  const [answer, setAnswer] = useState<AssistantResponse>();
  const assistant = useAiFeature('platform.assistant');
  const results = useMemo(() => filterMenuEntries(entries, query, i18n.language), [entries, query, i18n.language]);
  const question = query.trim();
  const options = useMemo<PaletteOption[]>(
    () => [
      ...results.map((entry) => ({ kind: 'page' as const, entry })),
      ...(assistant && question.length >= ASSISTANT_MIN_LENGTH ? [{ kind: 'assistant' as const }] : []),
    ],
    [results, assistant, question],
  );
  const showingAnswer = asking || !!answer;

  useEffect(() => {
    if (open) {
      setQuery('');
      setActive(0);
      setAnswer(undefined);
    }
  }, [open]);

  const openPath = (path: string) => {
    void navigate(path);
    onClose();
  };

  const ask = async () => {
    setAsking(true);
    try {
      const response = await askAssistant({ text: question });
      if (response.kind === 'Page' && response.path) {
        openPath(response.path);
      } else {
        setAnswer(response);
      }
    } catch (error) {
      if (!isApiError(error)) {
        throw error;
      }
    } finally {
      setAsking(false);
    }
  };

  const choose = (option: PaletteOption | undefined) => {
    if (option?.kind === 'page') {
      openPath(option.entry.path);
    } else if (option?.kind === 'assistant' && !asking) {
      void ask();
    }
  };

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActive((index) => Math.min(index + 1, options.length - 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActive((index) => Math.max(index - 1, 0));
    } else if (event.key === 'Enter') {
      choose(options[active]);
    }
  };

  return (
    <Modal
      open={open}
      onCancel={onClose}
      footer={null}
      closable={false}
      width={560}
      title={null}
      styles={{ body: { padding: 0 } }}
    >
      <div style={{ padding: 12, borderBottom: `1px solid ${token.colorBorderSecondary}` }}>
        <Input
          autoFocus
          size="large"
          variant="borderless"
          prefix={<SearchOutlined style={{ color: token.colorTextTertiary }} />}
          placeholder={t(assistant ? 'web.shell.assistant.placeholder' : 'web.shell.command.placeholder')}
          aria-label={t('web.shell.command.title')}
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setActive(0);
            setAnswer(undefined);
          }}
          onKeyDown={onKeyDown}
        />
      </div>
      {showingAnswer ? (
        <div style={{ maxHeight: 420, overflowY: 'auto' }}>
          <AssistantResult result={answer} loading={asking} onOpen={openPath} />
        </div>
      ) : (
        <div role="listbox" style={{ maxHeight: 360, overflowY: 'auto', padding: 8 }}>
          {options.length === 0 ? (
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.shell.command.empty')} />
          ) : (
            options.map((option, index) =>
              option.kind === 'assistant' ? (
                <Flex
                  key="assistant"
                  role="option"
                  aria-selected={index === active}
                  align="center"
                  gap={12}
                  onMouseEnter={() => setActive(index)}
                  onClick={() => choose(option)}
                  style={{
                    padding: '10px 12px',
                    borderRadius: token.borderRadius,
                    cursor: 'pointer',
                    background: index === active ? token.controlItemBgActive : undefined,
                  }}
                >
                  <StarFilled className="merp-gradient-text" />
                  <Typography.Text style={{ flex: 1 }}>{t('web.shell.assistant.ask', { question })}</Typography.Text>
                  {index === active && <EnterOutlined style={{ color: token.colorTextTertiary }} />}
                </Flex>
              ) : (
                <Flex
                  key={option.entry.key}
                  role="option"
                  aria-selected={index === active}
                  align="center"
                  gap={12}
                  onMouseEnter={() => setActive(index)}
                  onClick={() => choose(option)}
                  style={{
                    padding: '10px 12px',
                    borderRadius: token.borderRadius,
                    cursor: 'pointer',
                    background: index === active ? token.controlItemBgActive : undefined,
                  }}
                >
                  <span style={{ color: token.colorPrimary }}>{menuIcon(option.entry.icon)}</span>
                  <Flex vertical style={{ flex: 1 }}>
                    <Typography.Text>{option.entry.label}</Typography.Text>
                    {option.entry.section && (
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                        {option.entry.section}
                      </Typography.Text>
                    )}
                  </Flex>
                  {index === active && <EnterOutlined style={{ color: token.colorTextTertiary }} />}
                </Flex>
              ),
            )
          )}
        </div>
      )}
    </Modal>
  );
};
