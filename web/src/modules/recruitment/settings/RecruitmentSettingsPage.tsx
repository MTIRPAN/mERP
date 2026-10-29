import { Card, List, Tabs, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { platformPermissions } from '@/modules/platform/permissions';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { recruitmentPermissions } from '../permissions';
import { ConsentTextsTab } from './ConsentTextsTab';
import { EvaluationTemplatesTab } from './EvaluationTemplatesTab';
import { LookupsTab } from './LookupsTab';
import { StageTemplatesTab } from './StageTemplatesTab';

const tabs = ['stages', 'evaluations', 'lists', 'consents', 'emails'] as const;

type Tab = (typeof tabs)[number];

const emailTypes = [
  'application_received',
  'interview_invitation',
  'interview_updated',
  'interview_cancelled',
  'interview_reminder',
  'application_rejected',
  'offer_sent',
];

/** Where the e-mails candidates get and the retention are set: Platform's screens (D47). */
const EmailsTab = () => {
  const { t } = useTranslation();
  const canEditTemplates = useHasPermission(platformPermissions.notificationTemplatesRead);
  const canEditSettings = useHasPermission(platformPermissions.settingsManage);
  return (
    <Card size="small">
      <Typography.Paragraph>{t('web.recruitment.settings.emails.intro')}</Typography.Paragraph>
      <List
        size="small"
        dataSource={emailTypes}
        renderItem={(type) => <List.Item>{t(`web.recruitment.settings.emails.types.${type}`)}</List.Item>}
        style={{ marginBottom: 16 }}
      />
      <Typography.Paragraph>
        {canEditTemplates ? (
          <Link to="/system/notification-templates">{t('web.recruitment.settings.emails.templates_link')}</Link>
        ) : (
          t('web.recruitment.settings.emails.templates_hint')
        )}
      </Typography.Paragraph>
      <Typography.Paragraph>
        {t('web.recruitment.settings.emails.retention')}{' '}
        {canEditSettings && <Link to="/system/settings">{t('web.recruitment.settings.emails.settings_link')}</Link>}
      </Typography.Paragraph>
    </Card>
  );
};

/**
 * Recruitment settings (PLAN R1, D47): stage templates, evaluation templates (R5), reasons, sources and tags, the KVKK texts, and where the
 * candidate e-mails and the retention are set. The open tab is kept in the address (<c>?tab=</c>).
 */
export const RecruitmentSettingsPage = () => {
  const { t } = useTranslation();
  const canManage = useHasPermission(recruitmentPermissions.settingsManage);
  const [params, setParams] = useSearchParams();
  const requested = params.get('tab') as Tab | null;
  const tab: Tab = requested && tabs.includes(requested) ? requested : 'stages';

  return (
    <>
      <PageHeader title={t('web.recruitment.settings.title')} subtitle={t('web.recruitment.settings.subtitle')} />
      <Tabs
        activeKey={tab}
        onChange={(key) => setParams({ tab: key }, { replace: true })}
        items={[
          {
            key: 'stages',
            label: t('web.recruitment.settings.tabs.stages'),
            children: <StageTemplatesTab canManage={canManage} />,
          },
          {
            key: 'evaluations',
            label: t('web.recruitment.settings.tabs.evaluations'),
            children: <EvaluationTemplatesTab canManage={canManage} />,
          },
          {
            key: 'lists',
            label: t('web.recruitment.settings.tabs.lists'),
            children: <LookupsTab canManage={canManage} />,
          },
          {
            key: 'consents',
            label: t('web.recruitment.settings.tabs.consents'),
            children: <ConsentTextsTab canManage={canManage} />,
          },
          { key: 'emails', label: t('web.recruitment.settings.tabs.emails'), children: <EmailsTab /> },
        ]}
      />
    </>
  );
};
