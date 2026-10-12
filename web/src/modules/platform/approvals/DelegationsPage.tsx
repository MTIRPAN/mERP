import { PlusOutlined, StopOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Card, DatePicker, Form, Popconfirm, Select, Table, Tag, type TableColumnsType } from 'antd';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import {
  getListMyDelegationsQueryKey,
  useCreateDelegation,
  useListApprovalDocumentTypes,
  useListMyDelegations,
  useRevokeDelegation,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { DelegationItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDate } from '@/shared/format/date';
import { refreshApprovals } from './approvalQueries';
import { delegationState, type DelegationState } from './delegations';
import { UserPicker } from './UserPicker';

const stateColors: Record<DelegationState, string> = {
  active: 'success',
  upcoming: 'processing',
  ended: 'default',
  revoked: 'warning',
};

interface DelegationValues {
  delegateUserId?: string;
  period?: [Dayjs, Dayjs];
  documentTypes?: string[];
}

/** "Vekaletlerim": approvals the caller handed to others and received (§2.6). */
export const DelegationsPage = () => {
  const { t, i18n } = useTranslation();
  const me = useMe();
  const queryClient = useQueryClient();
  const delegations = useListMyDelegations();
  const documentTypes = useListApprovalDocumentTypes();
  const create = useCreateDelegation();
  const revoke = useRevokeDelegation();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const today = dayjs().format('YYYY-MM-DD');

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: getListMyDelegationsQueryKey() });
    await refreshApprovals(queryClient);
  };

  const submit = async (values: DelegationValues) => {
    await create.mutateAsync({
      data: {
        delegateUserId: values.delegateUserId!,
        from: values.period![0].format('YYYY-MM-DD'),
        to: values.period![1].format('YYYY-MM-DD'),
        documentTypes: values.documentTypes ?? [],
      },
    });
    await refresh();
  };

  const columns: TableColumnsType<DelegationItem> = [
    {
      key: 'direction',
      title: t('web.approvals.delegations.direction'),
      render: (_, row) => (
        <Tag color={row.given ? 'blue' : 'purple'}>
          {t(row.given ? 'web.approvals.delegations.given' : 'web.approvals.delegations.received')}
        </Tag>
      ),
    },
    {
      key: 'person',
      title: t('web.approvals.delegations.person'),
      render: (_, row) => (row.given ? row.delegateName : row.principalName),
    },
    {
      key: 'period',
      title: t('web.approvals.delegations.period'),
      render: (_, row) => `${formatDate(row.from, i18n.language)} – ${formatDate(row.to, i18n.language)}`,
    },
    {
      key: 'types',
      title: t('web.approvals.delegations.document_types'),
      render: (_, row) =>
        row.documentTypeNames.length > 0 ? row.documentTypeNames.join(', ') : t('web.approvals.delegations.all_types'),
    },
    {
      key: 'state',
      title: t('web.platform.fields.status'),
      render: (_, row) => {
        const state = delegationState(row, today);
        return <Tag color={stateColors[state]}>{t(`web.approvals.delegations.states.${state}`)}</Tag>;
      },
    },
    {
      key: 'actions',
      width: 56,
      render: (_, row) =>
        row.given &&
        !row.isRevoked &&
        row.to >= today && (
          <Popconfirm
            title={t('web.approvals.delegations.revoke_confirm')}
            okText={t('web.approvals.delegations.revoke')}
            cancelText={t('web.common.cancel')}
            onConfirm={() => revoke.mutateAsync({ id: row.id }).then(refresh)}
          >
            <Button type="text" danger icon={<StopOutlined />} aria-label={t('web.approvals.delegations.revoke')} />
          </Popconfirm>
        ),
    },
  ];

  return (
    <>
      <PageHeader
        title={t('web.approvals.delegations.title')}
        subtitle={t('web.approvals.delegations.subtitle')}
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawerOpen(true)}>
            {t('web.approvals.delegations.new')}
          </Button>
        }
      />
      <Card>
        <Table<DelegationItem>
          rowKey="id"
          columns={columns}
          dataSource={delegations.data ?? []}
          loading={delegations.isFetching}
          pagination={false}
          locale={{ emptyText: t('web.approvals.delegations.empty') }}
          scroll={{ x: 'max-content' }}
        />
      </Card>
      <FormDrawer<DelegationValues>
        title={t('web.approvals.delegations.new')}
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        onSubmit={submit}
        fieldAliases={{ from: ['period'], to: ['period'] }}
      >
        <Form.Item
          name="delegateUserId"
          label={t('web.approvals.delegations.delegate')}
          rules={[{ required: true, message: t('web.platform.fields.required') }]}
        >
          <UserPicker ariaLabel={t('web.approvals.delegations.delegate')} exclude={me.user.id ? [me.user.id] : []} />
        </Form.Item>
        <Form.Item
          name="period"
          label={t('web.approvals.delegations.period')}
          rules={[{ required: true, message: t('web.platform.fields.required') }]}
        >
          <DatePicker.RangePicker format="DD.MM.YYYY" style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item
          name="documentTypes"
          label={t('web.approvals.delegations.document_types')}
          tooltip={t('web.approvals.delegations.document_types_hint')}
        >
          <Select
            mode="multiple"
            allowClear
            placeholder={t('web.approvals.delegations.all_types')}
            options={(documentTypes.data ?? []).map((type) => ({ value: type.key, label: type.name }))}
          />
        </Form.Item>
      </FormDrawer>
    </>
  );
};
