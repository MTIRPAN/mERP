import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { RichTextEditor, RichTextView } from './RichTextEditor';

describe('RichTextEditor', () => {
  it('shows the value with its formatting and a toolbar', async () => {
    renderWithProviders(<RichTextEditor aria-label="İş tanımı" value="<p>Satış <strong>ekibi</strong></p>" />, {
      'web.rich_text.bold': 'Kalın',
      'web.rich_text.bullet_list': 'Madde işaretli liste',
    });

    const editor = await screen.findByRole('textbox', { name: 'İş tanımı' });
    await waitFor(() => expect(editor.querySelector('strong')?.textContent).toBe('ekibi'));
    expect(screen.getByRole('button', { name: 'Kalın' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Madde işaretli liste' })).toBeInTheDocument();
  });

  it('replaces the content when the value changes from outside', async () => {
    const { rerender } = renderWithProviders(<RichTextEditor aria-label="Metin" value="<p>ilk</p>" />);
    await screen.findByText('ilk');

    rerender(<RichTextEditor aria-label="Metin" value="<p>ikinci</p>" />);

    expect(await screen.findByText('ikinci')).toBeInTheDocument();
  });
});

describe('RichTextView', () => {
  it('renders sanitized HTML and nothing for an empty text', () => {
    const { container } = renderWithProviders(<RichTextView html="<p><em>Uzaktan</em> çalışma</p>" />);
    expect(container.querySelector('em')?.textContent).toBe('Uzaktan');

    const empty = renderWithProviders(<RichTextView html={null} />);
    expect(empty.container.querySelector('.merp-rich-text-view')).toBeNull();
  });
});
