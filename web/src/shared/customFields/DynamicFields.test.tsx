import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Button, Form } from 'antd';
import { describe, expect, it, vi } from 'vitest';
import type { CustomFieldFormItem } from '@/shared/api/generated/model';
import { renderWithProviders } from '@/test/render';
import { DynamicFields } from './DynamicFields';
import { toApiValues } from './customFieldValues';

vi.mock('@/shared/api/generated/endpoints/platform/platform', () => ({
  useListUsers: () => ({ data: { items: [] }, isFetching: false }),
}));

const field = (
  code: string,
  label: string,
  type: CustomFieldFormItem['type'],
  extra: Partial<CustomFieldFormItem> = {},
): CustomFieldFormItem => ({
  code,
  label,
  type,
  required: false,
  options: [],
  maxLength: null,
  min: null,
  max: null,
  wholeNumber: false,
  group: null,
  ...extra,
});

const fields = [
  field('shoe_size', 'Ayakkabı numarası', 'Number', { required: true }),
  field('tier', 'Seviye', 'Select', {
    options: [
      { code: 'gold', label: 'Altın' },
      { code: 'silver', label: 'Gümüş' },
    ],
    group: 'Sınıflandırma',
  }),
];

const texts = { 'web.platform.fields.required': 'Bu alan zorunludur.', 'web.custom_fields.title': 'Özel alanlar' };

const Harness = ({ onSubmit }: { onSubmit: (values: Record<string, unknown>) => void }) => (
  <Form
    layout="vertical"
    onFinish={(values: { customFields?: Record<string, unknown> }) =>
      onSubmit(toApiValues(fields, values.customFields))
    }
  >
    <DynamicFields fields={fields} />
    <Button htmlType="submit">Gönder</Button>
  </Form>
);

describe('DynamicFields', () => {
  it('shows the fields in their groups and requires the required ones', async () => {
    const onSubmit = vi.fn();
    renderWithProviders(<Harness onSubmit={onSubmit} />, texts);

    expect(screen.getByText('Özel alanlar')).toBeInTheDocument();
    expect(screen.getByText('Sınıflandırma')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Gönder' }));

    expect(await screen.findByText('Bu alan zorunludur.')).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('sends the values by field code', async () => {
    const onSubmit = vi.fn();
    renderWithProviders(<Harness onSubmit={onSubmit} />, texts);

    await userEvent.type(screen.getByLabelText('Ayakkabı numarası'), '42');
    await userEvent.click(screen.getByLabelText('Seviye'));
    await userEvent.click(await screen.findByTitle('Altın'));
    await userEvent.click(screen.getByRole('button', { name: 'Gönder' }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledWith({ shoe_size: 42, tier: 'gold' }));
  });
});
