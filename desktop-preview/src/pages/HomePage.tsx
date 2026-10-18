import { Box, Text, Flex, HStack, VStack, Button, Badge, Progress, SimpleGrid } from "@chakra-ui/react";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { useNavigate } from "react-router-dom";
import {
  Play, Square, FileChartColumn, Cpu, Monitor, MemoryStick, CircuitBoard,
  HardDrive, Wifi, Volume2, MonitorSmartphone, Activity,
} from "lucide-react";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { usePreviewState, usePreviewAction, phaseLabels, ActionError } from "@/preview/pages/shared";
import { monitorBusy } from "@/preview/runtime/state";
import { useAppStartup } from "@/contexts/app-startup-context";
import { useFloatingButtonStyle } from "@/hooks/use-floating-button-style";
import type { HardwareInfo } from "@/lib/hardware";
import { formatDiskType, formatMemorySpeed } from "@/lib/hardware";

interface InfoRow {
  name: string;
  value: string;
}

// Same card composition and row style as HardwarePage's DetailCard, rebuilt on the
// shared LiquidGlassCard so the home grid follows NexBox visual rules.
function HardwareSection({ icon: Icon, iconColor, title, rows }: {
  icon: React.ElementType;
  iconColor: string;
  title: string;
  rows: InfoRow[];
}) {
  if (rows.length === 0) return null;
  return (
    <LiquidGlassCard p={5} borderRadius="xl">
      <VStack align="start" spacing={3}>
        <HStack spacing={2}>
          <Icon size={18} color={iconColor} />
          <Text fontSize="md" fontWeight="bold">{title}</Text>
        </HStack>
        <VStack align="stretch" spacing={1.5} width="full">
          {rows.map((row) => (
            <HStack key={row.name} justify="space-between" width="full" spacing={3}>
              <Text fontSize="sm" opacity={0.72} noOfLines={1} flexShrink={0} maxW="35%">{row.name}</Text>
              <Text fontSize="sm" isTruncated textAlign="right" flex={1} title={row.value}>{row.value}</Text>
            </HStack>
          ))}
        </VStack>
      </VStack>
    </LiquidGlassCard>
  );
}

function buildHardwareSections(hardware: HardwareInfo): Array<{
  icon: React.ElementType; iconColor: string; title: string; rows: InfoRow[];
}> {
  const memoryTotal = hardware.memory.reduce((sum, mem) => sum + mem.capacity_gb, 0);
  return [
    {
      icon: Cpu, iconColor: "#3b82f6", title: "处理器",
      rows: [
        { name: "型号", value: hardware.cpu.name || "--" },
        { name: "核心 / 线程", value: `${hardware.cpu.cores} 核 / ${hardware.cpu.threads} 线程` },
        { name: "最大频率", value: `${(hardware.cpu.max_clock_speed / 1000).toFixed(1)} GHz` },
        { name: "三级缓存", value: `${(hardware.cpu.l3_cache_size / 1024).toFixed(0)} MB` },
        { name: "架构", value: hardware.cpu.architecture || "--" },
      ],
    },
    ...hardware.gpu.map((gpu, index) => ({
      icon: Monitor, iconColor: "#22c55e", title: index === 0 ? "显卡" : `显卡 ${index + 1}`,
      rows: [
        { name: "型号", value: gpu.name || "--" },
        { name: "厂商", value: gpu.vendor },
        ...(gpu.memory_gb != null && gpu.memory_gb > 0 ? [{ name: "显存", value: `${gpu.memory_gb.toFixed(1)} GB` }] : []),
        { name: "驱动版本", value: gpu.driver_version || "--" },
        ...(gpu.resolution_width && gpu.resolution_height ? [{
          name: "输出分辨率",
          value: `${gpu.resolution_width} x ${gpu.resolution_height}${gpu.refresh_rate ? ` @ ${gpu.refresh_rate} Hz` : ""}`,
        }] : []),
      ],
    })),
    {
      icon: MemoryStick, iconColor: "#06b6d4", title: "内存",
      rows: [
        { name: "总容量", value: `${memoryTotal.toFixed(0)} GB` },
        { name: "频率", value: hardware.memory.length > 0 ? formatMemorySpeed(hardware.memory[0]) : "--" },
        { name: "条数", value: `${hardware.memory.length}` },
        ...(hardware.memory[0]?.memory_type ? [{ name: "类型", value: hardware.memory[0].memory_type }] : []),
        ...(hardware.memory[0]?.manufacturer ? [{ name: "厂商", value: hardware.memory[0].manufacturer }] : []),
      ],
    },
    {
      icon: CircuitBoard, iconColor: "#f59e0b", title: "主板",
      rows: [
        { name: "型号", value: hardware.motherboard.product || "--" },
        { name: "制造商", value: hardware.motherboard.manufacturer || "--" },
        ...(hardware.motherboard.bios_version ? [{ name: "BIOS 版本", value: hardware.motherboard.bios_version }] : []),
        ...(hardware.motherboard.system_model ? [{ name: "机型", value: hardware.motherboard.system_model }] : []),
      ],
    },
    ...hardware.disk.map((disk, index) => ({
      icon: HardDrive, iconColor: "#a855f7", title: `存储 ${index + 1}`,
      rows: [
        { name: "型号", value: disk.model || "--" },
        { name: "容量", value: `${disk.size_gb.toFixed(0)} GB` },
        { name: "类型", value: formatDiskType(disk) },
        { name: "接口", value: disk.interface_type || "--" },
      ],
    })),
    ...hardware.monitor.map((monitor, index) => ({
      icon: MonitorSmartphone, iconColor: "#ec4899", title: index === 0 ? "显示器" : `显示器 ${index + 1}`,
      rows: [
        { name: "型号", value: monitor.name || "--" },
        ...(monitor.screen_width && monitor.screen_height ? [{
          name: "分辨率",
          value: `${monitor.screen_width} x ${monitor.screen_height}${monitor.refresh_rate ? ` @ ${monitor.refresh_rate} Hz` : ""}`,
        }] : []),
        { name: "制造商", value: monitor.manufacturer || "--" },
      ],
    })),
    ...hardware.network_card.map((card, index) => ({
      icon: Wifi, iconColor: "#14b8a6", title: index === 0 ? "网卡" : `网卡 ${index + 1}`,
      rows: [
        { name: "型号", value: card.name || "--" },
        { name: "链路速率", value: `${card.speed_mbps} Mbps` },
        ...(card.connection_name ? [{ name: "连接名称", value: card.connection_name }] : []),
      ],
    })),
    ...hardware.sound_card.map((card, index) => ({
      icon: Volume2, iconColor: "#f97316", title: index === 0 ? "声卡" : `声卡 ${index + 1}`,
      rows: [
        { name: "型号", value: card.name || "--" },
        { name: "制造商", value: card.manufacturer || "--" },
      ],
    })),
  ];
}

// Hardware details are the primary home content; monitoring and the latest report
// sit beside them as secondary cards (layout decided by the owner, 2026-09-07).
export default function HomePage() {
  const color = useAdaptiveTextColor();
  const state = usePreviewState();
  const { hardwareInfo } = useAppStartup();
  const { run, pending, error } = usePreviewAction();
  const navigate = useNavigate();
  const busy = monitorBusy();
  const stopping = ["stopping", "generating"].includes(state.monitor.phase);
  const latestReport = state.reports[0];
  const floatBtn = useFloatingButtonStyle();
  const generating = state.monitor.phase === "generating";
  const reportPercent = state.monitor.reportPercent;
  const hasReportProgress = reportPercent != null && Number.isFinite(reportPercent) && reportPercent >= 0 && reportPercent <= 100;
  const reportEtaSeconds = state.monitor.reportEtaSeconds;
  const hasReportEta = reportEtaSeconds != null && Number.isFinite(reportEtaSeconds) && reportEtaSeconds > 0;
  // 根容器不能加 overflowX hidden，否则会成为 sticky 的滚动容器使其失效
  return <Box pt={8} pr={4} pb={4} pl={4} minH="calc(100vh - 120px)" position="relative">
    <Flex gap={6} align="flex-start">
      <Text fontSize="3xl" fontWeight="bold" color={color.text} textShadow={color.shadow} lineHeight="1.4">FrameScope</Text>
      <Box flex={1} />
      <Button leftIcon={<FileChartColumn size={16} />} size="sm" variant="outline" onClick={() => navigate("/reports")} {...floatBtn}>查看报告</Button>
    </Flex>
    <Flex gap={6} mt={6} align="flex-start" wrap="wrap">
      <Box flex="1" minW="480px">
        <HStack mb={4} spacing={2}>
          <Activity size={18} color={color.text} />
          <Text fontSize="xl" fontWeight="bold" color={color.text} textShadow={color.shadow}>硬件配置</Text>
        </HStack>
        {hardwareInfo ? (
          <SimpleGrid columns={{ base: 1, lg: 2 }} spacing={4}>
            {buildHardwareSections(hardwareInfo).map((section) => (
              <HardwareSection key={section.title} {...section} />
            ))}
          </SimpleGrid>
        ) : (
          <LiquidGlassCard p={6} borderRadius="xl">
            <Text fontSize="sm" opacity={0.75}>硬件信息加载中……</Text>
          </LiquidGlassCard>
        )}
      </Box>
      {/* 粘性次要栏：顶部与硬件卡片首行对齐（缩进一个标题行的高度），高度锁定为可视区 */}
      <Box w="340px" maxW="100%" flexShrink={0}>
        <VStack spacing={4} align="stretch" position="sticky" top="68px" h="calc(100vh - 92px)" pt="48px">
          <LiquidGlassCard p={5} borderRadius="xl" flex={1} minH={0} overflowY="auto">
            <VStack spacing={3} align="stretch">
              <HStack spacing={2}>
                <Play size={16} />
                <Text fontWeight="bold">当前监控</Text>
                <Box flex={1} />
                <Badge colorScheme={state.monitor.phase === "error" ? "red" : busy ? "green" : "gray"}>{phaseLabels[state.monitor.phase]}</Badge>
              </HStack>
              <Box role="status" aria-label="当前监控游戏" px={3} py={2} borderRadius="md" bg="whiteAlpha.100">
                <Text fontSize="sm">{state.monitor.activeTargets.length ? state.monitor.activeTargets.join("、") : "未启动游戏"}</Text>
              </Box>
              <HStack spacing={2}>
                <Button leftIcon={<Play size={14} />} colorScheme="teal" size="sm" isDisabled={busy}
                  isLoading={pending} onClick={() => void run("monitor.start")}>启动监控</Button>
                <Button leftIcon={<Square size={14} />} size="sm" isDisabled={!busy || stopping} onClick={() => void run("monitor.stop")}>停止</Button>
                <Button variant="ghost" size="sm" onClick={() => navigate("/targets")}>管理目标</Button>
              </HStack>
              {generating && hasReportProgress && (
                <Progress
                  size="xs"
                  colorScheme="teal"
                  value={reportPercent}
                />
              )}
              <HStack fontSize="sm" spacing={2} opacity={0.85}>
                {generating ? (
                  <>
                    <Text>{hasReportProgress ? phaseLabels[state.monitor.phase] : "正在准备报告"}</Text>
                    <Text opacity={0.6}>·</Text>
                    <Text>{hasReportEta ? `预计还需 ${Math.ceil(reportEtaSeconds)} 秒` : "正在估算"}</Text>
                  </>
                ) : <Text>{phaseLabels[state.monitor.phase]}</Text>}
              </HStack>
              <ActionError message={error || state.monitor.error || ""} />
            </VStack>
          </LiquidGlassCard>
          <LiquidGlassCard p={5} borderRadius="xl" flex={1} minH={0} overflowY="auto">
            <VStack spacing={3} align="stretch">
              <HStack spacing={2}>
                <FileChartColumn size={16} />
                <Text fontWeight="bold">最新报告</Text>
              </HStack>
              {latestReport ? (
                <>
                  <Text fontWeight="medium">{latestReport.targetName}</Text>
                  <Text fontSize="sm" opacity={0.72}>
                    {new Date(latestReport.createdAt).toLocaleString()} · {latestReport.durationSeconds} 秒
                    {latestReport.averageFps != null ? ` · ${latestReport.averageFps.toFixed(1)} FPS` : ""}
                  </Text>
                  <HStack spacing={2}>
                    <Button size="sm" variant="outline" onClick={() => navigate(`/reports?id=${latestReport.id}`)}>打开报告详情</Button>
                    <Button size="sm" variant="ghost" onClick={() => navigate("/reports")}>全部报告</Button>
                  </HStack>
                </>
              ) : (
                <Text fontSize="sm" opacity={0.72}>暂无报告，启动监控后将自动生成</Text>
              )}
            </VStack>
          </LiquidGlassCard>
        </VStack>
      </Box>
    </Flex>
  </Box>;
}
