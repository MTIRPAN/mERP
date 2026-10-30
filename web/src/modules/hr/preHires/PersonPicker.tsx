import { Input, List, Modal, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useListEmployees } from '@/shared/api/generated/endpoints/hr/hr';
import type { EmployeeListItem, PreHireItem } from '@/shared/api/generated/model';

interface PersonPickerProps {
  preHire: PreHireItem;
  /** The pre-hire for the chosen person: rehired, or given the new assignment when they work in the company. */
  onPick: (preHire: PreHireItem) => void;
  onClose: () => void;
}

/**
 * The person a pre-hire is for, when the e-mail did not find them (D54): HR searches by TCKN or name, as the hire form
 * refuses a TCKN already registered.
 */
export const PersonPicker = ({ preHire, onPick, onClose }: PersonPickerProps) => {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const rows = useListEmployees({ search, pageSize: 20 }, { query: { enabled: search.length >= 2 } });

  // A row per employment: one entry per person, with their open employment in the pre-hire's company
  const people = useMemo(() => {
    const byPerson = new Map<string, { row: EmployeeListItem; openEmploymentId: string | null }>();
    for (const row of rows.data?.items ?? []) {
      const open = row.companyId === preHire.companyId && row.status !== 'Terminated' ? row.employmentId : null;
      const known = byPerson.get(row.id);
      byPerson.set(row.id, { row, openEmploymentId: known?.openEmploymentId ?? open });
    }

    return [...byPerson.values()];
  }, [rows.data, preHire.companyId]);

  return (
    <Modal title={t('web.hr.pre_hires.pick_person')} open footer={null} onCancel={onClose} destroyOnHidden>
      <Typography.Paragraph type="secondary">{t('web.hr.pre_hires.pick_person_hint')}</Typography.Paragraph>
      <Input.Search
        aria-label={t('web.hr.pre_hires.search_person')}
        placeholder={t('web.hr.pre_hires.search_person')}
        allowClear
        enterButton
        onSearch={(value) => setSearch(value.trim())}
      />
      <List
        style={{ marginTop: 12 }}
        loading={rows.isFetching}
        dataSource={search.length >= 2 ? people : []}
        locale={{ emptyText: t('web.hr.pre_hires.no_person') }}
        renderItem={({ row, openEmploymentId }) => (
          <List.Item
            style={{ cursor: 'pointer' }}
            onClick={() =>
              onPick({ ...preHire, existingEmployeeId: row.id, existingEmployeeName: row.fullName, openEmploymentId })
            }
          >
            <List.Item.Meta
              title={row.fullName}
              description={`${row.tckn} · ${row.companyName} · ${t(`web.hr.employment_status.${row.status}`)}`}
            />
          </List.Item>
        )}
      />
    </Modal>
  );
};
