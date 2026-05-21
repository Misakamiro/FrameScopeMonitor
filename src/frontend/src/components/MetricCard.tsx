import { ArrowUpRight, Minus } from "lucide-react";
import type { Metric } from "../types";
import { toneToClass } from "./tone";
import "./components.css";

interface MetricCardProps {
  metric: Metric;
  index?: number;
}

export function MetricCard({ metric, index = 0 }: MetricCardProps) {
  const toneClass = toneToClass(metric.tone);
  return (
    <article
      className={["metric-card", toneClass].join(" ")}
      data-metric-index={index}
    >
      <div className="metric-card__topline">
        <span>{metric.label}</span>
        {metric.trend ? <ArrowUpRight aria-hidden="true" size={15} /> : <Minus aria-hidden="true" size={15} />}
      </div>
      <div className="metric-card__value">{metric.value}</div>
      <p>{metric.detail}</p>
    </article>
  );
}
