import { Card, Tabs } from 'antd';
import { useTranslation } from 'react-i18next';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { useCardTab } from '../employees/useCardTab';
import { hrPermissions } from '../permissions';
import { PolicyCheckTab } from './check/PolicyCheckTab';
import { LeavePoliciesTab } from './policies/LeavePoliciesTab';
import { PolicyRulesTab } from './rules/PolicyRulesTab';
import { LeaveTypesTab } from './types/LeaveTypesTab';

const TABS = ['types', 'policies', 'rules', 'check'];

/**
 * Leave settings (PLAN L1, D41): the leave types, the annual leave policies, the assignment rules of each company and
 * a tool that tells which policy applies to an employee. Changes need hr.leave.settings.manage.
 */
export const LeaveSettingsPage = () => {
  const { t } = useTranslation();
  const canManage = useHasPermission(hrPermissions.leaveSettingsManage);
  const [tab, selectTab] = useCardTab(TABS);

  return (
    <>
      <PageHeader title={t('web.hr.leave.title')} subtitle={t('web.hr.leave.subtitle')} />
      <Card>
        <Tabs
          activeKey={tab}
          onChange={selectTab}
          destroyOnHidden
          items={[
            { key: 'types', label: t('web.hr.leave.tabs.types'), children: <LeaveTypesTab canManage={canManage} /> },
            {
              key: 'policies',
              label: t('web.hr.leave.tabs.policies'),
              children: <LeavePoliciesTab canManage={canManage} />,
            },
            { key: 'rules', label: t('web.hr.leave.tabs.rules'), children: <PolicyRulesTab canManage={canManage} /> },
            { key: 'check', label: t('web.hr.leave.tabs.check'), children: <PolicyCheckTab canManage={canManage} /> },
          ]}
        />
      </Card>
    </>
  );
};
