import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core';
import { Badge, Card, Empty, Flex, Space, Tag, Typography } from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { ApplicationListItem, PipelineStage } from '@/shared/api/generated/model';
import { formatMomentDate } from '@/shared/format/date';
import { stageTypeColors } from '../settings/stages';
import { daysInStage, isOverdue } from './pipeline';

interface CardProps {
  application: ApplicationListItem;
  draggable: boolean;
  onOpen: (id: string) => void;
}

const ApplicationCard = ({ application, draggable, onOpen }: CardProps) => {
  const { t, i18n } = useTranslation();
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: application.id,
    disabled: !draggable,
    data: { application },
  });
  const overdue = isOverdue(application);

  return (
    <div
      ref={setNodeRef}
      {...listeners}
      {...attributes}
      className="merp-kanban-card"
      style={{
        transform: transform ? `translate3d(${transform.x}px, ${transform.y}px, 0)` : undefined,
        opacity: isDragging ? 0.6 : 1,
        cursor: draggable ? 'grab' : 'pointer',
      }}
    >
      <Card size="small" hoverable onClick={() => onOpen(application.id)}>
        <Typography.Text strong>{application.candidateName}</Typography.Text>
        <Flex vertical gap={4} style={{ marginTop: 4 }}>
          <Typography.Text type="secondary">
            {t('web.recruitment.pipeline.applied_on', { date: formatMomentDate(application.appliedAt, i18n.language) })}
          </Typography.Text>
          {application.status === 'Active' && (
            <Typography.Text type={overdue ? 'danger' : 'secondary'}>
              {t(overdue ? 'web.recruitment.pipeline.overdue' : 'web.recruitment.pipeline.days_in_stage', {
                days: daysInStage(application),
              })}
            </Typography.Text>
          )}
          <Space size={[4, 4]} wrap>
            {application.isInternal && <Tag color="blue">{t('web.recruitment.pipeline.internal')}</Tag>}
            {application.knockedOut && <Tag color="warning">{t('web.recruitment.pipeline.knocked_out')}</Tag>}
            {application.hireRequestedAt && <Tag color="green">{t('web.recruitment.pipeline.hire_pending')}</Tag>}
          </Space>
        </Flex>
      </Card>
    </div>
  );
};

const StageColumn = ({ stage, children }: { stage: PipelineStage; children: ReactNode }) => {
  const { t } = useTranslation();
  const { setNodeRef, isOver } = useDroppable({ id: stage.id, data: { stage } });

  return (
    <div ref={setNodeRef} className={`merp-kanban-column${isOver ? ' merp-kanban-column-over' : ''}`}>
      <Flex justify="space-between" align="center" className="merp-kanban-column-header">
        <Space size={6}>
          <Badge color={stageTypeColors[stage.type]} />
          <Typography.Text strong>{stage.name}</Typography.Text>
        </Space>
        <Space size={6}>
          {stage.slaDays && (
            <Typography.Text type="secondary">
              {t('web.recruitment.settings.stage_templates.days', { days: stage.slaDays })}
            </Typography.Text>
          )}
          <Tag>{stage.count}</Tag>
        </Space>
      </Flex>
      <Flex vertical gap={8}>
        {children}
      </Flex>
    </div>
  );
};

interface KanbanBoardProps {
  stages: PipelineStage[];
  applications: ApplicationListItem[];
  /** Cards are dragged only by those who manage applications, and only while active. */
  canManage: boolean;
  onOpen: (id: string) => void;
  onDrop: (application: ApplicationListItem, stage: PipelineStage) => void;
}

/**
 * The pipeline of an opening as a kanban (D50): a column per stage; an active application is dragged to another
 * stage. The keyboard drags too (space, arrows, space).
 */
export const KanbanBoard = ({ stages, applications, canManage, onOpen, onDrop }: KanbanBoardProps) => {
  const { t } = useTranslation();
  // A click opens the card; dragging starts after a few pixels
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor),
  );

  const dropped = ({ active, over }: DragEndEvent) => {
    const application = active.data.current?.application as ApplicationListItem | undefined;
    const stage = over?.data.current?.stage as PipelineStage | undefined;
    if (application && stage && stage.id !== application.stageId) {
      onDrop(application, stage);
    }
  };

  return (
    <DndContext sensors={sensors} onDragEnd={dropped}>
      <div className="merp-kanban">
        {stages.map((stage) => {
          const cards = applications.filter((a) => a.stageId === stage.id);
          return (
            <StageColumn key={stage.id} stage={stage}>
              {cards.length === 0 ? (
                <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.recruitment.pipeline.empty_stage')} />
              ) : (
                cards.map((application) => (
                  <ApplicationCard
                    key={application.id}
                    application={application}
                    draggable={canManage && application.status === 'Active'}
                    onOpen={onOpen}
                  />
                ))
              )}
            </StageColumn>
          );
        })}
      </div>
    </DndContext>
  );
};
