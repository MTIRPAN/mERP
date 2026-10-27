import { Avatar, Card, Empty, Flex, Input, List, Result, Select, Skeleton, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { getGetCareerCompanyLogoUrl, useGetCareerPage } from '@/shared/api/generated/endpoints/careers/careers';
import type { WorkModel } from '@/shared/api/generated/model';
import { workModels } from '../openings/openings';
import { OpeningFacts } from './careerParts';
import { matches } from './careers';

/**
 * The career page of a tenant (PLAN R3b, D49): the open public openings of all its companies, filtered by company,
 * place and work model. Anonymous and simple; works on a phone.
 */
export const CareerPage = () => {
  const { t } = useTranslation();
  const { tenant = '' } = useParams();
  const page = useGetCareerPage(tenant, { query: { meta: { silent: true } } });
  const [company, setCompany] = useState<string>();
  const [branch, setBranch] = useState<string>();
  const [workModel, setWorkModel] = useState<WorkModel>();
  const [search, setSearch] = useState('');

  const openings = useMemo(
    () =>
      (page.data?.openings ?? []).filter(
        (o) =>
          (!company || o.companyId === company) &&
          (!branch || o.branchId === branch) &&
          (!workModel || o.workModel === workModel) &&
          matches(o, search),
      ),
    [page.data, company, branch, workModel, search],
  );
  const branches = useMemo(() => {
    const seen = new Map<string, string>();
    for (const o of page.data?.openings ?? []) {
      if (o.branchId && o.branchName && (!company || o.companyId === company)) {
        seen.set(o.branchId, o.branchName);
      }
    }

    return [...seen].map(([value, label]) => ({ value, label }));
  }, [page.data, company]);

  if (page.isError) {
    return (
      <div className="merp-careers">
        <Result status="404" title={t('web.recruitment.careers.not_found')} />
      </div>
    );
  }

  return (
    <div className="merp-careers">
      <header className="merp-careers-header">
        <Typography.Title level={2}>{t('web.recruitment.careers.title')}</Typography.Title>
        <Flex gap={8} wrap>
          {(page.data?.companies ?? [])
            .filter((c) => c.hasLogo)
            .map((c) => (
              <Avatar key={c.id} shape="square" size={40} src={getGetCareerCompanyLogoUrl(tenant, c.id)} alt={c.name} />
            ))}
        </Flex>
      </header>
      <Flex gap={8} wrap className="merp-careers-filters">
        <Input.Search
          allowClear
          placeholder={t('web.recruitment.careers.search')}
          onChange={(event) => setSearch(event.target.value)}
          style={{ flex: '1 1 220px' }}
        />
        {(page.data?.companies.length ?? 0) > 1 && (
          <Select
            allowClear
            aria-label={t('web.recruitment.careers.fields.company')}
            placeholder={t('web.recruitment.careers.all_companies')}
            value={company}
            onChange={(value?: string) => {
              setCompany(value);
              setBranch(undefined);
            }}
            options={(page.data?.companies ?? []).map((c) => ({ value: c.id, label: c.name }))}
            style={{ flex: '1 1 180px' }}
          />
        )}
        <Select
          allowClear
          aria-label={t('web.recruitment.careers.fields.branch')}
          placeholder={t('web.recruitment.careers.all_branches')}
          value={branch}
          onChange={setBranch}
          options={branches}
          style={{ flex: '1 1 160px' }}
        />
        <Select
          allowClear
          aria-label={t('web.recruitment.careers.fields.work_model')}
          placeholder={t('web.recruitment.careers.all_work_models')}
          value={workModel}
          onChange={setWorkModel}
          options={workModels.map((m) => ({ value: m, label: t(`web.recruitment.openings.work_models.${m}`) }))}
          style={{ flex: '1 1 160px' }}
        />
      </Flex>
      {page.isLoading ? (
        <Skeleton active />
      ) : (
        <List
          dataSource={openings}
          locale={{ emptyText: <Empty description={t('web.recruitment.careers.no_openings')} /> }}
          renderItem={(opening) => (
            <List.Item key={opening.id}>
              <Card size="small" className="merp-careers-card">
                <Link to={`/careers/${tenant}/openings/${opening.id}`}>
                  <Typography.Title level={4} style={{ margin: 0 }}>
                    {opening.title}
                  </Typography.Title>
                </Link>
                <OpeningFacts opening={opening} />
              </Card>
            </List.Item>
          )}
        />
      )}
    </div>
  );
};
