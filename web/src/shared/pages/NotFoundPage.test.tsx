import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { NotFoundPage } from './NotFoundPage';

describe('NotFoundPage (smoke)', () => {
  it('shows texts from the localization bundle', () => {
    renderWithProviders(<NotFoundPage />, {
      'web.errors.not_found.title': 'Sayfa bulunamadı',
      'web.errors.not_found.description': 'Aradığınız sayfa taşınmış olabilir.',
      'web.common.back_home': 'Ana sayfaya dön',
    });

    expect(screen.getByText('Sayfa bulunamadı')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ana sayfaya dön' })).toBeInTheDocument();
  });
});
