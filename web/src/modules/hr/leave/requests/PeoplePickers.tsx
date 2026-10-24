import { Select } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useListEmployees, useSearchMyColleagues } from '@/shared/api/generated/endpoints/hr/hr';
import { useSettled } from './leaveRequests';

const SEARCH_SIZE = 20;

interface PickerProps<T> {
  value?: T;
  onChange?: (value: T) => void;
  disabled?: boolean;
}

/** An employment of a person in the caller's scope, searched by name, registry number or TCKN (HR). */
export const EmploymentPicker = ({ value, onChange, disabled }: PickerProps<string>) => {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const settled = useSettled(search, 250);
  const employees = useListEmployees({ search: settled || undefined, pageSize: SEARCH_SIZE });
  return (
    <Select
      showSearch={{ filterOption: false, onSearch: setSearch }}
      disabled={disabled}
      placeholder={t('web.hr.leave_requests.fields.employee_placeholder')}
      loading={employees.isFetching}
      value={value}
      onChange={onChange}
      options={(employees.data?.items ?? []).map((e) => ({
        value: e.employmentId,
        label: `${e.fullName} · ${e.companyName} · ${e.registryNumber}`,
      }))}
    />
  );
};

/** Employments of people in the caller's scope, for a bulk leave (HR). */
export const EmploymentsPicker = ({ value, onChange, companyId }: PickerProps<string[]> & { companyId?: string }) => {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const settled = useSettled(search, 250);
  const employees = useListEmployees(
    { companyId, search: settled || undefined, pageSize: SEARCH_SIZE },
    { query: { enabled: !!companyId } },
  );
  const [known, setKnown] = useState<Record<string, string>>({});
  const options = (employees.data?.items ?? []).map((e) => ({
    value: e.employmentId,
    label: `${e.fullName} · ${e.registryNumber}`,
  }));
  return (
    <Select
      mode="multiple"
      showSearch={{ filterOption: false, onSearch: setSearch }}
      placeholder={t('web.hr.leave_requests.fields.employee_placeholder')}
      loading={employees.isFetching}
      value={value}
      onChange={(ids: string[]) => {
        setKnown((current) => ({ ...current, ...Object.fromEntries(options.map((o) => [o.value, o.label])) }));
        onChange?.(ids);
      }}
      options={[
        ...options,
        ...(value ?? [])
          .filter((id) => !options.some((o) => o.value === id))
          .map((id) => ({ value: id, label: known[id] ?? id })),
      ]}
    />
  );
};

/** A person who stands in during the leave: colleagues for the employee, people in scope for HR. */
export const SubstitutePicker = ({
  value,
  onChange,
  self,
  known,
}: PickerProps<string | null> & { self: boolean; known?: { value: string; label: string } }) => {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const settled = useSettled(search, 250);
  const colleagues = useSearchMyColleagues({ search: settled || undefined }, { query: { enabled: self } });
  const employees = useListEmployees(
    { search: settled || undefined, pageSize: SEARCH_SIZE },
    { query: { enabled: !self } },
  );
  const options = self
    ? (colleagues.data ?? []).map((c) => ({ value: c.employeeId, label: c.fullName }))
    : (employees.data?.items ?? []).map((e) => ({ value: e.id, label: `${e.fullName} · ${e.companyName}` }));
  return (
    <Select
      allowClear
      showSearch={{ filterOption: false, onSearch: setSearch }}
      placeholder={t('web.hr.leave_requests.fields.substitute_placeholder')}
      loading={self ? colleagues.isFetching : employees.isFetching}
      value={value ?? undefined}
      onChange={(id?: string) => onChange?.(id ?? null)}
      options={known && !options.some((o) => o.value === known.value) ? [...options, known] : options}
    />
  );
};
