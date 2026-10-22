import { Button, Card, Flex, Skeleton, Space, Tabs } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import { useGetEmployee } from '@/shared/api/generated/endpoints/hr/hr';
import type { EmploymentItem } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { NotFoundPage } from '@/shared/pages/NotFoundPage';
import { hrPermissions } from '../permissions';
import { EmployeeProfileChangesTab } from '../selfService/EmployeeProfileChangesTab';
import { mainEmployment } from './employeeFormat';
import { EmployeePhoto } from './EmployeePhoto';
import {
  Contacts,
  EditButton,
  Employments,
  Family,
  General,
  Qualifications,
  Summary,
  TabBody,
} from './EmployeeSections';
import { DocumentsTab } from './documents/DocumentsTab';
import { ChangeAssignmentDrawer } from './forms/ChangeAssignmentDrawer';
import { ContactsFormDrawer } from './forms/ContactsFormDrawer';
import { FamilyFormDrawer } from './forms/FamilyFormDrawer';
import { GroupTransferDrawer } from './forms/GroupTransferDrawer';
import { PersonFormDrawer } from './forms/PersonFormDrawer';
import { QualificationsFormDrawer } from './forms/QualificationsFormDrawer';
import { RehireDrawer } from './forms/RehireDrawer';
import { SensitiveFormDrawer } from './forms/SensitiveFormDrawer';
import { TerminateDrawer } from './forms/TerminateDrawer';
import { useCardTab } from './useCardTab';
import { EmployeeUserSection } from './user/EmployeeUserSection';

type Drawer =
  | { kind: 'none' }
  | { kind: 'person' | 'contacts' | 'family' | 'qualifications' | 'sensitive' | 'rehire' }
  | { kind: 'assignment' | 'terminate' | 'transfer'; employment: EmploymentItem };

/**
 * The employee card (PLAN H2c, D38): the person, contacts, employments with their job history, family, qualifications,
 * documents and change requests, with the system user, the edit forms and the life cycle actions the user may use.
 * Sensitive values arrive masked unless the user may see them; every value shown is audited by the server.
 */
export const EmployeeCardPage = () => {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  // While Platform handles a user request the card asks again until the answer arrives (D39)
  const employee = useGetEmployee(id, {
    query: { refetchInterval: (query) => (query.state.data?.userLinkState === 'Pending' ? 3000 : false) },
  });
  const canUpdate = useHasPermission(hrPermissions.employeesUpdate);
  const canCreate = useHasPermission(hrPermissions.employeesCreate);
  const canTerminate = useHasPermission(hrPermissions.employeesTerminate);
  const canUpdateSensitive = useHasPermission(hrPermissions.sensitiveUpdate);
  const canReadDocuments = useHasPermission(hrPermissions.documentsRead);
  const [drawer, setDrawer] = useState<Drawer>({ kind: 'none' });
  const [tab, selectTab] = useCardTab([
    'general',
    'contacts',
    'employment',
    'family',
    'qualifications',
    ...(canReadDocuments ? ['documents'] : []),
    'requests',
  ]);

  if (employee.isError && isApiError(employee.error) && employee.error.status === 404) {
    return <NotFoundPage />;
  }

  if (!employee.data) {
    return <Skeleton active />;
  }

  const data = employee.data;
  const main = mainEmployment(data.employments);
  const close = () => setDrawer({ kind: 'none' });
  const edit = (kind: 'person' | 'contacts' | 'family' | 'qualifications') =>
    canUpdate ? <EditButton label={t('web.common.edit')} onClick={() => setDrawer({ kind })} /> : undefined;
  const employmentActions = (employment: EmploymentItem) =>
    employment.status === 'Terminated' ? undefined : (
      <Space wrap size={4}>
        {canUpdate && (
          <Button size="small" onClick={() => setDrawer({ kind: 'assignment', employment })}>
            {t('web.hr.employees.lifecycle.change_assignment')}
          </Button>
        )}
        {canUpdate && (
          <Button size="small" onClick={() => setDrawer({ kind: 'transfer', employment })}>
            {t('web.hr.employees.lifecycle.group_transfer')}
          </Button>
        )}
        {canTerminate && (
          <Button size="small" danger onClick={() => setDrawer({ kind: 'terminate', employment })}>
            {t('web.hr.employees.lifecycle.terminate')}
          </Button>
        )}
      </Space>
    );

  return (
    <>
      <PageHeader
        title={data.fullName}
        subtitle={main ? `${main.companyName} · ${main.registryNumber}` : undefined}
        breadcrumb={[
          { title: <Link to="/hr/employees">{t('web.hr.employees.title')}</Link> },
          { title: data.fullName },
        ]}
        extra={
          <>
            <AuditHistoryButton module="hr" entityType="Employee" entityId={data.id} title={data.fullName} />
            {canCreate && (
              <Button onClick={() => setDrawer({ kind: 'rehire' })}>{t('web.hr.employees.lifecycle.rehire')}</Button>
            )}
          </>
        }
      />
      <Flex vertical gap={16}>
        <Summary
          data={data}
          main={main}
          photo={<EmployeePhoto employeeId={data.id} photoFileId={data.photoFileId} canManage={canUpdate} />}
          extra={<EmployeeUserSection employee={data} />}
        />
        <Card>
          <Tabs
            activeKey={tab}
            onChange={selectTab}
            items={[
              {
                key: 'general',
                label: t('web.hr.employees.tabs.general'),
                children: (
                  <TabBody toolbar={edit('person')}>
                    <General
                      data={data}
                      main={main}
                      sensitiveAction={
                        canUpdateSensitive && data.sensitiveVisible ? (
                          <EditButton label={t('web.common.edit')} onClick={() => setDrawer({ kind: 'sensitive' })} />
                        ) : undefined
                      }
                    />
                  </TabBody>
                ),
              },
              {
                key: 'contacts',
                label: t('web.hr.employees.tabs.contacts'),
                children: (
                  <TabBody toolbar={edit('contacts')}>
                    <Contacts lists={data} />
                  </TabBody>
                ),
              },
              {
                key: 'employment',
                label: t('web.hr.employees.tabs.employment'),
                children: <Employments employments={data.employments} actions={employmentActions} />,
              },
              {
                key: 'family',
                label: t('web.hr.employees.tabs.family'),
                children: (
                  <TabBody toolbar={edit('family')}>
                    <Family members={data.familyMembers} />
                  </TabBody>
                ),
              },
              {
                key: 'qualifications',
                label: t('web.hr.employees.tabs.qualifications'),
                children: (
                  <TabBody toolbar={edit('qualifications')}>
                    <Qualifications data={data} />
                  </TabBody>
                ),
              },
              ...(canReadDocuments
                ? [
                    {
                      key: 'documents',
                      label: t('web.hr.employees.tabs.documents'),
                      children: <DocumentsTab employeeId={data.id} />,
                    },
                  ]
                : []),
              {
                key: 'requests',
                label: t('web.hr.employees.tabs.requests'),
                children: <EmployeeProfileChangesTab employeeId={data.id} />,
              },
            ]}
          />
        </Card>
      </Flex>
      {drawer.kind === 'person' && (
        <PersonFormDrawer open employee={data} companyId={main?.companyId} onClose={close} />
      )}
      {drawer.kind === 'contacts' && <ContactsFormDrawer open employee={data} onClose={close} />}
      {drawer.kind === 'family' && <FamilyFormDrawer open employee={data} onClose={close} />}
      {drawer.kind === 'qualifications' && <QualificationsFormDrawer open employee={data} onClose={close} />}
      {drawer.kind === 'sensitive' && <SensitiveFormDrawer open employee={data} onClose={close} />}
      {drawer.kind === 'rehire' && (
        <RehireDrawer
          open
          employeeId={data.id}
          openCompanyIds={data.employments.filter((e) => e.status !== 'Terminated').map((e) => e.companyId)}
          onClose={close}
        />
      )}
      {drawer.kind === 'assignment' && (
        <ChangeAssignmentDrawer open employeeId={data.id} employment={drawer.employment} onClose={close} />
      )}
      {drawer.kind === 'terminate' && (
        <TerminateDrawer open employeeId={data.id} employment={drawer.employment} onClose={close} />
      )}
      {drawer.kind === 'transfer' && (
        <GroupTransferDrawer open employeeId={data.id} employment={drawer.employment} onClose={close} />
      )}
    </>
  );
};
