import { ArrowUpRight, Minus } from "lucide-react";
import { motion } from "framer-motion";
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
    <motion.article
      className={["metric-card", toneClass].join(" ")}
      layout
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: index * 0.04, type: "spring", stiffness: 280, damping: 28 }}
    >
      <div className="metric-card__topline">
        <span>{metric.label}</span>
        {metric.trend ? <ArrowUpRight aria-hidden="true" size={15} /> : <Minus aria-hidden="true" size={15} />}
      </div>
      <div className="metric-card__value">{metric.value}</div>
      <p>{metric.detail}</p>
    </motion.article>
  );
}
