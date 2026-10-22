import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Box, Heading, Text, HStack, VStack, Input, Button, Badge, SimpleGrid, Spinner } from "@chakra-ui/react";
import { ExternalLink, RefreshCw, FileSearch, FolderOpen, Trash2 } from "lucide-react";
import { invoke } from "@tauri-apps/api/core";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { CustomSelect } from "@/components/special/custom-select";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { usePreviewState, usePreviewAction, PreviewDialog, ActionError, formatMetric, useFloatingFieldStyle } from "./shared";
import { isDesktop, backendInvoke } from "@/preview/runtime/platform";
import { refreshDesktopReports } from "@/preview/runtime/state";
const labels = { full: "完整", partial: "部分数据", diagnostic: "诊断", error: "失败" };
const colors = { full: "green", partial: "yellow", diagnostic: "blue", error: "red" };
const artifactLabels: Record<string, string> = {
  complete: "产物完整", "legacy-readable": "历史兼容，建议重新生成",
  incomplete: "产物不完整", "inputs-changed": "输入已变化，需重新生成",
};
export default function ReportsPage() {
  const state = usePreviewState();
  const color = useAdaptiveTextColor();
  const [params] = useSearchParams();
  const [selected, setSelected] = useState<string | null>(params.get("id"));
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState("all");
  const [openError, setOpenError] = useState("");
  const [directory, setDirectory] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState(false);
  const { run, pending, error, clearError } = usePreviewAction();
  const field = useFloatingFieldStyle();
  // Real backend reports arrive through the runtime sync; refresh on mount so
  // a completed capture (or backend-side run) shows up without a restart.
  useEffect(() => {
    if (isDesktop()) void refreshDesktopReports();
  }, []);
  useEffect(() => { if (params.get("id")) setSelected(params.get("id")); }, [params]);
  const reports = state.reports.filter(r => (kind === "all" || r.kind === kind) && r.targetName.toLowerCase().includes(query.toLowerCase()));
  const report = state.reports.find(r => r.id === selected);
  const deleteBlocked = report?.operation === "regenerating" || report?.operation === "diagnosing"
    || (state.monitor.reportId === report?.id && ["waiting", "capturing", "stopping", "generating"].includes(state.monitor.phase));
  const deleteReport = async () => {
    if (!report || pending || deleteBlocked) return;
    const deleted = await run("report.delete", { id: report.id });
    if (deleted) { setDeleteConfirm(false); setDirectory(false); setSelected(null); }
  };
  const open = async () => {
    try {
      setOpenError("");
      if (isDesktop() && report?.runDir) await backendInvoke("report_open", { id: report.id });
      else await invoke("open_report_sample");
    } catch (cause) { setOpenError(String(cause)); }
  };
  const openDirectory = async () => {
    try {
      setOpenError("");
      if (isDesktop() && report?.runDir) await backendInvoke("report_open_directory", { id: report.id });
      else setDirectory(true);
    } catch (cause) { setOpenError(String(cause)); }
  };
  return <Box id="search-reports" pt={8}>
    <Heading size="lg" mb={6} color={color.text} textShadow={color.shadow}>报告</Heading>
    <HStack spacing={3} mb={5}>
      <Input aria-label="搜索报告" placeholder="搜索游戏报告" value={query} onChange={e => setQuery(e.target.value)} maxW="360px" sx={field.sx} />
      <CustomSelect ariaLabel="报告类型" width="160px" value={kind} onChange={setKind}
        options={[{ value: "all", label: "全部类型" }, ...Object.entries(labels).map(([value, label]) => ({ value, label }))]} />
    </HStack>
    <VStack spacing={3} align="stretch">
      {reports.map(item => <LiquidGlassCard key={item.id} p={5} borderRadius="xl">
        <HStack justify="space-between" spacing={4}>
          <Box flex={1} minW={0}><HStack mb={2}><Text fontWeight="bold" noOfLines={1}>{item.targetName}</Text>
            <Badge colorScheme={colors[item.kind]}>{labels[item.kind]}</Badge>{item.simulated ? <Badge colorScheme="purple">模拟</Badge> : <Badge>{artifactLabels[item.artifactStatus ?? "incomplete"] ?? "产物不完整"}</Badge>}</HStack>
            <Text fontSize="sm" opacity={.7}>{new Date(item.createdAt).toLocaleString("zh-CN")} · {item.durationSeconds == null ? "时长不可用" : `${item.durationSeconds} 秒`} · {formatMetric(item.averageFps, " FPS")}</Text></Box>
          {item.operation && item.operation !== "ready" && <Spinner size="sm" />}
          <Button size="sm" variant="outline" onClick={() => { setSelected(item.id); setOpenError(""); }}>查看详情</Button>
        </HStack>
      </LiquidGlassCard>)}
      {!reports.length && <Text py={12} textAlign="center" color={color.text}>暂无匹配的报告</Text>}
    </VStack>
    <PreviewDialog title="报告详情" isOpen={!!report} onClose={() => { if (!pending) { setDeleteConfirm(false); setSelected(null); } }}>
      {report && <VStack spacing={4} align="stretch">
        <HStack><Text fontWeight="bold">{report.targetName}</Text><Badge colorScheme={colors[report.kind]}>{labels[report.kind]}</Badge>{report.simulated ? <Badge colorScheme="purple">模拟</Badge> : <Badge>{artifactLabels[report.artifactStatus ?? "incomplete"] ?? "产物不完整"}</Badge>}</HStack>
        {!report.simulated && report.artifactError && <Text fontSize="sm">{report.artifactError}</Text>}
        <SimpleGrid columns={2} spacing={4}>
          {[["平均 FPS", formatMetric(report.averageFps)], ["1% Low", formatMetric(report.low1Fps)],
            ["0.1% Low", formatMetric(report.low01Fps)], ["帧时间", formatMetric(report.frameTimeMs, " ms")]].map(([label, value]) =>
              <Box key={label}><Text fontSize="xs" opacity={.7}>{label}</Text><Text fontSize="xl" fontWeight="bold">{value}</Text></Box>)}
        </SimpleGrid>
        <HStack wrap="wrap">
          <Button size="sm" leftIcon={<ExternalLink size={14} />} isDisabled={report.simulated === false && report.canOpenReport === false} onClick={() => void open()}>打开图表报告</Button>
          <Button size="sm" leftIcon={<FolderOpen size={14} />} variant="outline" onClick={() => void openDirectory()}>报告目录</Button>
        </HStack>
        <HStack wrap="wrap">
          <Button size="sm" variant="outline" leftIcon={<RefreshCw size={14} />} isLoading={pending && report.operation === "regenerating"} isDisabled={pending || (report.simulated === false && report.canRegenerate === false)}
            onClick={() => void run("report.regenerate", { id: report.id })}>重新生成</Button>
          <Button size="sm" variant="outline" leftIcon={<FileSearch size={14} />} isLoading={pending && report.operation === "diagnosing"} isDisabled={pending}
            onClick={() => void run("report.diagnose", { id: report.id })}>生成诊断</Button>
          <Button size="sm" variant="outline" colorScheme="red" leftIcon={<Trash2 size={14} />} isDisabled={pending || deleteBlocked}
            onClick={() => { clearError(); setOpenError(""); setDeleteConfirm(true); }}>删除记录</Button>
        </HStack>
        {deleteBlocked && <Text fontSize="sm">报告正在采集、生成或处理，完成后才能删除。</Text>}
        {report.diagnostic && <Text fontSize="sm" role="status">{report.diagnostic}</Text>}
        <ActionError message={error || openError} />
      </VStack>}
    </PreviewDialog>
    <PreviewDialog title="删除报告记录" isOpen={deleteConfirm && !!report} onClose={() => { if (!pending) setDeleteConfirm(false); }}>
      {report && <VStack spacing={4} align="stretch">
        <Text fontWeight="bold">{report.targetName}</Text>
        <Text fontSize="sm">{new Date(report.createdAt).toLocaleString("zh-CN")}</Text>
        <Text>本次记录的报告、原始采集数据和诊断文件将一起移入 Windows 回收站。需要恢复时，请到回收站还原。</Text>
        {!isDesktop() && <Text fontSize="sm">当前为模拟预览，只移除模拟记录，不操作真实文件。</Text>}
        <HStack justify="flex-end">
          <Button variant="outline" isDisabled={pending} onClick={() => setDeleteConfirm(false)}>取消</Button>
          <Button colorScheme="red" isLoading={pending} isDisabled={pending || deleteBlocked} onClick={deleteReport}>移入回收站</Button>
        </HStack>
        <ActionError message={error} />
      </VStack>}
    </PreviewDialog>
    <PreviewDialog title="预览报告目录" isOpen={directory} onClose={() => setDirectory(false)}>
      <Text fontFamily="monospace" fontSize="sm">{report?.runDir ?? `${state.config.outputDirectory}/${report?.id}`}</Text>
      <Text fontSize="sm" mt={4}>{isDesktop() && report?.runDir ? "真实运行目录。" : "模拟目录，不访问真实运行数据。"}</Text>
      <VStack align="stretch" mt={4} spacing={2}><Text>framescope-interactive-report.html</Text><Text>framescope-interactive-data.js</Text><Text>framescope-interactive-manifest.json</Text></VStack>
    </PreviewDialog>
  </Box>;
}
