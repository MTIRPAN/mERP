import { Select } from 'antd';
import { useDeferredValue, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchApprovalUsers } from '@/shared/api/generated/endpoints/platform/platform';

type UserPickerProps =
  | {
      mode?: undefined;
      value?: string;
      onChange?: (value: string | undefined) => void;
      ariaLabel?: string;
      exclude?: string[];
    }
  | {
      mode: 'multiple';
      value?: string[];
      onChange?: (value: string[]) => void;
      ariaLabel?: string;
      exclude?: string[];
    };

/** Picks active users by name for delegations and approvers; chosen users keep their names (D34). */
export const UserPicker = (props: UserPickerProps) => {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const deferred = useDeferredValue(search);
  const selected = props.value === undefined ? [] : Array.isArray(props.value) ? props.value : [props.value];
  const users = useSearchApprovalUsers({
    search: deferred || undefined,
    ids: selected.length > 0 ? selected : undefined,
  });
  const options = (users.data ?? [])
    .filter((user) => !props.exclude?.includes(user.id))
    .map((user) => ({ value: user.id, label: user.name }));

  return (
    <Select
      showSearch={{ filterOption: false, onSearch: setSearch }}
      allowClear
      mode={props.mode}
      value={props.value}
      // Each variant passes its own value type through
      onChange={(value: string | string[] | undefined) =>
        (props.onChange as ((value: unknown) => void) | undefined)?.(value)
      }
      options={options}
      loading={users.isFetching}
      placeholder={t('web.approvals.user_search')}
      aria-label={props.ariaLabel}
      notFoundContent={t('web.approvals.no_users')}
      style={{ width: '100%' }}
    />
  );
};
