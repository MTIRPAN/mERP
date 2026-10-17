import { ApartmentOutlined, ClusterOutlined, PlusOutlined } from '@ant-design/icons';
import {
  Button,
  Card,
  Col,
  Empty,
  Flex,
  Input,
  Row,
  Segmented,
  Select,
  Skeleton,
  Statistic,
  Tag,
  Tree,
  Typography,
} from 'antd';
import type { DataNode } from 'antd/es/tree';
import type { TFunction } from 'i18next';
import { useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { useMe } from '@/app/me/meContext';
import { useGetOrgUnitTree } from '@/shared/api/generated/endpoints/hr/hr';
import type { OrgUnitTreeNode } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { hrPermissions } from '../permissions';
import { PositionFormDrawer } from '../positions/PositionFormDrawer';
import { MoveOrgUnitModal } from './MoveOrgUnitModal';
import { OrgChart } from './OrgChart';
import { OrgUnitFormDrawer } from './OrgUnitFormDrawer';
import { OrgUnitPanel } from './OrgUnitPanel';
import { buildUnitTree, totals, type UnitNode } from './unitTree';

type ViewMode = 'tree' | 'chart';

type UnitDrawer = { open: false } | { open: true; unitId?: string; parentId?: string };
type PositionDrawer = { open: false } | { open: true; positionId?: string; orgUnitId?: string };

/**
 * The organization of a company (PLAN H1b, D37): the unit tree or the organization chart, the staffing of each
 * unit and the vacancies. The company is the active company of the header; without one the user picks it here.
 */
export const OrgStructurePage = () => {
  const { t, i18n } = useTranslation();
  const me = useMe();
  const activeCompanyId = useActiveCompanyId();
  const canManage = useHasPermission(hrPermissions.orgManage);
  const [pickedCompanyId, setPickedCompanyId] = useState<string>();
  const companyId = activeCompanyId ?? pickedCompanyId ?? me.companies[0]?.id;
  const [view, setView] = useState<ViewMode>('tree');
  const [search, setSearch] = useState('');
  const [selectedId, setSelectedId] = useState<string>();
  const [unitDrawer, setUnitDrawer] = useState<UnitDrawer>({ open: false });
  const [positionDrawer, setPositionDrawer] = useState<PositionDrawer>({ open: false });
  const [moving, setMoving] = useState<string>();
  const tree = useGetOrgUnitTree({ companyId }, { query: { enabled: !!companyId } });
  const units = useMemo(() => tree.data?.units ?? [], [tree.data]);
  const selected = units.find((unit) => unit.id === selectedId);
  const sum = totals(units);

  const locale = i18n.language;
  const treeData = useMemo(
    () => toTreeData(buildUnitTree(units), search.trim(), locale, t),
    [units, search, locale, t],
  );

  const panel = selected ? (
    <OrgUnitPanel
      key={selected.id}
      unit={selected}
      canManage={canManage}
      onEdit={() => setUnitDrawer({ open: true, unitId: selected.id })}
      onAddSubUnit={() => setUnitDrawer({ open: true, parentId: selected.id })}
      onMove={() => setMoving(selected.id)}
      onAddPosition={() => setPositionDrawer({ open: true, orgUnitId: selected.id })}
      onEditPosition={(positionId) => setPositionDrawer({ open: true, positionId })}
    />
  ) : (
    <Card>
      <Empty description={t('web.hr.org.select_unit')} />
    </Card>
  );

  return (
    <>
      <PageHeader
        title={t('web.hr.org.title')}
        subtitle={t('web.hr.org.subtitle')}
        extra={
          <>
            {!activeCompanyId && (
              <Select
                aria-label={t('web.hr.org.company')}
                value={companyId}
                onChange={(value: string) => {
                  setPickedCompanyId(value);
                  setSelectedId(undefined);
                }}
                options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
                style={{ minWidth: 200 }}
              />
            )}
            <Segmented<ViewMode>
              value={view}
              onChange={setView}
              options={[
                { value: 'tree', label: t('web.hr.org.view_tree'), icon: <ApartmentOutlined /> },
                { value: 'chart', label: t('web.hr.org.view_chart'), icon: <ClusterOutlined /> },
              ]}
            />
            {canManage && companyId && (
              <Button type="primary" icon={<PlusOutlined />} onClick={() => setUnitDrawer({ open: true })}>
                {t('web.hr.org.new_unit')}
              </Button>
            )}
          </>
        }
      />
      {!companyId ? (
        <Card>
          <Empty description={t('web.hr.org.pick_company')} />
        </Card>
      ) : tree.isLoading ? (
        <Skeleton active />
      ) : units.length === 0 ? (
        <Card>
          <Empty description={t('web.hr.org.empty')} />
        </Card>
      ) : (
        <Flex vertical gap={16}>
          <Row gutter={[16, 16]}>
            <Col xs={12} md={6}>
              <Card size="small">
                <Statistic title={t('web.hr.org.stats.units')} value={units.length} />
              </Card>
            </Col>
            <Col xs={12} md={6}>
              <Card size="small">
                <Statistic title={t('web.hr.org.stats.norm')} value={sum.norm} />
              </Card>
            </Col>
            <Col xs={12} md={6}>
              <Card size="small">
                <Statistic title={t('web.hr.org.stats.filled')} value={sum.filled} />
              </Card>
            </Col>
            <Col xs={12} md={6}>
              <Card size="small">
                <Statistic title={t('web.hr.org.stats.vacant')} value={sum.vacant} />
              </Card>
            </Col>
          </Row>
          {view === 'tree' ? (
            <Row gutter={[16, 16]}>
              <Col xs={24} lg={9}>
                <Card>
                  <Input.Search
                    allowClear
                    placeholder={t('web.hr.org.search')}
                    onChange={(event) => setSearch(event.target.value)}
                    style={{ marginBottom: 12 }}
                  />
                  <Tree
                    // Remounted per search so the matching units show expanded
                    key={search}
                    blockNode
                    defaultExpandAll
                    treeData={treeData}
                    selectedKeys={selectedId ? [selectedId] : []}
                    onSelect={(keys) => setSelectedId(keys[0] as string | undefined)}
                  />
                </Card>
              </Col>
              <Col xs={24} lg={15}>
                {panel}
              </Col>
            </Row>
          ) : (
            <>
              <OrgChart units={units} selectedId={selectedId} onSelect={setSelectedId} />
              {panel}
            </>
          )}
        </Flex>
      )}
      {companyId && unitDrawer.open && (
        <OrgUnitFormDrawer
          open
          onClose={() => setUnitDrawer({ open: false })}
          companyId={companyId}
          units={units}
          unitId={unitDrawer.unitId}
          parentId={unitDrawer.parentId}
          onSaved={setSelectedId}
        />
      )}
      {positionDrawer.open && (
        <PositionFormDrawer
          open
          onClose={() => setPositionDrawer({ open: false })}
          positionId={positionDrawer.positionId}
          companyId={companyId}
          orgUnitId={positionDrawer.orgUnitId}
        />
      )}
      {moving && <MoveOrgUnitModal open onClose={() => setMoving(undefined)} unitId={moving} units={units} />}
    </>
  );
};

/**
 * Tree nodes; with a search only the matching units and the units leading to them remain. Names are compared in
 * the user's culture, codes (ASCII, culture independent) invariantly.
 */
const toTreeData = (nodes: UnitNode[], search: string, locale: string, t: TFunction): DataNode[] =>
  nodes.flatMap((node) => {
    const children = toTreeData(node.children, search, locale, t);
    const matches =
      !search ||
      node.unit.name.toLocaleLowerCase(locale).includes(search.toLocaleLowerCase(locale)) ||
      node.unit.code.toLowerCase().includes(search.toLowerCase());
    return matches || children.length > 0 ? [{ key: node.unit.id, title: unitTitle(node.unit, t), children }] : [];
  });

const unitTitle = (unit: OrgUnitTreeNode, t: TFunction): ReactNode => (
  <Flex gap={8} align="center" wrap>
    <span>{unit.name}</span>
    <Typography.Text type="secondary">{unit.code}</Typography.Text>
    {unit.vacant > 0 && <Tag color="orange">{t('web.hr.org.vacant_count', { count: unit.vacant })}</Tag>}
    {!unit.isActive && <Tag>{t('web.hr.org.inactive')}</Tag>}
  </Flex>
);
