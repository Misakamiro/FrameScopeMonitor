import { ShieldAlert } from "lucide-react";
import type { ProcessPreview } from "../types";
import { StatusPill } from "./StatusPill";
import "./components.css";

interface ProcessRowProps {
  process: ProcessPreview;
}

export function ProcessRow({ process }: ProcessRowProps) {
  const statusTone =
    process.status === "watching"
      ? "primary"
      : process.status === "running"
        ? "success"
        : process.status === "blocked"
          ? "warning"
          : "neutral";

  return (
    <div className="process-row">
      <div className="process-row__identity">
        <span className="process-row__icon">
          {process.status === "blocked" ? <ShieldAlert aria-hidden="true" size={15} /> : null}
        </span>
        <div>
          <strong>{process.name}</strong>
          <small>PID {process.pid || "未连接"}</small>
        </div>
      </div>
      <span>{process.cpu.toFixed(1)}%</span>
      <span>{process.memory}</span>
      <span>{process.io}</span>
      <StatusPill tone={statusTone}>{statusText(process.status)}</StatusPill>
    </div>
  );
}

function statusText(status: ProcessPreview["status"]) {
  switch (status) {
    case "watching":
      return "监听中";
    case "running":
      return "运行";
    case "blocked":
      return "受限";
    case "idle":
    default:
      return "待发现";
  }
}
