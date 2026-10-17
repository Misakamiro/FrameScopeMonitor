import { useEffect, useState } from "react";
import { Box, Heading, Text, VStack, HStack, Input, NumberInput, NumberInputField, NumberInputStepper, NumberIncrementStepper, NumberDecrementStepper, Switch, Button, FormControl, FormLabel } from "@chakra-ui/react";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { CustomSelect } from "@/components/special/custom-select";
import { usePreviewState, usePreviewAction, ActionError } from "../pages/shared";
import { isDesktop } from "../runtime/platform";
import type { PreviewScenario } from "../runtime";
import { UpdateSettings } from "./UpdateSettings";
type NumberConfigKey = "sampleIntervalMs" | "pollIntervalMs" | "captureDurationSeconds";
export function FrameScopeSettings() {
  const state = usePreviewState();
  const desktop = isDesktop();
  const [draft, setDraft] = useState(state.config);
  const [dirty, setDirty] = useState(false);
  const [saved, setSaved] = useState(false);
  const { run, pending, error } = usePreviewAction();
  // State rewrites whenever anything syncs (monitor ticks, backend
  // reconciliation) and each rewrite clones state.config. Re-syncing the
  // draft unconditionally used to wipe in-progress edits mid-keystroke;
  // only mirror the live config while the user hasn't touched the form.
  useEffect(() => { if (!dirty) setDraft(state.config); }, [state.config, dirty]);
  const numberFields: Array<[NumberConfigKey, string, number, number]> = desktop
    ? [["sampleIntervalMs", "全局采样间隔（毫秒）", 500, 5000], ["pollIntervalMs", "目标扫描间隔（毫秒）", 250, 10000]]
    : [["sampleIntervalMs", "全局采样间隔（毫秒）", 500, 5000], ["pollIntervalMs", "目标扫描间隔（毫秒）", 250, 10000], ["captureDurationSeconds", "模拟采集时长（秒）", 1, 3600]];
  const update = (patch: Partial<typeof draft>) => { setDraft({ ...draft, ...patch }); setDirty(true); setSaved(false); };
  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    const ok = await run("config.update", draft);
    setSaved(ok);
    if (ok) setDirty(false);
  };
  return <Box>
    <Heading size="lg" mb={6}>FrameScope</Heading>
    <VStack spacing={5} align="stretch">
      <LiquidGlassCard p={6} borderRadius="xl">
        <VStack as="form" spacing={5} align="stretch" onSubmit={save}>
          <Text fontSize="lg" fontWeight="bold">采样与报告</Text>
          {desktop && <Text fontSize="sm" opacity={.7}>保存到 FrameScope 配置文件：扫描间隔即时生效；采样间隔对之后启动的采集会话生效。</Text>}
          {numberFields.map(([key, label, min, max]) =>
            <FormControl key={key}><FormLabel fontSize="sm">{label}</FormLabel><NumberInput value={draft[key]} min={min} max={max} onChange={(_, value) => update({ [key]: value })}>
              <NumberInputField /><NumberInputStepper><NumberIncrementStepper /><NumberDecrementStepper /></NumberInputStepper></NumberInput></FormControl>)}
          {!desktop && <FormControl><FormLabel fontSize="sm">预览数据目录</FormLabel><Input value={draft.outputDirectory} onChange={e => update({ outputDirectory: e.target.value })} /></FormControl>}
          <HStack justify="space-between">
            <Box><Text fontSize="sm">启动软件时自动开启监控</Text><Text fontSize="xs" opacity={.7}>下次打开软件时生效，不改变当前监控状态。</Text></Box>
            <Switch aria-label="启动软件时自动开启监控" colorScheme="teal" isChecked={draft.autoStart} onChange={e => update({ autoStart: e.target.checked })} />
          </HStack>
          {([["autoOpenReport", "完成后自动打开报告"], ["detailedLogging", "详细日志"], ["diagnosticLogging", "性能诊断日志"]] as const).map(([key, label]) =>
            <HStack key={key} justify="space-between"><Text fontSize="sm">{label}</Text><Switch aria-label={label} colorScheme="teal" isChecked={draft[key]} onChange={e => update({ [key]: e.target.checked })} /></HStack>)}
          <HStack justify="space-between">
            <Box><Text fontSize="sm">启动软件时自动最小化</Text><Text fontSize="xs" opacity={.7}>下次启动时最小化到任务栏，不影响监控和悬浮窗。</Text></Box>
            <Switch aria-label="启动软件时自动最小化" colorScheme="teal" isChecked={draft.startMinimized} onChange={e => update({ startMinimized: e.target.checked })} />
          </HStack>
          <ActionError message={error} /><HStack justify="flex-end"><Button variant="ghost" onClick={() => { setDraft(state.config); setDirty(false); setSaved(false); }}>取消修改</Button><Button type="submit" colorScheme="teal" isLoading={pending}>保存</Button></HStack>
          {saved && <Text role="status" fontSize="sm" color="teal.500">设置已保存</Text>}
        </VStack>
      </LiquidGlassCard>
      {/* Fixture scenarios only make sense for the browser preview; on
          desktop every value comes from the real backend. */}
      {!isDesktop() && <LiquidGlassCard p={6} borderRadius="xl">
        <FormControl><FormLabel>预览数据场景</FormLabel><CustomSelect ariaLabel="预览数据场景" width="100%" value={state.scenario}
          onChange={value => void run("scenario.set", value as PreviewScenario)}
          options={[
            { value: "normal", label: "正常硬件" },
            { value: "multi-gpu", label: "多 GPU" },
            { value: "missing-sensors", label: "部分传感器缺失" },
            { value: "empty", label: "空数据" },
            { value: "failure", label: "采样失败" },
          ]} /></FormControl>
        <Text mt={3} fontSize="sm" opacity={.7}>仅模拟数据，不启动硬件或游戏采集。</Text>
      </LiquidGlassCard>}
    </VStack>
  </Box>;
}
export function AboutSettings() {
  return <Box><Heading size="lg" mb={6}>关于／更新</Heading>
    <LiquidGlassCard p={6} borderRadius="xl"><VStack spacing={4} align="stretch">
      <UpdateSettings />
      <Text fontSize="sm">界面直接复用 NexBox 9.5.3。硬件数据来自 FrameScope 硬件服务；目标、采集与报告已接入真实采集管线。</Text>
      <Text fontSize="sm">NexBox 来源：MuLiuSaMa/NexBox · c800dc5</Text>
      <Text fontSize="sm">FrameScope 功能基线：Misakamiro/FrameScopeMonitor · 2c48031</Text>
      <Text fontSize="sm">NexBox 原作者及贡献者的版权与 GPL-3.0 许可保留在随附 LICENSE、LICENSE.EXTRA 和源码来源清单中。</Text>
    </VStack></LiquidGlassCard></Box>;
}
