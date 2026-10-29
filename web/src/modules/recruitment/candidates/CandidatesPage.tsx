import { PlusOutlined } from '@ant-design/icons';
import { Button, Input, Select, Space, Tag, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import {
  getListCandidatesQueryKey,
  listCandidates,
  useGetCandidateOptions,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { CandidateListItem } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatMomentDate } from '@/shared/format/date';
import { recruitmentPermissions } from '../permissions';
import { CandidateFormDrawer } from './CandidateFormDrawer';

/**
 * The candidate pool (PLAN R4b, D50): every candidate of the tenant; the search covers names, profile, tags, notes and
 * the text of the latest CV.
 */
export const CandidatesPage = () => {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const options = useGetCandidateOptions();
  const [search, setSearch] = useState<string>();
  const [tag, setTag] = useState<string>();
  const [source, setSource] = useState<string>();
  const [creating, setCreating] = useState(false);
  const sourceNames = useMemo(
    () => new Map((options.data?.sources ?? []).map((s) => [s.code, s.name])),
    [options.data],
  );

  const columns = useMemo<DataTableColumn<CandidateListItem>[]>(
    () => [
      {
        key: 'name',
        title: t('web.recruitment.candidates.fields.name'),
        sorter: true,
        render: (_, row) => (
          <Typography.Link onClick={() => void navigate(`/recruitment/candidates/${row.id}`)}>
            {row.fullName}
          </Typography.Link>
        ),
      },
      { key: 'email', dataIndex: 'email', title: t('web.recruitment.candidates.fields.email') },
      {
        key: 'title',
        title: t('web.recruitment.candidates.fields.current_title'),
        render: (_, row) => [row.currentTitle, row.currentCompany].filter(Boolean).join(' · ') || '—',
      },
      {
        key: 'location',
        dataIndex: 'location',
        title: t('web.recruitment.candidates.fields.location'),
        hiddenByDefault: true,
      },
      {
        key: 'tags',
        title: t('web.recruitment.candidates.fields.tags'),
        render: (_, row) => (
          <Space size={[4, 4]} wrap>
            {row.tags.map((tag) => (
              <Tag key={tag.code}>{tag.name}</Tag>
            ))}
          </Space>
        ),
      },
      {
        key: 'source',
        title: t('web.recruitment.candidates.fields.source'),
        render: (_, row) => sourceNames.get(row.source) ?? row.source,
      },
      {
        key: 'applicationCount',
        dataIndex: 'applicationCount',
        title: t('web.recruitment.candidates.fields.applications'),
      },
      {
        key: 'lastAppliedAt',
        title: t('web.recruitment.candidates.fields.last_applied_at'),
        sorter: true,
        render: (_, row) => (row.lastAppliedAt ? formatMomentDate(row.lastAppliedAt, i18n.language) : '—'),
      },
      {
        key: 'createdAt',
        title: t('web.recruitment.candidates.fields.created_at'),
        sorter: true,
        hiddenByDefault: true,
        render: (_, row) => formatMomentDate(row.createdAt, i18n.language),
      },
    ],
    [t, i18n.language, navigate, sourceNames],
  );

  const fetchPage = (list: ListParams) =>
    listCandidates({
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      search: list.filter?.search,
      tag: list.filter?.tag,
      source: list.filter?.source,
    });

  return (
    <>
      <PageHeader
        title={t('web.recruitment.candidates.title')}
        subtitle={t('web.recruitment.candidates.subtitle')}
        extra={
          <Can permission={recruitmentPermissions.candidatesManage}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
              {t('web.recruitment.candidates.new_title')}
            </Button>
          </Can>
        }
      />
      <DataTable<CandidateListItem>
        queryKey={getListCandidatesQueryKey()}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{ search, tag, source }}
        toolbar={
          <Space wrap>
            <Input.Search
              allowClear
              placeholder={t('web.recruitment.candidates.search')}
              onSearch={(value) => setSearch(value.trim() || undefined)}
              style={{ width: 320 }}
            />
            <Select
              allowClear
              aria-label={t('web.recruitment.candidates.fields.tags')}
              placeholder={t('web.recruitment.candidates.all_tags')}
              value={tag}
              onChange={setTag}
              options={(options.data?.tags ?? []).map((o) => ({ value: o.code, label: o.name }))}
              style={{ minWidth: 160 }}
            />
            <Select
              allowClear
              aria-label={t('web.recruitment.candidates.fields.source')}
              placeholder={t('web.recruitment.candidates.all_sources')}
              value={source}
              onChange={setSource}
              options={(options.data?.sources ?? []).map((o) => ({ value: o.code, label: o.name }))}
              style={{ minWidth: 180 }}
            />
          </Space>
        }
      />
      {creating && (
        <CandidateFormDrawer
          onClose={() => setCreating(false)}
          onSaved={(id) => void navigate(`/recruitment/candidates/${id}`)}
        />
      )}
    </>
  );
};
