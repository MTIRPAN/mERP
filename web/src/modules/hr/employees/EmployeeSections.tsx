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
  Space,
  Table,
  Tag,
  Timeline,
  Tooltip,
  Typography,
  type DescriptionsProps,
} from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useGetCustomFieldForm } from '@/shared/api/generated/endpoints/platform/platform';
import type { EmployeeDetails, EmploymentItem, FamilyMember, JobAssignmentItem } from '@/shared/api/generated/model';
import { displayValue } from '@/shared/customFields/customFieldValues';
import { formatDate } from '@/shared/format/date';
import { EMPLOYEE_ENTITY } from './employeeFilters';
import { countryName, languageName } from './employeeFormat';
import { EmployeeStatusTag } from './EmployeeStatusTag';

export const dash = '—';

/** The edit button of a tab or a card, for those who may change it. */
export const EditButton = ({ label, onClick }: { label: string; onClick: () => void }) => (
  <Button size="small" icon={<EditOutlined />} onClick={onClick}>
    {label}
  </Button>
);

/** A tab's content under a right-aligned toolbar. */
export const TabBody = ({ toolbar, children }: { toolbar?: ReactNode; children: ReactNode }) => (
  <Flex vertical gap={12}>
    {toolbar && <Flex justify="flex-end">{toolbar}</Flex>}
    {children}
  </Flex>
);

/** The head of the card: the photo, the name, the current job and the main employment, with extra content at the end. */
export const Summary = ({
  data,
  main,
  photo,
  extra,
}: {
  data: EmployeeDetails;
  main?: EmploymentItem;
  photo: ReactNode;
  extra?: ReactNode;
}) => {
  const { t, i18n } = useTranslation();
  const current = main?.assignments.find((a) => a.isCurrent) ?? main?.assignments[0];
  return (
    <Card>
      <Flex gap={24} align="center" wrap>
        {photo}
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
        {extra}
      </Flex>
    </Card>
  );
};

/** A value the user may not see: masked by the server, shown with a lock. */
export const Masked = ({ value, visible }: { value: string | null | undefined; visible: boolean }) => {
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

/**
 * The person's identity, IBAN and custom fields, and the special category data unless the screen leaves them out
 * (self service shows the person's own card without them, D39).
 */
export const General = ({
  data,
  main,
  sensitiveAction,
  ibanAction,
  showSensitive = true,
}: {
  data: EmployeeDetails;
  main?: EmploymentItem;
  sensitiveAction?: ReactNode;
  ibanAction?: ReactNode;
  showSensitive?: boolean;
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
      children: (
        <Space wrap>
          <Masked value={data.iban} visible={data.sensitiveVisible} />
          {ibanAction}
        </Space>
      ),
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
      {showSensitive && (
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
      )}
    </Flex>
  );
};

/** The contact lists of a card, or of a change request in the same shape. */
export type ContactLists = Pick<EmployeeDetails, 'emails' | 'phones' | 'addresses' | 'emergencyContacts'>;

export const Contacts = ({ lists }: { lists: ContactLists }) => {
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
            dataSource={lists.emails}
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
            dataSource={lists.phones}
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
            dataSource={lists.addresses}
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
            dataSource={lists.emergencyContacts}
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

export const Employments = ({
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

export const Family = ({ members }: { members: FamilyMember[] }) => {
  const { t, i18n } = useTranslation();
  return (
    <Table
      size="small"
      pagination={false}
      rowKey={(row) => `${row.relation}-${row.fullName}`}
      dataSource={members}
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

export const Qualifications = ({ data }: { data: EmployeeDetails }) => {
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
