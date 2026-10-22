import { EditOutlined, UserOutlined } from '@ant-design/icons';
import { App, Avatar, Button, Card, Flex, Popconfirm, Result, Skeleton, Space, Tabs, Tooltip } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useGetMyEmployee,
  useListMyProfileChanges,
  useWithdrawProfileChange,
} from '@/shared/api/generated/endpoints/hr/hr';
import type { FamilyMember, ProfileChangeItem, ProfileChangeKind } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { mainEmployment } from '../employees/employeeFormat';
import {
  Contacts,
  Employments,
  Family,
  General,
  Qualifications,
  Summary,
  TabBody,
  type ContactLists,
} from '../employees/EmployeeSections';
import { ContactsFormDrawer } from '../employees/forms/ContactsFormDrawer';
import { FamilyFormDrawer } from '../employees/forms/FamilyFormDrawer';
import { useCardTab } from '../employees/useCardTab';
import { hrPermissions } from '../permissions';
import { IbanChangeDrawer } from './IbanChangeDrawer';
import { ProfileChangeList } from './ProfileChangeList';
import { isOpenProfileChange } from './profileChanges';
import { useProfileChangesRefresh } from './useProfileChangesRefresh';

/** The change request form open on the page; with a returned request to correct. */
type RequestForm = { kind: 'none' } | { kind: ProfileChangeKind; returned?: ProfileChangeItem };

/**
 * "Kartım" (PLAN H3c, D39): the card of the person linked to the user, with their own TCKN and IBAN, and change
 * requests for the contacts, the family and the IBAN that HR approves before they reach the card.
 */
export const MyCardPage = () => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const canRequest = useHasPermission(hrPermissions.selfProfileRequest);
  const card = useGetMyEmployee({ query: { retry: false, meta: { silent: true } } });
  const requests = useListMyProfileChanges({ query: { enabled: canRequest } });
  const withdraw = useWithdrawProfileChange();
  const refreshRequests = useProfileChangesRefresh();
  const [form, setForm] = useState<RequestForm>({ kind: 'none' });
  const [tab, selectTab] = useCardTab([
    'general',
    'contacts',
    'employment',
    'family',
    'qualifications',
    ...(canRequest ? ['requests'] : []),
  ]);

  if (card.isError && isApiError(card.error) && card.error.status === 404) {
    return (
      <>
        <PageHeader title={t('web.hr.me.title')} />
        <Result status="info" title={t('web.hr.me.not_linked')} subTitle={t('web.hr.me.not_linked_hint')} />
      </>
    );
  }

  if (!card.data) {
    return <Skeleton active />;
  }

  const data = card.data;
  const main = mainEmployment(data.employments);
  const close = () => setForm({ kind: 'none' });
  const openRequest = (kind: ProfileChangeKind) =>
    requests.data?.find((item) => item.kind === kind && isOpenProfileChange(item));

  const requestButton = (kind: ProfileChangeKind) => {
    if (!canRequest) {
      return undefined;
    }

    const pending = openRequest(kind);
    return pending ? (
      <Tooltip title={t('web.hr.me.request_open', { number: pending.number })}>
        <Button size="small" icon={<EditOutlined />} disabled>
          {t('web.hr.me.request_change')}
        </Button>
      </Tooltip>
    ) : (
      <Button size="small" icon={<EditOutlined />} onClick={() => setForm({ kind })}>
        {t('web.hr.me.request_change')}
      </Button>
    );
  };

  const withdrawRequest = async (item: ProfileChangeItem) => {
    await withdraw.mutateAsync({ id: item.id });
    await refreshRequests();
    message.success(t('web.hr.me.withdrawn', { number: item.number }));
  };

  const requestActions = (item: ProfileChangeItem) =>
    isOpenProfileChange(item) ? (
      <Space size={4} wrap>
        {item.status === 'Returned' && (
          <Button size="small" type="primary" onClick={() => setForm({ kind: item.kind, returned: item })}>
            {t('web.hr.me.correct')}
          </Button>
        )}
        <Popconfirm
          title={t('web.hr.me.withdraw_confirm', { number: item.number })}
          okText={t('web.hr.me.withdraw')}
          cancelText={t('web.common.cancel')}
          onConfirm={() => withdrawRequest(item)}
        >
          <Button size="small" danger>
            {t('web.hr.me.withdraw')}
          </Button>
        </Popconfirm>
      </Space>
    ) : undefined;

  const returned = form.kind !== 'none' ? form.returned : undefined;
  return (
    <>
      <PageHeader title={t('web.hr.me.title')} subtitle={data.fullName} />
      <Flex vertical gap={16}>
        <Summary data={data} main={main} photo={<Avatar size={96} icon={<UserOutlined />} />} />
        <Card>
          <Tabs
            activeKey={tab}
            onChange={selectTab}
            items={[
              {
                key: 'general',
                label: t('web.hr.employees.tabs.general'),
                children: <General data={data} main={main} showSensitive={false} ibanAction={requestButton('Iban')} />,
              },
              {
                key: 'contacts',
                label: t('web.hr.employees.tabs.contacts'),
                children: (
                  <TabBody toolbar={requestButton('Contacts')}>
                    <Contacts lists={data} />
                  </TabBody>
                ),
              },
              {
                key: 'employment',
                label: t('web.hr.employees.tabs.employment'),
                children: <Employments employments={data.employments} actions={() => undefined} />,
              },
              {
                key: 'family',
                label: t('web.hr.employees.tabs.family'),
                children: (
                  <TabBody toolbar={requestButton('Family')}>
                    <Family members={data.familyMembers} />
                  </TabBody>
                ),
              },
              {
                key: 'qualifications',
                label: t('web.hr.employees.tabs.qualifications'),
                children: <Qualifications data={data} />,
              },
              ...(canRequest
                ? [
                    {
                      key: 'requests',
                      label: t('web.hr.me.tabs.requests'),
                      children: (
                        <ProfileChangeList
                          items={requests.data}
                          loading={requests.isLoading}
                          actions={requestActions}
                        />
                      ),
                    },
                  ]
                : []),
            ]}
          />
        </Card>
      </Flex>
      {form.kind === 'Contacts' && (
        <ContactsFormDrawer
          open
          employee={data}
          onClose={close}
          request={{
            requestId: returned?.id,
            initial: returned?.proposed as ContactLists | undefined,
            note: returned?.note,
          }}
        />
      )}
      {form.kind === 'Family' && (
        <FamilyFormDrawer
          open
          employee={data}
          onClose={close}
          request={{
            requestId: returned?.id,
            initial: returned?.proposed as FamilyMember[] | undefined,
            note: returned?.note,
          }}
        />
      )}
      {form.kind === 'Iban' && (
        <IbanChangeDrawer
          open
          onClose={close}
          requestId={returned?.id}
          initial={returned ? { iban: returned.proposedIban, note: returned.note } : undefined}
        />
      )}
    </>
  );
};
