import { FileText } from "lucide-react";
import type { ReportPreview } from "../types";
import { StatusPill } from "./StatusPill";
import "./components.css";

interface ReportRowProps {
  report: ReportPreview;
}

export function ReportRow({ report }: ReportRowProps) {
  const tone =
    report.status === "ready" ? "success" : report.status === "generating" ? "warning" : "danger";
  const status = report.status === "ready" ? "可查看" : report.status === "generating" ? "生成中" : "失败";

  return (
    <article className="report-row">
      <div className="report-row__icon">
        <FileText aria-hidden="true" size={18} />
      </div>
      <div className="report-row__body">
        <div>
          <strong>{report.game}</strong>
          <StatusPill tone={tone}>{status}</StatusPill>
        </div>
        <p>{report.path}</p>
      </div>
      <div className="report-row__meta">
        <span>{report.fps}</span>
        <small>{report.timestamp}</small>
      </div>
    </article>
  );
}
