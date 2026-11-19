import { createRoot } from "react-dom/client";
import { ReportView } from "./ReportView";
import { startMotionEnvironment } from '../lib/motion-preferences';
startMotionEnvironment();
const savePng = async (url: string, name: string) => {
  const a = document.createElement("a");
  a.href = url;
  a.download = name;
  a.click();
};
const data = (window as unknown as { FRAMESCOPE_DATA: Record<string, unknown> })
  .FRAMESCOPE_DATA;
createRoot(document.getElementById("root")!).render(
  <div className="report-root">
    {data ? (
      <ReportView
        data={data}
        savePng={savePng}
        onError={(message) => {
          document.getElementById("report-error")!.textContent = message;
        }}
      />
    ) : (
      <section className="report-status">
        <h1>报告数据无法读取</h1>
        <p>请确认网页包完整，或在 FrameScope 中重新生成并导出报告。</p>
      </section>
    )}
    <div id="report-error" role="alert" />
  </div>,
);
