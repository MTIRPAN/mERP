import { PlusOutlined } from '@ant-design/icons';
import { Button, Flex, Input, Segmented, Space } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { exportEmployees, getListEmployeesQueryKey, listEmployees } from '@/shared/api/generated/endpoints/hr/hr';
import { useGetCustomFieldForm } from '@/shared/api/generated/endpoints/platform/platform';
import type { EmployeeListItem } from '@/shared/api/generated/model';
import { useListExport } from '@/shared/api/listExport';
import type { ListParams } from '@/shared/api/paging';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { CustomFieldFilter } from '@/shared/customFields/CustomFieldFilter';
import { displayValue } from '@/shared/customFields/customFieldValues';
import { formatDate } from '@/shared/format/date';
import { hrPermissions } from '../permissions';
import { EmployeeAvatar } from './EmployeePhoto';
import { EmployeeStatusTag } from './EmployeeStatusTag';
import { EMPLOYEE_ENTITY, statusFilters, toStatusParam, type StatusFilter } from './employeeFilters';
import { HireWizard } from './HireWizard';

/**
 * Employees in the caller's scope, one row per person and company (PLAN H2c, D38). The default list shows who works
 * today; the status filter shows who is about to start and who left. The active company narrows the list (D4).
 */
export const EmployeesPage = () => {
  const { t, i18n } = useTranslation();
  const activeCompanyId = useActiveCompanyId();
  const canExport = useHasPermission(hrPermissions.employeesExport);
  const [params] = useSearchParams();
  const [search, setSearch] = useState<string | undefined>(() => params.get('search') ?? undefined);
  const [status, setStatus] = useState<StatusFilter>(() => (params.get('search') ? 'all' : 'active'));
  const [customFilter, setCustomFilter] = useState<string>();
  const [hiring, setHiring] = useState(false);
  // Fields of every employee, as optional columns and filters (D30)
  const fields = useGetCustomFieldForm({ entityType: EMPLOYEE_ENTITY });
  const customFields = useMemo(() => fields.data ?? [], [fields.data]);

  const columns = useMemo<DataTableColumn<EmployeeListItem>[]>(
    () => [
      {
        key: 'firstName',
        title: t('web.hr.employees.fields.name'),
        sorter: true,
        render: (_, row) => (
          <Flex gap={8} align="center">
            <EmployeeAvatar employeeId={row.id} photoFileId={row.photoFileId} />
            <Link to={`/hr/employees/${row.id}`}>{row.fullName}</Link>
          </Flex>
        ),
      },
      {
        key: 'registryNumber',
        dataIndex: 'registryNumber',
        title: t('web.hr.employees.fields.registry_number'),
        sorter: true,
        width: 120,
      },
      { key: 'tckn', dataIndex: 'tckn', title: t('web.hr.employees.fields.tckn'), hiddenByDefault: true },
      { key: 'companyName', dataIndex: 'companyName', title: t('web.hr.employees.fields.company') },
      { key: 'orgUnitName', dataIndex: 'orgUnitName', title: t('web.hr.employees.fields.org_unit') },
      {
        key: 'positionCode',
        dataIndex: 'positionCode',
        title: t('web.hr.employees.fields.position'),
        hiddenByDefault: true,
      },
      { key: 'jobName', dataIndex: 'jobName', title: t('web.hr.employees.fields.job') },
      { key: 'branchName', dataIndex: 'branchName', title: t('web.hr.employees.fields.branch'), hiddenByDefault: true },
      {
        key: 'hireDate',
        title: t('web.hr.employees.fields.hire_date'),
        sorter: true,
        render: (_, row) => formatDate(row.hireDate, i18n.language),
      },
      {
        key: 'terminationDate',
        title: t('web.hr.employees.fields.termination_date'),
        sorter: true,
        hiddenByDefault: true,
        render: (_, row) => (row.terminationDate ? formatDate(row.terminationDate, i18n.language) : '—'),
      },
      ...customFields.map<DataTableColumn<EmployeeListItem>>((field) => ({
        key: `customFields.${field.code}`,
        title: field.label,
        hiddenByDefault: true,
        render: (_, row) => displayValue(field, row.customFields[field.code], t, i18n.language),
      })),
      {
        key: 'status',
        title: t('web.hr.employees.fields.status'),
        render: (_, row) => <EmployeeStatusTag status={row.status} />,
      },
    ],
    [t, i18n.language, customFields],
  );

  const download = useListExport();

  const fetchPage = (list: ListParams) =>
    listEmployees({
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      search: list.filter?.search,
      status: toStatusParam(list.filter?.status as StatusFilter | undefined),
      customFields: list.filter?.customFields,
    });

  return (
    <>
      <PageHeader
        title={t('web.hr.employees.title')}
        subtitle={t('web.hr.employees.subtitle')}
        extra={
          <Can permission={hrPermissions.employeesCreate}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setHiring(true)}>
              {t('web.hr.employees.hire')}
            </Button>
          </Can>
        }
      />
      <DataTable<EmployeeListItem>
        // The active company is sent as X-Company-Id; it is part of the key so the list refreshes when it changes
        queryKey={[...getListEmployeesQueryKey(), activeCompanyId]}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="employmentId"
        filter={{ search, status, customFields: customFilter }}
        onExport={
          canExport
            ? ({ sort, filter, columns: keys }) =>
                download(
                  exportEmployees({
                    columns: keys.join(','),
                    sort,
                    search: filter?.search,
                    status: toStatusParam(filter?.status as StatusFilter | undefined),
                    customFields: filter?.customFields,
                  }),
                  t('web.hr.employees.title'),
                )
            : undefined
        }
        toolbar={
          <Space wrap>
            <Input.Search
              allowClear
              defaultValue={search}
              placeholder={t('web.hr.employees.search')}
              onSearch={(value) => setSearch(value.trim() || undefined)}
              style={{ width: 280 }}
            />
            <CustomFieldFilter fields={customFields} onChange={setCustomFilter} />
            <Segmented<StatusFilter>
              value={status}
              onChange={setStatus}
              options={statusFilters.map((value) => ({ value, label: t(`web.hr.employees.filters.${value}`) }))}
            />
          </Space>
        }
      />
      {hiring && <HireWizard open onClose={() => setHiring(false)} />}
    </>
  );
};
