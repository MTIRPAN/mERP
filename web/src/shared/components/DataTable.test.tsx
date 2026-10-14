import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { DataTable } from './DataTable';

interface Row {
  id: string;
  code: string;
}

describe('DataTable export', () => {
  it('exports with the filter and the visible columns in the table order', async () => {
    const onExport = vi.fn(() => Promise.resolve());
    renderWithProviders(
      <QueryClientProvider client={new QueryClient()}>
        <DataTable<Row>
          queryKey={['rows']}
          fetchPage={() => Promise.resolve({ items: [{ id: '1', code: 'A1' }], page: 1, pageSize: 20, totalCount: 1 })}
          columns={[
            { key: 'code', dataIndex: 'code', title: 'Kod' },
            { key: 'note', title: 'Not', hiddenByDefault: true },
            { key: 'name', title: 'Ad' },
          ]}
          rowKey="id"
          filter={{ search: 'gebze' }}
          onExport={onExport}
        />
      </QueryClientProvider>,
      { 'web.table.export': "Excel'e aktar" },
    );

    await userEvent.click(await screen.findByRole('button', { name: /Excel'e aktar/ }));

    await waitFor(() =>
      expect(onExport).toHaveBeenCalledWith({
        sort: undefined,
        filter: { search: 'gebze' },
        columns: ['code', 'name'],
      }),
    );
  });
});
