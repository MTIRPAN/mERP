import { EditOutlined, FilePdfOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  Card,
  Col,
  Empty,
  Flex,
  Row,
  Select,
  Skeleton,
  Space,
  Statistic,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getLeaveRecordDocument, useListLeaveBalances } from '@/shared/api/generated/endpoints/hr/hr';
import type {
  EmploymentItem,
  LeaveBalanceItem,
  LeaveLedgerItem,
  LeaveLedgerSource,
} from '@/shared/api/generated/model';
import { saveFile } from '@/shared/api/download';
import { isApiError } from '@/shared/api/problem';
import { formatDate, formatDateTime } from '@/shared/format/date';
import { mainEmployment } from '../../employees/employeeFormat';
import { AdjustBalanceDrawer } from './AdjustBalanceDrawer';

const sourceColors: Record<LeaveLedgerSource, string> = {
  Accrual: 'green',
  Request: 'blue',
  RequestCancel: 'cyan',
  Adjustment: 'orange',
  OpeningBalance: 'purple',
  Transfer: 'geekblue',
  Termination: 'default',
};

interface LeaveBalancesTabProps {
  employeeId: string;
  employments: EmploymentItem[];
  canManage: boolean;
}

/**
 * The leave balances of an employment on the employee card (PLAN L3, D43): balance, available, earned, used and
 * pending days, the next entitlement, and every movement with its reason.
 */
export const LeaveBalancesTab = ({ employeeId, employments, canManage }: LeaveBalancesTabProps) => {
  const { t } = useTranslation();
  const [employmentId, setEmploymentId] = useState(mainEmployment(employments)?.id);
  const [adjusting, setAdjusting] = useState(false);
  const balances = useListLeaveBalances(employeeId, employmentId ?? '', { query: { enabled: !!employmentId } });
  const { message } = App.useApp();
  const [printing, setPrinting] = useState(false);

  /** The annual leave record of the employment as PDF (D46). */
  const printRecord = async () => {
    const registry = employments.find((e) => e.id === employmentId)?.registryNumber ?? '';
    setPrinting(true);
    try {
      await saveFile(
        getLeaveRecordDocument(employeeId, employmentId ?? ''),
        t('web.hr.leave_balances.record_file', { registry }),
      );
    } catch (error) {
      void message.error(isApiError(error) ? error.message : String(error));
    } finally {
      setPrinting(false);
    }
  };

  if (!employmentId) {
    return <Empty />;
  }

  const items = balances.data ?? [];
  return (
    <Flex vertical gap={16}>
      <Flex justify="space-between" gap={8} wrap>
        {employments.length > 1 ? (
          <Select
            aria-label={t('web.hr.leave_balances.employment')}
            value={employmentId}
            onChange={setEmploymentId}
            options={employments.map((e) => ({ value: e.id, label: `${e.companyName} · ${e.registryNumber}` }))}
            style={{ minWidth: 260 }}
          />
        ) : (
          <span />
        )}
        <Space wrap>
          <Button icon={<FilePdfOutlined />} loading={printing} onClick={() => void printRecord()}>
            {t('web.hr.leave_balances.record')}
          </Button>
          {canManage && items.some((b) => !b.closedOn) && (
            <Button icon={<EditOutlined />} onClick={() => setAdjusting(true)}>
              {t('web.hr.leave_balances.adjust')}
            </Button>
          )}
        </Space>
      </Flex>
      {balances.isLoading ? (
        <Skeleton active />
      ) : items.length === 0 ? (
        <Empty description={t('web.hr.leave_balances.none')} />
      ) : (
        items.map((balance) => <BalanceCard key={balance.leaveTypeCode} balance={balance} />)
      )}
      {adjusting && (
        <AdjustBalanceDrawer
          employeeId={employeeId}
          employmentId={employmentId}
          balances={items}
          onClose={() => setAdjusting(false)}
        />
      )}
    </Flex>
  );
};

/** One balance: its figures, the next entitlement and the movements. */
export const BalanceCard = ({ balance }: { balance: LeaveBalanceItem }) => {
  const { t, i18n } = useTranslation();
  const days = (value: number) => t('web.hr.leave_balances.days_value', { days: value.toLocaleString(i18n.language) });
  return (
    <Card
      size="small"
      title={balance.leaveTypeName}
      extra={
        balance.closedOn ? (
          <Tag>{t('web.hr.leave_balances.closed_on', { date: formatDate(balance.closedOn, i18n.language) })}</Tag>
        ) : undefined
      }
    >
      <Row gutter={[16, 16]}>
        <Col xs={12} md={6}>
          <Statistic
            title={t('web.hr.leave_balances.available')}
            value={balance.available}
            precision={balance.available % 1 ? 2 : 0}
          />
        </Col>
        <Col xs={12} md={6}>
          <Statistic
            title={t('web.hr.leave_balances.earned')}
            value={balance.earned}
            precision={balance.earned % 1 ? 2 : 0}
          />
        </Col>
        <Col xs={12} md={6}>
          <Statistic
            title={t('web.hr.leave_balances.used')}
            value={balance.used}
            precision={balance.used % 1 ? 2 : 0}
          />
        </Col>
        <Col xs={12} md={6}>
          <Statistic
            title={t('web.hr.leave_balances.pending')}
            value={balance.pending}
            precision={balance.pending % 1 ? 2 : 0}
          />
        </Col>
      </Row>
      {balance.nextAccrual && (
        <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
          {t('web.hr.leave_balances.next_accrual', {
            date: formatDate(balance.nextAccrual.date, i18n.language),
            days: days(balance.nextAccrual.days),
          })}
        </Typography.Paragraph>
      )}
      <Table<LeaveLedgerItem>
        size="small"
        style={{ marginTop: 12 }}
        rowKey={(entry) => `${entry.date}-${entry.recordedAt}-${entry.source}`}
        dataSource={balance.entries}
        pagination={balance.entries.length > 10 ? { pageSize: 10, showSizeChanger: false } : false}
        locale={{
          emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.hr.leave_balances.no_entries')} />,
        }}
        columns={[
          {
            key: 'date',
            title: t('web.hr.leave_balances.date'),
            width: 130,
            render: (_, entry) => formatDate(entry.date, i18n.language),
          },
          {
            key: 'source',
            title: t('web.hr.leave_balances.source'),
            render: (_, entry) => (
              <Tag color={sourceColors[entry.source]}>{t(`web.hr.leave_balances.sources.${entry.source}`)}</Tag>
            ),
          },
          {
            key: 'quantity',
            title: t('web.hr.leave_balances.quantity'),
            align: 'right',
            width: 110,
            render: (_, entry) => (
              <Typography.Text type={entry.quantity < 0 ? 'danger' : 'success'}>
                {entry.quantity > 0 ? '+' : ''}
                {entry.quantity.toLocaleString(i18n.language)}
              </Typography.Text>
            ),
          },
          {
            key: 'note',
            title: t('web.hr.leave_balances.note'),
            render: (_, entry) => entry.note ?? entry.periodKey ?? '',
          },
          {
            key: 'recordedAt',
            title: t('web.hr.leave_balances.recorded_at'),
            width: 170,
            render: (_, entry) => formatDateTime(entry.recordedAt, i18n.language),
          },
        ]}
      />
    </Card>
  );
};
