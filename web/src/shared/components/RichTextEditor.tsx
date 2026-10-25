import {
  BoldOutlined,
  ItalicOutlined,
  LinkOutlined,
  OrderedListOutlined,
  StrikethroughOutlined,
  UnderlineOutlined,
  UnorderedListOutlined,
} from '@ant-design/icons';
import { EditorContent, useEditor, useEditorState, type Editor } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import { Button, Divider, Flex, Tooltip, theme } from 'antd';
import { useEffect, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

interface RichTextEditorProps {
  /** HTML; the server keeps only the formatting this editor produces (D49). */
  value?: string | null;
  onChange?: (html: string | null) => void;
  id?: string;
  'aria-label'?: string;
  minHeight?: number;
}

const ToolbarButton = ({
  title,
  icon,
  active,
  onClick,
}: {
  title: string;
  icon: ReactNode;
  active?: boolean;
  onClick: () => void;
}) => (
  <Tooltip title={title}>
    <Button
      size="small"
      type={active ? 'primary' : 'text'}
      icon={icon}
      aria-label={title}
      aria-pressed={active}
      onMouseDown={(event) => event.preventDefault()}
      onClick={onClick}
    />
  </Tooltip>
);

const Toolbar = ({ editor }: { editor: Editor }) => {
  const { t } = useTranslation();
  const state = useEditorState({
    editor,
    selector: ({ editor: e }) => ({
      bold: e.isActive('bold'),
      italic: e.isActive('italic'),
      underline: e.isActive('underline'),
      strike: e.isActive('strike'),
      heading: e.isActive('heading', { level: 3 }),
      bullet: e.isActive('bulletList'),
      ordered: e.isActive('orderedList'),
      link: e.isActive('link'),
    }),
  });

  const setLink = () => {
    const previous = editor.getAttributes('link').href as string | undefined;
    const url = window.prompt(t('web.rich_text.link_prompt'), previous ?? 'https://');
    if (url === null) {
      return;
    }

    if (url.trim() === '' || url.trim() === 'https://') {
      editor.chain().focus().extendMarkRange('link').unsetLink().run();
      return;
    }

    editor.chain().focus().extendMarkRange('link').setLink({ href: url.trim() }).run();
  };

  return (
    <Flex gap={2} wrap align="center" style={{ padding: 4 }}>
      <ToolbarButton
        title={t('web.rich_text.heading')}
        icon={<strong>H</strong>}
        active={state.heading}
        onClick={() => editor.chain().focus().toggleHeading({ level: 3 }).run()}
      />
      <Divider orientation="vertical" />
      <ToolbarButton
        title={t('web.rich_text.bold')}
        icon={<BoldOutlined />}
        active={state.bold}
        onClick={() => editor.chain().focus().toggleBold().run()}
      />
      <ToolbarButton
        title={t('web.rich_text.italic')}
        icon={<ItalicOutlined />}
        active={state.italic}
        onClick={() => editor.chain().focus().toggleItalic().run()}
      />
      <ToolbarButton
        title={t('web.rich_text.underline')}
        icon={<UnderlineOutlined />}
        active={state.underline}
        onClick={() => editor.chain().focus().toggleUnderline().run()}
      />
      <ToolbarButton
        title={t('web.rich_text.strike')}
        icon={<StrikethroughOutlined />}
        active={state.strike}
        onClick={() => editor.chain().focus().toggleStrike().run()}
      />
      <Divider orientation="vertical" />
      <ToolbarButton
        title={t('web.rich_text.bullet_list')}
        icon={<UnorderedListOutlined />}
        active={state.bullet}
        onClick={() => editor.chain().focus().toggleBulletList().run()}
      />
      <ToolbarButton
        title={t('web.rich_text.ordered_list')}
        icon={<OrderedListOutlined />}
        active={state.ordered}
        onClick={() => editor.chain().focus().toggleOrderedList().run()}
      />
      <ToolbarButton title={t('web.rich_text.link')} icon={<LinkOutlined />} active={state.link} onClick={setLink} />
    </Flex>
  );
};

/** An emptied editor gives an empty paragraph; the form keeps null for "no text". */
const toValue = (editor: Editor): string | null => (editor.isEmpty ? null : editor.getHTML());

/**
 * Rich text for form fields (D18, D49): TipTap with headings, bold, italic, underline, strike, lists and links, the
 * formatting the server keeps. Works as an antd form control (value / onChange with HTML).
 */
export const RichTextEditor = ({
  value,
  onChange,
  id,
  'aria-label': ariaLabel,
  minHeight = 160,
}: RichTextEditorProps) => {
  const { token } = theme.useToken();
  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: { levels: [2, 3, 4] },
        code: false,
        codeBlock: false,
        horizontalRule: false,
        link: { openOnClick: false, autolink: true, protocols: ['http', 'https', 'mailto'] },
      }),
    ],
    content: value ?? '',
    editorProps: {
      attributes: {
        ...(id ? { id } : {}),
        ...(ariaLabel ? { 'aria-label': ariaLabel } : {}),
        'aria-multiline': 'true',
        role: 'textbox',
        style: `min-height:${minHeight}px;padding:8px 11px;outline:none`,
      },
    },
    onUpdate: ({ editor: e }) => onChange?.(toValue(e)),
  });

  // A value set from outside (form reset, loaded record) replaces the content
  useEffect(() => {
    if (editor && (value ?? null) !== toValue(editor)) {
      editor.commands.setContent(value ?? '', { emitUpdate: false });
    }
  }, [editor, value]);

  return (
    <div
      className="merp-rich-text"
      style={{
        border: `1px solid ${token.colorBorder}`,
        borderRadius: token.borderRadius,
        background: token.colorBgContainer,
      }}
    >
      {editor && <Toolbar editor={editor} />}
      <Divider style={{ margin: 0 }} />
      <EditorContent editor={editor} />
    </div>
  );
};

/** Rich text the server sanitized (D49), shown as it will look to candidates. */
export const RichTextView = ({ html }: { html: string | null | undefined }) =>
  html ? <div className="merp-rich-text-view" dangerouslySetInnerHTML={{ __html: html }} /> : null;
