import { theme } from 'antd';
import { BarChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { SVGRenderer } from 'echarts/renderers';
import ReactEChartsCore from 'echarts-for-react/lib/core';
import { useTranslation } from 'react-i18next';

echarts.use([BarChart, GridComponent, LegendComponent, TooltipComponent, SVGRenderer]);

export interface ChartSeries {
  name: string;
  data: (number | null)[];
  color?: string;
}

interface ReportChartProps {
  categories: string[];
  series: ChartSeries[];
  /** Bars from the left, categories top to bottom (a funnel); otherwise columns. */
  horizontal?: boolean;
  /** Stacks the series in one bar per category. */
  stacked?: boolean;
  height?: number;
}

const BAR_HEIGHT = 36;

/** A bar chart of a report in the theme's colors, numbers in the user's language. */
export const ReportChart = ({ categories, series, horizontal, stacked, height }: ReportChartProps) => {
  const { i18n } = useTranslation();
  const { token } = theme.useToken();
  const palette = [
    token.colorPrimary,
    token.colorSuccess,
    token.colorError,
    token.colorWarning,
    token.colorTextTertiary,
  ];
  const format = (value: number) => value.toLocaleString(i18n.language);
  const category = {
    type: 'category' as const,
    // The first category on top of a horizontal chart
    data: horizontal ? [...categories].reverse() : categories,
    axisLabel: { color: token.colorText, width: 160, overflow: 'truncate' as const },
    axisLine: { lineStyle: { color: token.colorBorder } },
    axisTick: { show: false },
  };
  const value = {
    type: 'value' as const,
    minInterval: 1,
    axisLabel: { color: token.colorTextSecondary },
    splitLine: { lineStyle: { color: token.colorBorderSecondary } },
  };

  return (
    <ReactEChartsCore
      echarts={echarts}
      opts={{ renderer: 'svg' }}
      notMerge
      style={{ height: height ?? (horizontal ? Math.max(categories.length * BAR_HEIGHT + 60, 160) : 280) }}
      option={{
        grid: { left: 8, right: 32, top: series.length > 1 ? 36 : 12, bottom: 8, containLabel: true },
        legend: series.length > 1 ? { top: 0, textStyle: { color: token.colorText } } : undefined,
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' }, valueFormatter: format },
        xAxis: horizontal ? value : category,
        yAxis: horizontal ? category : value,
        series: series.map((s, index) => ({
          type: 'bar',
          name: s.name,
          stack: stacked ? 'total' : undefined,
          data: horizontal ? [...s.data].reverse() : s.data,
          barMaxWidth: 24,
          itemStyle: { color: s.color ?? palette[index % palette.length] },
          label: stacked
            ? undefined
            : {
                show: true,
                position: horizontal ? 'right' : 'top',
                color: token.colorText,
                formatter: ({ value: v }: { value: number }) => format(v),
              },
        })),
      }}
    />
  );
};
