import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Box, Heading, Text, HStack, VStack, Input, Select, Button, Badge, SimpleGrid, Spinner } from "@chakra-ui/react";
import { ExternalLink, RefreshCw, FileSearch, FolderOpen } from "lucide-react";
import { invoke } from "@tauri-apps/api/core";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { usePreviewState, usePreviewAction, PreviewDialog, ActionError, formatMetric, useFloatingFieldStyle } from "./shared";
import { isDesktop, backendInvoke } from "@/preview/runtime/platform";
import { refreshDesktopReports } from "@/preview/runtime/state";
const labels = { full: "完整", partial: "部分数据", diagnostic: "诊断", error: "失败" };
const colors = { full: "green", partial: "yellow", diagnostic: "blue", error: "red" };
export default function ReportsPage() {
  const state = usePreviewState();
  const color = useAdaptiveTextColor();
  const [params] = useSearchParams();
  const [selected, setSelected] = useState<string | null>(params.get("id"));
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState("all");
  const [openError, setOpenError] = useState("");
  const [directory, setDirectory] = useState(false);
  const { run, pending, error } = usePreviewAction();
  const field = useFloatingFieldStyle();
  // Real backend reports arrive through the runtime sync; refresh on mount so
  // a completed capture (or backend-side run) shows up without a restart.
  useEffect(() => {
    if (isDesktop()) void refreshDesktopReports();
  }, []);
  useEffect(() => { if (params.get("id")) setSelected(params.get("id")); }, [params]);
  const reports = state.reports.filter(r => (kind === "all" || r.kind === kind) && r.targetName.toLowerCase().includes(query.toLowerCase()));
  const report = state.reports.find(r => r.id === selected);
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
  return <Box pt={8}>
    <Heading size="lg" mb={6} color={color.text} textShadow={color.shadow}>报告</Heading>
    <HStack spacing={3} mb={5}>
      <Input aria-label="搜索报告" placeholder="搜索游戏报告" value={query} onChange={e => setQuery(e.target.value)} maxW="360px" sx={field.sx} />
      <Select aria-label="报告类型" value={kind} onChange={e => setKind(e.target.value)} w="160px" sx={field.sx}><option value="all">全部类型</option>
        {Object.entries(labels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select>
    </HStack>
    <VStack spacing={3} align="stretch">
      {reports.map(item => <LiquidGlassCard key={item.id} p={5} borderRadius="xl">
        <HStack justify="space-between" spacing={4}>
          <Box flex={1} minW={0}><HStack mb={2}><Text fontWeight="bold" noOfLines={1}>{item.targetName}</Text>
            <Badge colorScheme={colors[item.kind]}>{labels[item.kind]}</Badge>{item.simulated ? <Badge colorScheme="purple">模拟</Badge> : null}</HStack>
            <Text fontSize="sm" opacity={.7}>{new Date(item.createdAt).toLocaleString("zh-CN")} · {item.durationSeconds} 秒 · {formatMetric(item.averageFps, " FPS")}</Text></Box>
          {item.operation && item.operation !== "ready" && <Spinner size="sm" />}
          <Button size="sm" variant="outline" onClick={() => { setSelected(item.id); setOpenError(""); }}>查看详情</Button>
        </HStack>
      </LiquidGlassCard>)}
      {!reports.length && <Text py={12} textAlign="center" color={color.text}>暂无匹配的报告</Text>}
    </VStack>
    <PreviewDialog title="报告详情" isOpen={!!report} onClose={() => setSelected(null)}>
      {report && <VStack spacing={4} align="stretch">
        <HStack><Text fontWeight="bold">{report.targetName}</Text><Badge colorScheme={colors[report.kind]}>{labels[report.kind]}</Badge>{report.simulated ? <Badge colorScheme="purple">模拟</Badge> : null}</HStack>
        <SimpleGrid columns={2} spacing={4}>
          {[["平均 FPS", formatMetric(report.averageFps)], ["1% Low", formatMetric(report.low1Fps)],
            ["0.1% Low", formatMetric(report.low01Fps)], ["帧时间", formatMetric(report.frameTimeMs, " ms")]].map(([label, value]) =>
              <Box key={label}><Text fontSize="xs" opacity={.7}>{label}</Text><Text fontSize="xl" fontWeight="bold">{value}</Text></Box>)}
        </SimpleGrid>
        <HStack wrap="wrap">
          <Button size="sm" leftIcon={<ExternalLink size={14} />} onClick={() => void open()}>打开图表报告</Button>
          <Button size="sm" leftIcon={<FolderOpen size={14} />} variant="outline" onClick={() => void openDirectory()}>报告目录</Button>
        </HStack>
        <HStack wrap="wrap">
          <Button size="sm" variant="outline" leftIcon={<RefreshCw size={14} />} isLoading={pending && report.operation === "regenerating"} isDisabled={pending}
            onClick={() => void run("report.regenerate", { id: report.id })}>重新生成</Button>
          <Button size="sm" variant="outline" leftIcon={<FileSearch size={14} />} isLoading={pending && report.operation === "diagnosing"} isDisabled={pending}
            onClick={() => void run("report.diagnose", { id: report.id })}>生成诊断</Button>
        </HStack>
        {report.diagnostic && <Text fontSize="sm" role="status">{report.diagnostic}</Text>}
        <ActionError message={error || openError} />
      </VStack>}
    </PreviewDialog>
    <PreviewDialog title="预览报告目录" isOpen={directory} onClose={() => setDirectory(false)}>
      <Text fontFamily="monospace" fontSize="sm">{report?.runDir ?? `${state.config.outputDirectory}/${report?.id}`}</Text>
      <Text fontSize="sm" mt={4}>{isDesktop() && report?.runDir ? "真实运行目录。" : "模拟目录，不访问真实运行数据。"}</Text>
      <VStack align="stretch" mt={4} spacing={2}><Text>framescope-interactive-report.html</Text><Text>framescope-interactive-data.js</Text><Text>framescope-interactive-manifest.json</Text></VStack>
    </PreviewDialog>
  </Box>;
}
