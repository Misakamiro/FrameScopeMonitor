import type { ChartSeries } from "../types";
import { toneToClass } from "./tone";
import "./components.css";

interface ChartShellProps {
  title: string;
  description: string;
  series: ChartSeries[];
  compact?: boolean;
}

function linePath(points: number[], width: number, height: number) {
  const max = Math.max(...points);
  const min = Math.min(...points);
  const range = Math.max(max - min, 1);
  return points
    .map((point, index) => {
      const x = (index / Math.max(points.length - 1, 1)) * width;
      const y = height - ((point - min) / range) * (height - 18) - 9;
      return `${index === 0 ? "M" : "L"} ${x.toFixed(2)} ${y.toFixed(2)}`;
    })
    .join(" ");
}

export function ChartShell({ title, description, series, compact }: ChartShellProps) {
  const width = 420;
  const height = compact ? 116 : 178;

  return (
    <section className={["chart-shell", compact ? "chart-shell--compact" : ""].join(" ")}>
      <div className="chart-shell__header">
        <div>
          <h3>{title}</h3>
          <p>{description}</p>
        </div>
        <span className="chart-shell__tag">趋势预览</span>
      </div>
      <svg
        className="chart-shell__plot"
        viewBox={`0 0 ${width} ${height}`}
        role="img"
        aria-label={`${title} 静态图表预览`}
      >
        <defs>
          <linearGradient id={`${title}-fade`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="currentColor" stopOpacity="0.16" />
            <stop offset="100%" stopColor="currentColor" stopOpacity="0" />
          </linearGradient>
        </defs>
        {[0.25, 0.5, 0.75].map((ratio) => (
          <line
            key={ratio}
            x1="0"
            x2={width}
            y1={height * ratio}
            y2={height * ratio}
            className="chart-shell__grid"
          />
        ))}
        {series.map((item, index) => {
          const path = linePath(item.points, width, height);
          const className = toneToClass(item.tone);
          return (
            <g key={item.label} className={className} data-series-index={index}>
              <path d={`${path} L ${width} ${height} L 0 ${height} Z`} className="chart-shell__area" />
              <path d={path} className="chart-shell__line" />
            </g>
          );
        })}
      </svg>
      <div className="chart-shell__legend">
        {series.map((item) => (
          <span key={item.label}>
            <i className={toneToClass(item.tone)} />
            {item.label}
          </span>
        ))}
      </div>
    </section>
  );
}
