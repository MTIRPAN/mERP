import { EditOutlined, LockOutlined } from '@ant-design/icons';
import {
  Alert,
  Button,
  Card,
  Col,
  Descriptions,
  Empty,
  Flex,
  Row,
  Skeleton,
  Space,
  Table,
  Tabs,
  Tag,
  Timeline,
  Tooltip,
  Typography,
  type DescriptionsProps,
} from 'antd';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { AuditHistoryButton } from '@/modules/platform/audit/AuditHistoryButton';
import { useGetEmployee } from '@/shared/api/generated/endpoints/hr/hr';
import { useGetCustomFieldForm } from '@/shared/api/generated/endpoints/platform/platform';
import type { EmployeeDetails, EmploymentItem, JobAssignmentItem } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { displayValue } from '@/shared/customFields/customFieldValues';
import { formatDate } from '@/shared/format/date';
import { NotFoundPage } from '@/shared/pages/NotFoundPage';
import { hrPermissions } from '../permissions';
import { EMPLOYEE_ENTITY } from './employeeFilters';
import { countryName, languageName, mainEmployment } from './employeeFormat';
import { EmployeePhoto } from './EmployeePhoto';
import { EmployeeStatusTag } from './EmployeeStatusTag';
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

const dash = '—';

type Drawer =
  | { kind: 'none' }
  | { kind: 'person' | 'contacts' | 'family' | 'qualifications' | 'sensitive' | 'rehire' }
  | { kind: 'assignment' | 'terminate' | 'transfer'; employment: EmploymentItem };

/** The edit button of a tab or a card, for those who may change it. */
const EditButton = ({ label, onClick }: { label: string; onClick: () => void }) => (
  <Button size="small" icon={<EditOutlined />} onClick={onClick}>
    {label}
  </Button>
);

/** A tab's content under a right-aligned toolbar. */
const TabBody = ({ toolbar, children }: { toolbar?: ReactNode; children: ReactNode }) => (
  <Flex vertical gap={12}>
    {toolbar && <Flex justify="flex-end">{toolbar}</Flex>}
    {children}
  </Flex>
);

/**
 * The employee card (PLAN H2c, D38): the person, contacts, employments with their job history, family, qualifications
 * and documents, with the edit forms and the life cycle actions the user may use. Sensitive values arrive masked
 * unless the user may see them; every value shown is audited by the server.
 */
export const EmployeeCardPage = () => {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const employee = useGetEmployee(id);
  const canUpdate = useHasPermission(hrPermissions.employeesUpdate);
  const canCreate = useHasPermission(hrPermissions.employeesCreate);
  const canTerminate = useHasPermission(hrPermissions.employeesTerminate);
  const canUpdateSensitive = useHasPermission(hrPermissions.sensitiveUpdate);
  const canReadDocuments = useHasPermission(hrPermissions.documentsRead);
  const [drawer, setDrawer] = useState<Drawer>({ kind: 'none' });

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
        <Summary data={data} main={main} canUpdate={canUpdate} />
        <Card>
          <Tabs
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
                    <Contacts data={data} />
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
                    <Family data={data} />
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

const Summary = ({ data, main, canUpdate }: { data: EmployeeDetails; main?: EmploymentItem; canUpdate: boolean }) => {
  const { t, i18n } = useTranslation();
  const current = main?.assignments.find((a) => a.isCurrent) ?? main?.assignments[0];
  return (
    <Card>
      <Flex gap={24} align="center" wrap>
        <EmployeePhoto employeeId={data.id} photoFileId={data.photoFileId} canManage={canUpdate} />
        <Flex vertical gap={4} style={{ flex: 1, minWidth: 240 }}>
          <Space wrap>
            <Typography.Title level={4} style={{ margin: 0 }}>
              {data.fullName}
            </Typography.Title>
            {main && <EmployeeStatusTag status={main.status} />}
          </Space>
          {current && (
            <Typography.Text>
              {current.jobName ?? dash} · {current.orgUnitName ?? dash}
            </Typography.Text>
          )}
          {main && (
            <Typography.Text type="secondary">
              {t('web.hr.employees.summary', {
                company: main.companyName,
                registry: main.registryNumber,
                date: formatDate(main.hireDate, i18n.language),
              })}
            </Typography.Text>
          )}
        </Flex>
      </Flex>
    </Card>
  );
};

/** A value the user may not see: masked by the server, shown with a lock. */
const Masked = ({ value, visible }: { value: string | null | undefined; visible: boolean }) => {
  const { t } = useTranslation();
  if (!value) {
    return dash;
  }

  return visible ? (
    value
  ) : (
    <Tooltip title={t('web.hr.employees.masked_hint')}>
      <Space size={4}>
        <LockOutlined />
        {value}
      </Space>
    </Tooltip>
  );
};

const General = ({
  data,
  main,
  sensitiveAction,
}: {
  data: EmployeeDetails;
  main?: EmploymentItem;
  sensitiveAction?: ReactNode;
}) => {
  const { t, i18n } = useTranslation();
  const fields = useGetCustomFieldForm({ entityType: EMPLOYEE_ENTITY, companyId: main?.companyId });
  const customFields = fields.data ?? [];
  const date = (value: string | null | undefined) => (value ? formatDate(value, i18n.language) : dash);
  const label = (prefix: string, value: string | null | undefined) => (value ? t(`${prefix}${value}`) : dash);

  const person: DescriptionsProps['items'] = [
    {
      key: 'tckn',
      label: t('web.hr.employees.fields.tckn'),
      children: <Masked value={data.tckn} visible={data.sensitiveVisible} />,
    },
    { key: 'birthDate', label: t('web.hr.employees.fields.birth_date'), children: date(data.birthDate) },
    { key: 'birthPlace', label: t('web.hr.employees.fields.birth_place'), children: data.birthPlace ?? dash },
    {
      key: 'nationality',
      label: t('web.hr.employees.fields.nationality'),
      children: countryName(data.nationality, i18n.language) ?? dash,
    },
    { key: 'gender', label: t('web.hr.employees.fields.gender'), children: label('web.hr.gender.', data.gender) },
    {
      key: 'maritalStatus',
      label: t('web.hr.employees.fields.marital_status'),
      children: label('web.hr.marital_status.', data.maritalStatus),
    },
    {
      key: 'militaryStatus',
      label: t('web.hr.employees.fields.military_status'),
      children: label('web.hr.military_status.', data.militaryStatus),
    },
    {
      key: 'iban',
      label: t('web.hr.employees.fields.iban'),
      children: <Masked value={data.iban} visible={data.sensitiveVisible} />,
    },
    ...customFields.map((field) => ({
      key: `customFields.${field.code}`,
      label: field.label,
      children: displayValue(field, data.customFields[field.code], t, i18n.language) || dash,
    })),
  ];

  const sensitive = data.sensitive;
  return (
    <Flex vertical gap={16}>
      <Descriptions column={{ xs: 1, md: 2 }} items={person} />
      <Card size="small" title={t('web.hr.employees.sensitive.title')} extra={sensitiveAction}>
        {sensitive.hidden ? (
          <Alert type="info" showIcon icon={<LockOutlined />} message={t('web.hr.employees.sensitive.hidden')} />
        ) : (
          <Descriptions
            column={{ xs: 1, md: 2 }}
            items={[
              {
                key: 'disability',
                label: t('web.hr.employees.sensitive.disability_degree'),
                children: sensitive.disabilityDegree != null ? `%${sensitive.disabilityDegree}` : dash,
              },
              {
                key: 'blood',
                label: t('web.hr.employees.sensitive.blood_type'),
                children: sensitive.bloodType ?? dash,
              },
              {
                key: 'criminal',
                label: t('web.hr.employees.sensitive.criminal_record_date'),
                children: date(sensitive.criminalRecordDate),
              },
              {
                key: 'health',
                label: t('web.hr.employees.sensitive.health_notes'),
                children: sensitive.healthNotes ?? dash,
                span: 2,
              },
            ]}
          />
        )}
      </Card>
    </Flex>
  );
};

const Contacts = ({ data }: { data: EmployeeDetails }) => {
  const { t } = useTranslation();
  const kind = (value: string) => t(`web.hr.contact_kind.${value}`);
  const primary = (isPrimary: boolean) => (isPrimary ? <Tag>{t('web.hr.employees.primary')}</Tag> : null);
  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} lg={12}>
        <Card size="small" title={t('web.hr.employees.contacts.emails')}>
          <Table
            size="small"
            pagination={false}
            rowKey={(row) => row.address}
            dataSource={data.emails}
            locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /> }}
            columns={[
              {
                key: 'kind',
                title: t('web.hr.employees.contacts.kind'),
                render: (_, row) => kind(row.kind),
                width: 120,
              },
              { key: 'address', dataIndex: 'address', title: t('web.hr.employees.contacts.email') },
              { key: 'primary', render: (_, row) => primary(row.isPrimary), width: 80 },
            ]}
          />
        </Card>
      </Col>
      <Col xs={24} lg={12}>
        <Card size="small" title={t('web.hr.employees.contacts.phones')}>
          <Table
            size="small"
            pagination={false}
            rowKey={(row) => row.number}
            dataSource={data.phones}
            locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /> }}
            columns={[
              {
                key: 'kind',
                title: t('web.hr.employees.contacts.kind'),
                render: (_, row) => kind(row.kind),
                width: 120,
              },
              { key: 'number', dataIndex: 'number', title: t('web.hr.employees.contacts.phone') },
              { key: 'primary', render: (_, row) => primary(row.isPrimary), width: 80 },
            ]}
          />
        </Card>
      </Col>
      <Col xs={24} lg={12}>
        <Card size="small" title={t('web.hr.employees.contacts.addresses')}>
          <Table
            size="small"
            pagination={false}
            rowKey={(row) => `${row.kind}-${row.line}`}
            dataSource={data.addresses}
            locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /> }}
            columns={[
              {
                key: 'kind',
                title: t('web.hr.employees.contacts.kind'),
                render: (_, row) => kind(row.kind),
                width: 120,
              },
              {
                key: 'line',
                title: t('web.hr.employees.contacts.address'),
                render: (_, row) =>
                  [row.line, row.district, row.city, row.postalCode, row.countryCode].filter(Boolean).join(', '),
              },
            ]}
          />
        </Card>
      </Col>
      <Col xs={24} lg={12}>
        <Card size="small" title={t('web.hr.employees.contacts.emergency')}>
          <Table
            size="small"
            pagination={false}
            rowKey={(row) => `${row.fullName}-${row.phone}`}
            dataSource={data.emergencyContacts}
            locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /> }}
            columns={[
              { key: 'name', dataIndex: 'fullName', title: t('web.hr.employees.contacts.name') },
              {
                key: 'relation',
                title: t('web.hr.employees.contacts.relation'),
                render: (_, row) => t(`web.hr.family_relation.${row.relation}`),
              },
              { key: 'phone', dataIndex: 'phone', title: t('web.hr.employees.contacts.phone') },
            ]}
          />
        </Card>
      </Col>
    </Row>
  );
};

const AssignmentText = ({ assignment }: { assignment: JobAssignmentItem }) => {
  const { t, i18n } = useTranslation();
  const until = assignment.validTo
    ? formatDate(assignment.validTo, i18n.language)
    : t('web.hr.employees.assignment.ongoing');
  return (
    <Flex vertical gap={2}>
      <Space wrap>
        <Typography.Text strong>
          {assignment.jobName ?? dash} · {assignment.positionCode ?? dash}
        </Typography.Text>
        <Tag>{t(`web.hr.assignment_reason.${assignment.reason}`)}</Tag>
        {assignment.isCurrent && <Tag color="success">{t('web.hr.employees.assignment.current')}</Tag>}
      </Space>
      <Typography.Text>
        {[assignment.orgUnitName, assignment.branchName].filter(Boolean).join(' · ') || dash}
      </Typography.Text>
      <Typography.Text type="secondary">
        {formatDate(assignment.validFrom, i18n.language)} – {until}
        {assignment.managerPositionCode
          ? ` · ${t('web.hr.employees.assignment.manager_override', { position: assignment.managerPositionCode })}`
          : ''}
      </Typography.Text>
    </Flex>
  );
};

const Employments = ({
  employments,
  actions,
}: {
  employments: EmploymentItem[];
  actions: (employment: EmploymentItem) => ReactNode;
}) => {
  const { t, i18n } = useTranslation();
  const date = (value: string | null | undefined) => (value ? formatDate(value, i18n.language) : dash);
  if (employments.length === 0) {
    return <Empty />;
  }

  return (
    <Flex vertical gap={16}>
      {employments.map((employment) => (
        <Card
          key={employment.id}
          size="small"
          extra={actions(employment)}
          title={
            <Space wrap>
              {employment.companyName}
              <Typography.Text type="secondary">{employment.registryNumber}</Typography.Text>
              <EmployeeStatusTag status={employment.status} />
              {employment.isPrimary && <Tag>{t('web.hr.employees.primary')}</Tag>}
            </Space>
          }
        >
          <Descriptions
            size="small"
            column={{ xs: 1, md: 3 }}
            items={[
              { key: 'hire', label: t('web.hr.employees.fields.hire_date'), children: date(employment.hireDate) },
              {
                key: 'seniority',
                label: t('web.hr.employees.fields.seniority_date'),
                children: employment.seniorityReason ? (
                  <Tooltip title={employment.seniorityReason}>{date(employment.seniorityDate)}</Tooltip>
                ) : (
                  date(employment.seniorityDate)
                ),
              },
              {
                key: 'type',
                label: t('web.hr.employees.fields.employment_type'),
                children: [
                  t(`web.hr.employment_type.${employment.type}`),
                  employment.contractEndDate ? date(employment.contractEndDate) : undefined,
                  employment.weeklyHours != null
                    ? t('web.hr.employees.weekly_hours_value', { hours: employment.weeklyHours })
                    : undefined,
                ]
                  .filter(Boolean)
                  .join(' · '),
              },
              {
                key: 'probation',
                label: t('web.hr.employees.fields.probation_end_date'),
                children: date(employment.probationEndDate),
              },
              ...(employment.terminationDate
                ? [
                    {
                      key: 'termination',
                      label: t('web.hr.employees.fields.termination_date'),
                      children: date(employment.terminationDate),
                    },
                    {
                      key: 'sgk',
                      label: t('web.hr.employees.fields.sgk_exit_code'),
                      children: employment.sgkExitCode
                        ? `${employment.sgkExitCode} · ${employment.sgkExitCodeName ?? ''}`
                        : dash,
                    },
                    {
                      key: 'rehireable',
                      label: t('web.hr.employees.fields.rehireable'),
                      children:
                        employment.rehireable == null
                          ? dash
                          : t(employment.rehireable ? 'web.common.yes' : 'web.common.no'),
                    },
                  ]
                : []),
            ]}
          />
          <Typography.Title level={5}>{t('web.hr.employees.assignment.history')}</Typography.Title>
          <Timeline
            items={employment.assignments.map((assignment) => ({
              key: assignment.id,
              color: assignment.isCurrent ? 'green' : 'gray',
              content: <AssignmentText assignment={assignment} />,
            }))}
          />
        </Card>
      ))}
    </Flex>
  );
};

const Family = ({ data }: { data: EmployeeDetails }) => {
  const { t, i18n } = useTranslation();
  return (
    <Table
      size="small"
      pagination={false}
      rowKey={(row) => `${row.relation}-${row.fullName}`}
      dataSource={data.familyMembers}
      locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /> }}
      columns={[
        { key: 'name', dataIndex: 'fullName', title: t('web.hr.employees.family.name') },
        {
          key: 'relation',
          title: t('web.hr.employees.family.relation'),
          render: (_, row) => t(`web.hr.family_relation.${row.relation}`),
        },
        {
          key: 'birthDate',
          title: t('web.hr.employees.fields.birth_date'),
          render: (_, row) => (row.birthDate ? formatDate(row.birthDate, i18n.language) : dash),
        },
        {
          key: 'isWorking',
          title: t('web.hr.employees.family.is_working'),
          render: (_, row) => t(row.isWorking ? 'web.common.yes' : 'web.common.no'),
        },
      ]}
    />
  );
};

const Qualifications = ({ data }: { data: EmployeeDetails }) => {
  const { t, i18n } = useTranslation();
  const date = (value: string | null | undefined) => (value ? formatDate(value, i18n.language) : dash);
  const empty = { emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /> };
  return (
    <Flex vertical gap={16}>
      <Card size="small" title={t('web.hr.employees.qualifications.education')}>
        <Table
          size="small"
          pagination={false}
          rowKey={(row) => `${row.level}-${row.school}`}
          dataSource={data.educations}
          locale={empty}
          columns={[
            {
              key: 'level',
              title: t('web.hr.employees.qualifications.level'),
              render: (_, row) => t(`web.hr.education_level.${row.level}`),
            },
            { key: 'school', dataIndex: 'school', title: t('web.hr.employees.qualifications.school') },
            {
              key: 'department',
              title: t('web.hr.employees.qualifications.department'),
              render: (_, row) => row.department ?? dash,
            },
            {
              key: 'year',
              title: t('web.hr.employees.qualifications.graduation_year'),
              render: (_, row) => row.graduationYear ?? dash,
            },
          ]}
        />
      </Card>
      <Card size="small" title={t('web.hr.employees.qualifications.certificates')}>
        <Table
          size="small"
          pagination={false}
          rowKey={(row) => `${row.kind}-${row.name}-${row.number ?? ''}`}
          dataSource={data.certificates}
          locale={empty}
          columns={[
            {
              key: 'kind',
              title: t('web.hr.employees.qualifications.kind'),
              render: (_, row) => t(`web.hr.certificate_kind.${row.kind}`),
            },
            { key: 'name', dataIndex: 'name', title: t('web.hr.employees.qualifications.name') },
            {
              key: 'number',
              title: t('web.hr.employees.qualifications.number'),
              render: (_, row) => row.number ?? dash,
            },
            {
              key: 'issued',
              title: t('web.hr.employees.qualifications.issued_on'),
              render: (_, row) => date(row.issuedOn),
            },
            {
              key: 'valid',
              title: t('web.hr.employees.qualifications.valid_until'),
              render: (_, row) => date(row.validUntil),
            },
          ]}
        />
      </Card>
      <Card size="small" title={t('web.hr.employees.qualifications.languages')}>
        <Table
          size="small"
          pagination={false}
          rowKey={(row) => row.code}
          dataSource={data.languages}
          locale={empty}
          columns={[
            {
              key: 'language',
              title: t('web.hr.employees.qualifications.language'),
              render: (_, row) => languageName(row.code, i18n.language),
            },
            {
              key: 'level',
              title: t('web.hr.employees.qualifications.language_level'),
              render: (_, row) => t(`web.hr.language_level.${row.level}`),
            },
          ]}
        />
      </Card>
      <Descriptions
        items={[
          {
            key: 'military',
            label: t('web.hr.employees.fields.military_status'),
            children: data.militaryStatus ? t(`web.hr.military_status.${data.militaryStatus}`) : dash,
          },
        ]}
      />
    </Flex>
  );
};
