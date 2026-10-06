import { HistoryOutlined } from '@ant-design/icons';
import { Button, Tooltip } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { platformPermissions } from '@/modules/platform/permissions';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { AuditHistoryDrawer, type AuditTarget } from './AuditHistoryDrawer';

/**
 * Opens the change history of a record. Shown only with platform.audit.read; the API also requires the Tenant
 * scope (D27). `compact` renders an icon button for table rows.
 */
export const AuditHistoryButton = ({
  title,
  compact,
  ...target
}: AuditTarget & { title?: string; compact?: boolean }) => {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const allowed = useHasPermission(platformPermissions.auditRead);
  if (!allowed) {
    return null;
  }

  return (
    <>
      {compact ? (
        <Tooltip title={t('web.audit.history')}>
          <Button
            type="text"
            icon={<HistoryOutlined />}
            aria-label={t('web.audit.history')}
            onClick={() => setOpen(true)}
          />
        </Tooltip>
      ) : (
        <Button icon={<HistoryOutlined />} onClick={() => setOpen(true)}>
          {t('web.audit.history')}
        </Button>
      )}
      <AuditHistoryDrawer target={target} title={title} open={open} onClose={() => setOpen(false)} />
    </>
  );
};
