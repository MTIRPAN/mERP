import { EnterOutlined, SearchOutlined } from '@ant-design/icons';
import { Empty, Flex, Input, Modal, Typography, theme } from 'antd';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { filterMenuEntries, menuIcon, type MenuEntry } from './menuItems';

interface CommandPaletteProps {
  open: boolean;
  onClose: () => void;
  entries: MenuEntry[];
}

/**
 * Ctrl/⌘+K quick navigation. It is also the entry point the planned assistant will extend with natural
 * language commands.
 */
export const CommandPalette = ({ open, onClose, entries }: CommandPaletteProps) => {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { token } = theme.useToken();
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const results = useMemo(() => filterMenuEntries(entries, query, i18n.language), [entries, query, i18n.language]);

  useEffect(() => {
    if (open) {
      setQuery('');
      setActive(0);
    }
  }, [open]);

  const go = (entry: MenuEntry | undefined) => {
    if (entry) {
      void navigate(entry.path);
      onClose();
    }
  };

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActive((index) => Math.min(index + 1, results.length - 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActive((index) => Math.max(index - 1, 0));
    } else if (event.key === 'Enter') {
      go(results[active]);
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
          placeholder={t('web.shell.command.placeholder')}
          aria-label={t('web.shell.command.title')}
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setActive(0);
          }}
          onKeyDown={onKeyDown}
        />
      </div>
      <div role="listbox" style={{ maxHeight: 360, overflowY: 'auto', padding: 8 }}>
        {results.length === 0 ? (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.shell.command.empty')} />
        ) : (
          results.map((entry, index) => (
            <Flex
              key={entry.key}
              role="option"
              aria-selected={index === active}
              align="center"
              gap={12}
              onMouseEnter={() => setActive(index)}
              onClick={() => go(entry)}
              style={{
                padding: '10px 12px',
                borderRadius: token.borderRadius,
                cursor: 'pointer',
                background: index === active ? token.controlItemBgActive : undefined,
              }}
            >
              <span style={{ color: token.colorPrimary }}>{menuIcon(entry.icon)}</span>
              <Flex vertical style={{ flex: 1 }}>
                <Typography.Text>{entry.label}</Typography.Text>
                {entry.section && (
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {entry.section}
                  </Typography.Text>
                )}
              </Flex>
              {index === active && <EnterOutlined style={{ color: token.colorTextTertiary }} />}
            </Flex>
          ))
        )}
      </div>
    </Modal>
  );
};
