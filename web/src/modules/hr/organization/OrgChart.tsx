import { CompressOutlined, ZoomInOutlined, ZoomOutOutlined } from '@ant-design/icons';
import { Avatar, Button, Flex, Tag, Tooltip, Typography, theme } from 'antd';
import { useCallback, useMemo, useRef, useState, type PointerEvent, type WheelEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { OrgUnitTreeNode } from '@/shared/api/generated/model';
import {
  CARD_HEIGHT,
  CARD_WIDTH,
  clamp,
  fitView,
  layoutOrgChart,
  linkPath,
  type ChartLayout,
  type View,
} from './chartLayout';
import { initials } from './unitTree';

interface OrgChartProps {
  units: OrgUnitTreeNode[];
  selectedId?: string;
  onSelect: (unitId: string) => void;
  height?: number;
}

/**
 * Organization chart of a company (PLAN H1b, D37): one card per unit with its manager position and staffing.
 * Drag to pan, wheel or the buttons to zoom. Initials stand in for photos until the employee card (H2).
 */
export const OrgChart = ({ units, selectedId, onSelect, height = 560 }: OrgChartProps) => {
  const { t, i18n } = useTranslation();
  const { token } = theme.useToken();
  const layout = useMemo(() => layoutOrgChart(units), [units]);
  const drag = useRef<{ x: number; y: number; moved: boolean } | null>(null);
  const [panning, setPanning] = useState(false);
  const [width, setWidth] = useState(0);
  // A view the user zoomed or panned to; it belongs to one layout, so new data shows the fitted chart again
  const [manual, setManual] = useState<{ layout: ChartLayout; view: View }>();
  const view = manual?.layout === layout ? manual.view : fitView(layout, width, height);
  const setView = (change: (current: View) => View) => setManual({ layout, view: change(view) });
  const fit = () => setManual(undefined);

  const observer = useRef<ResizeObserver | null>(null);
  const container = useCallback((element: HTMLDivElement | null) => {
    observer.current?.disconnect();
    observer.current = null;
    if (!element) {
      return;
    }

    setWidth(element.clientWidth);
    if (typeof ResizeObserver !== 'undefined') {
      observer.current = new ResizeObserver((entries) => setWidth(entries[0]?.contentRect.width ?? 0));
      observer.current.observe(element);
    }
  }, []);

  const zoom = (factor: number, originX?: number, originY?: number) =>
    setView((current) => {
      const scale = clamp(current.scale * factor);
      const x = originX ?? width / 2;
      const y = originY ?? height / 2;
      // Keep the point under the cursor in place
      return {
        scale,
        x: x - ((x - current.x) * scale) / current.scale,
        y: y - ((y - current.y) * scale) / current.scale,
      };
    });

  const onWheel = (event: WheelEvent<HTMLDivElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    zoom(event.deltaY < 0 ? 1.1 : 1 / 1.1, event.clientX - box.left, event.clientY - box.top);
  };

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    drag.current = { x: event.clientX, y: event.clientY, moved: false };
  };

  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const start = drag.current;
    if (!start) {
      return;
    }

    const dx = event.clientX - start.x;
    const dy = event.clientY - start.y;
    if (!start.moved && Math.abs(dx) + Math.abs(dy) < 4) {
      return;
    }

    drag.current = { x: event.clientX, y: event.clientY, moved: true };
    setPanning(true);
    setView((current) => ({ ...current, x: current.x + dx, y: current.y + dy }));
  };

  const onPointerUp = () => {
    // The click that follows a drag must not select a card, so the drag is forgotten after it
    window.setTimeout(() => (drag.current = null));
    setPanning(false);
  };

  return (
    <div
      ref={container}
      role="application"
      aria-label={t('web.hr.org.view_chart')}
      onWheel={onWheel}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerLeave={() => {
        drag.current = null;
        setPanning(false);
      }}
      style={{
        position: 'relative',
        height,
        overflow: 'hidden',
        cursor: panning ? 'grabbing' : 'grab',
        background: token.colorFillQuaternary,
        borderRadius: token.borderRadiusLG,
        border: `1px solid ${token.colorBorderSecondary}`,
        touchAction: 'none',
        userSelect: 'none',
      }}
    >
      <div
        style={{
          position: 'absolute',
          left: 0,
          top: 0,
          width: layout.width,
          height: layout.height,
          transform: `translate(${view.x}px, ${view.y}px) scale(${view.scale})`,
          transformOrigin: '0 0',
        }}
      >
        <svg width={layout.width} height={layout.height} style={{ position: 'absolute', left: 0, top: 0 }} aria-hidden>
          {layout.links.map((link) => (
            <path key={link.id} d={linkPath(link)} fill="none" stroke={token.colorBorder} strokeWidth={1.5} />
          ))}
        </svg>
        {layout.nodes.map(({ unit, x, y }) => {
          const selected = unit.id === selectedId;
          return (
            <div
              key={unit.id}
              role="button"
              tabIndex={0}
              aria-pressed={selected}
              aria-label={unit.name}
              onClick={() => {
                if (!drag.current?.moved) {
                  onSelect(unit.id);
                }
              }}
              onKeyDown={(event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  onSelect(unit.id);
                }
              }}
              style={{
                position: 'absolute',
                left: x,
                top: y,
                width: CARD_WIDTH,
                height: CARD_HEIGHT,
                boxSizing: 'border-box',
                padding: 10,
                background: token.colorBgContainer,
                borderRadius: token.borderRadiusLG,
                border: `${selected ? 2 : 1}px solid ${selected ? token.colorPrimary : token.colorBorderSecondary}`,
                boxShadow: token.boxShadowTertiary,
                opacity: unit.isActive ? 1 : 0.6,
                cursor: 'pointer',
              }}
            >
              <Flex gap={8} align="flex-start">
                <Avatar style={{ background: token.colorPrimaryBg, color: token.colorPrimary, flex: 'none' }}>
                  {initials(unit.name, i18n.language)}
                </Avatar>
                <div style={{ minWidth: 0 }}>
                  <Typography.Text strong ellipsis={{ tooltip: unit.name }} style={{ display: 'block' }}>
                    {unit.name}
                  </Typography.Text>
                  <Typography.Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
                    {t(`web.hr.org_unit_type.${unit.type}`)} · {unit.code}
                  </Typography.Text>
                </div>
              </Flex>
              <Typography.Text
                type={unit.managerJobName ? undefined : 'secondary'}
                ellipsis
                style={{ display: 'block', fontSize: token.fontSizeSM, marginTop: 4 }}
              >
                {unit.managerJobName ?? t('web.hr.org.fields.no_manager')}
              </Typography.Text>
              <Flex gap={4} style={{ marginTop: 2 }}>
                <Typography.Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
                  {t('web.hr.org.staffing', { norm: unit.norm, filled: unit.filled, vacant: unit.vacant })}
                </Typography.Text>
                {unit.vacant > 0 && (
                  <Tag color="orange" style={{ marginInlineEnd: 0, lineHeight: '18px' }}>
                    {unit.vacant}
                  </Tag>
                )}
              </Flex>
            </div>
          );
        })}
      </div>
      <Flex vertical gap={4} style={{ position: 'absolute', right: 12, top: 12 }}>
        <Tooltip title={t('web.hr.org.chart.zoom_in')} placement="left">
          <Button icon={<ZoomInOutlined />} aria-label={t('web.hr.org.chart.zoom_in')} onClick={() => zoom(1.2)} />
        </Tooltip>
        <Tooltip title={t('web.hr.org.chart.zoom_out')} placement="left">
          <Button
            icon={<ZoomOutOutlined />}
            aria-label={t('web.hr.org.chart.zoom_out')}
            onClick={() => zoom(1 / 1.2)}
          />
        </Tooltip>
        <Tooltip title={t('web.hr.org.chart.fit')} placement="left">
          <Button icon={<CompressOutlined />} aria-label={t('web.hr.org.chart.fit')} onClick={fit} />
        </Tooltip>
      </Flex>
      <Typography.Text
        type="secondary"
        style={{ position: 'absolute', left: 12, bottom: 8, fontSize: token.fontSizeSM }}
      >
        {t('web.hr.org.chart.hint')}
      </Typography.Text>
    </div>
  );
};
