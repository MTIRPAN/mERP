import { DownloadOutlined, SettingOutlined } from '@ant-design/icons';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { Button, Checkbox, Dropdown, Flex, Table, Tooltip, type TableColumnType, type TableProps } from 'antd';
import type { SorterResult } from 'antd/es/table/interface';
import { useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { MAX_PAGE_SIZE, type ListParams, type PagedResult } from '@/shared/api/paging';

export interface DataTableColumn<T> extends TableColumnType<T> {
  key: string;
  /** Hidden until the user enables it in the column chooser. */
  hiddenByDefault?: boolean;
}

interface DataTableProps<T> {
  queryKey: readonly unknown[];
  fetchPage: (params: ListParams) => Promise<PagedResult<T>>;
  columns: DataTableColumn<T>[];
  rowKey: keyof T & string;
  filter?: ListParams['filter'];
  /** Server-side Excel export with the same filter, sort and visible columns. */
  onExport?: (params: Omit<ListParams, 'page' | 'pageSize'> & { columns: string[] }) => void | Promise<void>;
  toolbar?: ReactNode;
  defaultPageSize?: number;
}

/** Server-side paginated, sortable table with a column chooser and Excel export (antd Table, D18). */
export const DataTable = <T extends object>({
  queryKey,
  fetchPage,
  columns,
  rowKey,
  filter,
  onExport,
  toolbar,
  defaultPageSize = 20,
}: DataTableProps<T>) => {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(defaultPageSize);
  const [sort, setSort] = useState<string | undefined>();
  const [visible, setVisible] = useState<string[]>(() => columns.filter((c) => !c.hiddenByDefault).map((c) => c.key));
  const [exporting, setExporting] = useState(false);

  const params: ListParams = { page, pageSize: Math.min(pageSize, MAX_PAGE_SIZE), sort, filter };
  const query = useQuery({
    queryKey: [...queryKey, params],
    queryFn: () => fetchPage(params),
    placeholderData: keepPreviousData,
  });

  const shownColumns = useMemo(() => columns.filter((c) => visible.includes(c.key)), [columns, visible]);

  const onChange: TableProps<T>['onChange'] = (pagination, _filters, sorter) => {
    setPage(pagination.current ?? 1);
    setPageSize(pagination.pageSize ?? defaultPageSize);
    const single: SorterResult<T> | undefined = Array.isArray(sorter) ? sorter[0] : sorter;
    const field = single?.columnKey ?? single?.field;
    setSort(single?.order && field ? `${single.order === 'descend' ? '-' : ''}${String(field)}` : undefined);
  };

  const columnChooser = (
    <Dropdown
      trigger={['click']}
      popupRender={() => (
        <Flex vertical gap={6} style={{ padding: 12 }} className="ant-dropdown-menu">
          {columns.map((column) => (
            <Checkbox
              key={column.key}
              checked={visible.includes(column.key)}
              onChange={(event) =>
                setVisible((current) =>
                  event.target.checked ? [...current, column.key] : current.filter((key) => key !== column.key),
                )
              }
            >
              {column.title as ReactNode}
            </Checkbox>
          ))}
        </Flex>
      )}
    >
      <Tooltip title={t('web.table.columns')}>
        <Button icon={<SettingOutlined />} aria-label={t('web.table.columns')} />
      </Tooltip>
    </Dropdown>
  );

  return (
    <Flex vertical gap={12}>
      <Flex justify="space-between" align="center" gap={8} wrap>
        <div>{toolbar}</div>
        <Flex gap={8}>
          {onExport && (
            <Button
              icon={<DownloadOutlined />}
              loading={exporting}
              onClick={() => {
                setExporting(true);
                // Visible columns in the table's order
                const keys = columns.filter((c) => visible.includes(c.key)).map((c) => c.key);
                void Promise.resolve(onExport({ sort, filter, columns: keys })).finally(() => setExporting(false));
              }}
            >
              {t('web.table.export')}
            </Button>
          )}
          {columnChooser}
        </Flex>
      </Flex>
      <Table<T>
        rowKey={rowKey}
        columns={shownColumns}
        dataSource={query.data?.items ?? []}
        loading={query.isFetching}
        onChange={onChange}
        locale={{ emptyText: t('web.table.empty') }}
        pagination={{
          current: page,
          pageSize,
          total: query.data?.totalCount ?? 0,
          showSizeChanger: true,
          pageSizeOptions: [10, 20, 50, 100, 200],
          showTotal: (total) => t('web.table.total', { total }),
        }}
        scroll={{ x: 'max-content' }}
      />
    </Flex>
  );
};
