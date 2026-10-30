import { useQueryClient } from '@tanstack/react-query';
import { App, Button, Input, Modal, Select, Space, Table, Tag, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { cancelPreHire, getListPreHiresQueryKey, useListPreHires } from '@/shared/api/generated/endpoints/hr/hr';
import type { PreHireItem, PreHireStatus } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDate, formatDateTime } from '@/shared/format/date';
import { HireWizard } from '../employees/HireWizard';
import { ChangeAssignmentDrawer } from '../employees/forms/ChangeAssignmentDrawer';
import { RehireDrawer } from '../employees/forms/RehireDrawer';
import { PersonPicker } from './PersonPicker';
import { completionOf, preHireStatusColors, preHireStatuses } from './preHires';

/**
 * İşe giriş ön kayıtları (PLAN R7, D54): candidates Recruitment hires, waiting for HR. HR completes each with the hire
 * form (a new person), the rehire form (a former or other-company employee) or the assignment change form (an employee
 * of the company); Recruitment then hires the application and fills the seat. A pre-hire may be called off.
 */
export const PreHiresPage = () => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<PreHireStatus[]>(['Pending']);
  const [page, setPage] = useState({ page: 1, pageSize: 20 });
  const [completing, setCompleting] = useState<PreHireItem>();
  const [picking, setPicking] = useState<PreHireItem>();
  const [cancelling, setCancelling] = useState<PreHireItem>();
  const [reason, setReason] = useState('');
  const list = useListPreHires({ ...page, status });
  const dash = '—';

  const cancel = async () => {
    if (!cancelling) {
      return;
    }

    try {
      await cancelPreHire(cancelling.id, { reason: reason.trim() || null });
      void message.success(t('web.hr.pre_hires.cancelled'));
      await queryClient.invalidateQueries({ queryKey: getListPreHiresQueryKey() });
      setCancelling(undefined);
      setReason('');
    } catch (error) {
      void message.error(isApiError(error) ? error.message : String(error));
    }
  };

  const done = () => void message.success(t('web.hr.pre_hires.completed'));
  const mode = completing ? completionOf(completing) : undefined;

  return (
    <>
      <PageHeader title={t('web.hr.pre_hires.title')} subtitle={t('web.hr.pre_hires.subtitle')} />
      <Space style={{ marginBottom: 12 }}>
        <Select
          aria-label={t('web.hr.pre_hires.fields.status')}
          mode="multiple"
          allowClear
          placeholder={t('web.hr.pre_hires.all_statuses')}
          value={status}
          onChange={(value: PreHireStatus[]) => {
            setStatus(value);
            setPage((p) => ({ ...p, page: 1 }));
          }}
          options={preHireStatuses.map((s) => ({ value: s, label: t(`web.hr.pre_hires.status.${s}`) }))}
          style={{ minWidth: 260 }}
        />
      </Space>
      <Table<PreHireItem>
        rowKey="id"
        size="small"
        loading={list.isLoading}
        dataSource={list.data?.items ?? []}
        pagination={{
          current: page.page,
          pageSize: page.pageSize,
          total: list.data?.totalCount ?? 0,
          onChange: (current, pageSize) => setPage({ page: current, pageSize }),
        }}
        columns={[
          {
            key: 'name',
            title: t('web.hr.pre_hires.fields.candidate'),
            render: (_, row) => (
              <Space direction="vertical" size={0}>
                <Typography.Text strong>{`${row.firstName} ${row.lastName}`}</Typography.Text>
                <Typography.Text type="secondary">{row.email}</Typography.Text>
              </Space>
            ),
          },
          {
            key: 'position',
            title: t('web.hr.pre_hires.fields.position'),
            render: (_, row) => (
              <Space direction="vertical" size={0}>
                {row.positionTitle}
                <Typography.Text type="secondary">
                  {[row.companyName, row.orgUnitName].filter(Boolean).join(' · ') || dash}
                </Typography.Text>
              </Space>
            ),
          },
          {
            key: 'start',
            title: t('web.hr.pre_hires.fields.start_date'),
            render: (_, row) => formatDate(row.startDate, i18n.language),
          },
          {
            key: 'person',
            title: t('web.hr.pre_hires.fields.person'),
            render: (_, row) =>
              row.existingEmployeeId ? (
                <Link to={`/hr/employees/${row.existingEmployeeId}`}>{row.existingEmployeeName ?? dash}</Link>
              ) : (
                <Typography.Text type="secondary">{t('web.hr.pre_hires.new_person')}</Typography.Text>
              ),
          },
          {
            key: 'status',
            title: t('web.hr.pre_hires.fields.status'),
            render: (_, row) => (
              <Space direction="vertical" size={0}>
                <Tag color={preHireStatusColors[row.status]}>{t(`web.hr.pre_hires.status.${row.status}`)}</Tag>
                {row.completedAt && (
                  <Typography.Text type="secondary">{formatDateTime(row.completedAt, i18n.language)}</Typography.Text>
                )}
                {row.cancelReason && <Typography.Text type="secondary">{row.cancelReason}</Typography.Text>}
              </Space>
            ),
          },
          {
            key: 'actions',
            title: '',
            align: 'right',
            render: (_, row) =>
              row.status === 'Pending' ? (
                <Space>
                  {!row.existingEmployeeId && (
                    <Button size="small" onClick={() => setPicking(row)}>
                      {t('web.hr.pre_hires.pick_person')}
                    </Button>
                  )}
                  <Button size="small" danger onClick={() => setCancelling(row)}>
                    {t('web.hr.pre_hires.cancel')}
                  </Button>
                  <Button size="small" type="primary" onClick={() => setCompleting(row)}>
                    {t(`web.hr.pre_hires.complete.${completionOf(row)}`)}
                  </Button>
                </Space>
              ) : row.employeeId ? (
                <Link to={`/hr/employees/${row.employeeId}`}>{t('web.hr.pre_hires.open_employee')}</Link>
              ) : null,
          },
        ]}
      />
      {completing && mode === 'hire' && (
        <HireWizard open preHire={completing} onHired={done} onClose={() => setCompleting(undefined)} />
      )}
      {completing && mode === 'rehire' && completing.existingEmployeeId && (
        <RehireDrawer
          open
          employeeId={completing.existingEmployeeId}
          openCompanyIds={[]}
          preHire={completing}
          onClose={() => setCompleting(undefined)}
        />
      )}
      {completing && mode === 'assignment' && completing.existingEmployeeId && completing.openEmploymentId && (
        <ChangeAssignmentDrawer
          open
          employeeId={completing.existingEmployeeId}
          employment={{
            id: completing.openEmploymentId,
            companyId: completing.companyId,
            companyName: completing.companyName ?? '',
          }}
          preHire={completing}
          onClose={() => setCompleting(undefined)}
        />
      )}
      {picking && (
        <PersonPicker
          preHire={picking}
          onClose={() => setPicking(undefined)}
          onPick={(forPerson) => {
            setPicking(undefined);
            setCompleting(forPerson);
          }}
        />
      )}
      <Modal
        title={t('web.hr.pre_hires.cancel')}
        open={!!cancelling}
        okText={t('web.hr.pre_hires.cancel')}
        okButtonProps={{ danger: true }}
        cancelText={t('web.common.cancel')}
        onCancel={() => setCancelling(undefined)}
        onOk={() => void cancel()}
      >
        <Typography.Paragraph type="secondary">{t('web.hr.pre_hires.cancel_hint')}</Typography.Paragraph>
        <Input.TextArea
          aria-label={t('web.hr.pre_hires.fields.cancel_reason')}
          placeholder={t('web.hr.pre_hires.fields.cancel_reason')}
          rows={3}
          maxLength={500}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
      </Modal>
    </>
  );
};
