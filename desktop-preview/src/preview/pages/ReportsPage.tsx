import {searchMatches} from '@/lib/unified-search';
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
import type { PreviewReport } from '@/preview/runtime/types';
const labels = { full: "完整", partial: "部分数据", diagnostic: "诊断", error: "失败" };
const colors = { full: "green", partial: "yellow", diagnostic: "blue", error: "red" };
const artifactLabels: Record<string, string> = {
  complete: "产物完整", "legacy-readable": "历史兼容，建议重新生成", "raw-unavailable-readable": "原始数据不足，保留历史统计",
  incomplete: "产物不完整", "inputs-changed": "输入已变化，显示已生成的报告",
};
function artifactLabel(report: PreviewReport) {
  if (report.inspectionStatus === 'unavailable') return '检查暂不可用';
  if (report.inspectionStatus === 'pending') return '等待校验';
  if (report.inspectionStatus === 'checking') return '正在校验原始数据';
  return artifactLabels[report.artifactStatus ?? 'incomplete'] ?? '产物不完整';
}
export default function ReportsPage() {
  const state = usePreviewState();
  const color = useAdaptiveTextColor();
  const [params] = useSearchParams();
  const [selected, setSelected] = useState<string | null>(params.get("id"));
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState("all");
  const [page, setPage] = useState(1);
  const [openError, setOpenError] = useState("");
  const [directory, setDirectory] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const { run, pending, error, clearError } = usePreviewAction();
  const field = useFloatingFieldStyle();
  // Real backend reports arrive through the runtime sync; refresh on mount so
  // a completed capture (or backend-side run) shows up without a restart.
  useEffect(() => {
    if (!isDesktop()) return;
    let cancelled = false;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const delays = [1000, 3000, 5000];
    const errorDelays = [1000, 3000, 10000];
    let failures = 0;
    const check = async (attempt: number) => {
      const retry = await refreshDesktopReports({page, search:query, kind}, controller.signal, refresh > 0 && attempt === 0);
      if (cancelled || !retry) return;
      if (retry === 'error') {
        if (failures < errorDelays.length) timer = setTimeout(() => void check(attempt + 1), errorDelays[failures++]);
      } else {
        failures = 0;
        timer = setTimeout(() => void check(attempt + 1), delays[Math.min(attempt, delays.length - 1)]);
      }
    };
    void check(0);
    return () => { cancelled = true; controller.abort(); clearTimeout(timer); };
  }, [page, query, kind, refresh]);
  useEffect(() => { if (params.get("id")) setSelected(params.get("id")); }, [params]);
  const filtered = state.reports.filter(r => (kind === "all" || r.kind === kind) && searchMatches(query,[r.targetName,r.createdAt,r.id]));
  const pages = isDesktop() ? (state.reportPage?.pages ?? 1) : Math.max(1, Math.ceil(filtered.length/50));
  const currentPage = isDesktop() ? (state.reportPage?.page ?? 1) : Math.min(page, pages);
  const reports = isDesktop() ? state.reports : filtered.slice((currentPage-1)*50,currentPage*50);
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
      <Button aria-label="刷新报告" onClick={() => setRefresh(value => value + 1)}><RefreshCw size={16}/></Button>
      <Input aria-label="搜索报告" placeholder="搜索全部历史报告" value={query} onChange={e => {setQuery(e.target.value);setPage(1);}} maxW="360px" sx={field.sx} />
      <CustomSelect ariaLabel="报告类型" width="160px" value={kind} onChange={value=>{setKind(value);setPage(1);}}
        options={[{ value: "all", label: "全部类型" }, ...Object.entries(labels).map(([value, label]) => ({ value, label }))]} />
    </HStack>
    <ActionError message={state.reportListError || (reports.some(item => item.inspectionStatus === 'unavailable') ? '部分报告的完整校验失败。可查看具体原因并刷新重试；已生成报告与原始输入的状态分别保留。' : '')}/>
    <HStack mb={4} justify="space-between"><Text fontSize="sm">共 {isDesktop() ? state.reportPage?.total ?? 0 : state.reports.length} 份报告 · 匹配 {isDesktop() ? state.reportPage?.filteredTotal ?? 0 : filtered.length} 份</Text><HStack><Button size="sm" isDisabled={currentPage<=1} onClick={()=>setPage(currentPage-1)}>上一页</Button><Text fontSize="sm">{currentPage} / {pages}</Text><Button size="sm" isDisabled={currentPage>=pages} onClick={()=>setPage(currentPage+1)}>下一页</Button></HStack></HStack>
    <VStack spacing={3} align="stretch">
      {reports.map(item => <LiquidGlassCard key={item.id} p={5} borderRadius="xl">
        <HStack justify="space-between" spacing={4}>
          <Box flex={1} minW={0}><HStack mb={2}><Text fontWeight="bold" noOfLines={1}>{item.targetName}</Text>
            <Badge colorScheme={colors[item.kind]}>{labels[item.kind]}</Badge>{item.simulated ? <Badge colorScheme="purple">模拟</Badge> : <Badge>{artifactLabel(item)}</Badge>}</HStack>
            <Text fontSize="sm" opacity={.7}>{new Date(item.createdAt).toLocaleString("zh-CN")} · {item.durationSeconds == null ? "时长不可用" : `${item.durationSeconds} 秒`} · {formatMetric(item.averageFps, " FPS")}</Text></Box>
          {item.operation && item.operation !== "ready" && <Spinner size="sm" />}
          <Button size="sm" variant="outline" onClick={() => { setSelected(item.id); setOpenError(""); }}>查看详情</Button>
        </HStack>
      </LiquidGlassCard>)}
      {!reports.length && <Text py={12} textAlign="center" color={color.text}>暂无匹配的报告</Text>}
    </VStack>
    <PreviewDialog title="报告详情" isOpen={!!report} onClose={() => { if (!pending) { setDeleteConfirm(false); setSelected(null); } }}>
      {report && <VStack spacing={4} align="stretch">
        <HStack><Text fontWeight="bold">{report.targetName}</Text><Badge colorScheme={colors[report.kind]}>{labels[report.kind]}</Badge>{report.simulated ? <Badge colorScheme="purple">模拟</Badge> : <Badge>{artifactLabel(report)}</Badge>}</HStack>
        {!report.simulated && report.artifactError && report.artifactError !== error && report.artifactError !== report.diagnostic && <Text fontSize="sm">{report.artifactError}</Text>}
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
        {report.diagnostic && report.diagnostic !== error && <Text fontSize="sm" role="status">{report.diagnostic}</Text>}
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
