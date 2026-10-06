import { Alert, Drawer, Empty, Flex, Skeleton, Tag, Timeline, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useSearchAudit } from '@/shared/api/generated/endpoints/platform/platform';
import { formatDateTime } from '@/shared/format/date';
import { actionColors, displayValue, groupOperations, type AuditOperation } from './auditView';

/** The drawer shows the newest changes; older ones are on the audit log page. */
const HISTORY_SIZE = 200;

export interface AuditTarget {
  /** Module (schema) of the entity, e.g. "platform". */
  module: string;
  /** Entity type name, e.g. "Company". */
  entityType: string;
  entityId: string;
}

const OperationChanges = ({ operation }: { operation: AuditOperation }) => {
  const { t } = useTranslation();
  if (operation.action === 'Delete') {
    return <Typography.Text type="secondary">{t('web.audit.deleted')}</Typography.Text>;
  }

  if (operation.action === 'Read') {
    return (
      <Typography.Text type="secondary">
        {t('web.audit.read_fields', { fields: operation.changes.map((c) => c.label ?? c.field).join(', ') })}
      </Typography.Text>
    );
  }

  return (
    <Flex vertical gap={2}>
      {operation.changes.map((change) => (
        <Typography.Text key={change.id} style={{ fontSize: 13 }}>
          <Typography.Text type="secondary">{change.label ?? change.field}: </Typography.Text>
          {operation.action === 'Update' && (
            <>
              <Typography.Text delete type="secondary">
                {displayValue(change.oldValue, t)}
              </Typography.Text>
              {' → '}
            </>
          )}
          <span>{displayValue(change.newValue, t)}</span>
        </Typography.Text>
      ))}
    </Flex>
  );
};

/** Change history of one record (PLAN P3): who changed which field when, with the old and new value. */
export const AuditHistoryDrawer = ({
  target,
  title,
  open,
  onClose,
}: {
  target: AuditTarget;
  title?: string;
  open: boolean;
  onClose: () => void;
}) => {
  const { t, i18n } = useTranslation();
  const history = useSearchAudit({ ...target, pageSize: HISTORY_SIZE }, { query: { enabled: open } });
  const operations = groupOperations(history.data?.items ?? []);

  return (
    <Drawer title={title ?? t('web.audit.history')} open={open} onClose={onClose} size={560} destroyOnHidden>
      {!history.data ? (
        <Skeleton active />
      ) : operations.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.audit.empty')} />
      ) : (
        <>
          {history.data.totalCount > history.data.items.length && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 16 }}
              title={t('web.audit.history_truncated', { count: history.data.items.length })}
              description={<Link to="/system/audit">{t('web.audit.open_log')}</Link>}
            />
          )}
          <Timeline
            items={operations.map((operation) => ({
              key: operation.key,
              color: actionColors[operation.action] === 'error' ? 'red' : 'blue',
              content: (
                <Flex vertical gap={4}>
                  <Flex gap={8} align="center" wrap>
                    <Tag color={actionColors[operation.action]} variant="filled">
                      {t(`web.audit.action.${operation.action}`)}
                    </Tag>
                    <Typography.Text strong>{operation.userName ?? t('web.audit.system_user')}</Typography.Text>
                    <Typography.Text type="secondary">
                      {formatDateTime(operation.occurredAt, i18n.language)}
                    </Typography.Text>
                  </Flex>
                  <OperationChanges operation={operation} />
                </Flex>
              ),
            }))}
          />
        </>
      )}
    </Drawer>
  );
};
