import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeContext } from '@/app/me/meContext';
import type { AskAssistantRequest, AssistantResponse, MeResponse } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { CommandPalette } from './CommandPalette';
import type { MenuEntry } from './menuItems';

const ask = vi.fn<(request: AskAssistantRequest) => Promise<AssistantResponse>>();

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  askAssistant: (request: AskAssistantRequest) => ask(request),
}));

const texts: Record<string, string> = {
  'web.shell.command.title': 'Hızlı geçiş',
  'web.shell.command.empty': 'Eşleşen sayfa yok',
  'web.shell.assistant.ask': 'Asistana sor: {question}',
  'web.shell.assistant.total': '{count} kayıt',
  'web.shell.assistant.see_all': 'Tümünü gör',
  'web.shell.assistant.not_understood': 'Bu soruyu yanıtlayamadım.',
  'web.shell.assistant.notice': 'Yapay zeka önerisi.',
};

const entries: MenuEntry[] = [{ key: 'hr-my-leave', path: '/hr/me/leave', label: 'İzinlerim', icon: null }];

const me = (enabled: boolean) =>
  ({ ai: { enabled, features: enabled ? ['platform.assistant'] : [] } }) as unknown as MeResponse;

const response = (overrides: Partial<AssistantResponse>): AssistantResponse => ({
  kind: 'Answer',
  title: 'Onay bekleyen izin taleplerim',
  message: null,
  path: null,
  answer: null,
  model: 'model',
  promptVersion: 'v1',
  ...overrides,
});

const render = (enabled = true) =>
  renderWithProviders(
    <MeContext.Provider value={me(enabled)}>
      <Routes>
        <Route path="/" element={<CommandPalette open onClose={vi.fn()} entries={entries} />} />
        <Route path="/hr/me/leave" element={<p>leave page</p>} />
        <Route path="/access/users" element={<p>users page</p>} />
      </Routes>
    </MeContext.Provider>,
    texts,
  );

const typeQuestion = async (text: string) => {
  await userEvent.type(screen.getByRole('textbox', { name: 'Hızlı geçiş' }), text);
};

describe('CommandPalette', () => {
  beforeEach(() => ask.mockReset());

  it('asks the assistant and shows the rows of the answer', async () => {
    ask.mockResolvedValue(
      response({
        answer: {
          total: 1,
          items: [
            {
              title: 'Yıllık izin · IZN-2026-000012',
              detail: '2 Şubat 2026 · Onay bekliyor',
              link: '/hr/me/leave?request=r1',
            },
          ],
          link: '/hr/me/leave',
        },
      }),
    );
    render();

    await typeQuestion('onay bekleyen izinlerim');
    await userEvent.click(screen.getByText('Asistana sor: onay bekleyen izinlerim'));

    expect(await screen.findByText('Yıllık izin · IZN-2026-000012')).toBeInTheDocument();
    expect(screen.getByText('1 kayıt')).toBeInTheDocument();
    expect(screen.getByText('Yapay zeka önerisi.')).toBeInTheDocument();
    expect(ask).toHaveBeenCalledWith({ text: 'onay bekleyen izinlerim' });

    await userEvent.click(screen.getByRole('button', { name: 'Tümünü gör' }));
    expect(await screen.findByText('leave page')).toBeInTheDocument();
  });

  it('opens the page the assistant picks and says when it cannot answer', async () => {
    ask
      .mockResolvedValueOnce(response({ kind: 'None' }))
      .mockResolvedValueOnce(response({ kind: 'Page', path: '/access/users' }));
    render();

    await typeQuestion('maaşımı artır');
    await userEvent.keyboard('{Enter}');
    expect(await screen.findByText('Bu soruyu yanıtlayamadım.')).toBeInTheDocument();

    await userEvent.clear(screen.getByRole('textbox', { name: 'Hızlı geçiş' }));
    await typeQuestion('kullanıcılar');
    await userEvent.keyboard('{Enter}');
    await waitFor(() => expect(screen.getByText('users page')).toBeInTheDocument());
  });

  it('only searches the menu when the assistant is off', async () => {
    render(false);

    await typeQuestion('izin');
    expect(screen.getByText('İzinlerim')).toBeInTheDocument();
    expect(screen.queryByText(/Asistana sor/)).not.toBeInTheDocument();
  });
});
