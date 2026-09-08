import { useEffect, useState } from "react";
import {
  Box, Heading, Text, HStack, VStack, Input, InputGroup, InputLeftElement,
  Button, IconButton, Switch, FormControl, FormLabel, Select, Badge, Tooltip,
} from "@chakra-ui/react";
import { Search, Plus, Pencil, Trash2 } from "lucide-react";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { useAdaptiveTextColor } from "@/hooks/use-adaptive-text-color";
import { usePreviewState, usePreviewAction, PreviewDialog, ActionError, useFloatingFieldStyle } from "./shared";
import { isDesktop, backendInvoke } from "@/preview/runtime/platform";
import { monitorBusy } from "@/preview/runtime/state";
import type { PreviewTarget } from "@/preview/runtime";

const fallbackProcesses = ["cs2.exe", "VALORANT-Win64-Shipping.exe", "TslGame.exe", "GamePreview.exe"];
export default function TargetsPage() {
  const state = usePreviewState();
  const color = useAdaptiveTextColor();
  const { run, pending, error, clearError } = usePreviewAction();
  const [query, setQuery] = useState("");
  const [editing, setEditing] = useState<Partial<PreviewTarget> | null>(null);
  const [removing, setRemoving] = useState<PreviewTarget | null>(null);
  // Desktop: real window-titled process enumeration (fallback examples only
  // apply to the browser preview, and a load failure surfaces in the UI
  // instead of silently masquerading as real data).
  const [processes, setProcesses] = useState<string[]>(isDesktop() ? [] : fallbackProcesses);
  const [processesUnavailable, setProcessesUnavailable] = useState(false);
  useEffect(() => {
    if (!isDesktop()) return;
    let cancelled = false;
    backendInvoke<{ processes: Array<{ processName: string; displayText?: string }> }>("processes_refresh", { query: "" })
      .then(payload => {
        if (cancelled) return;
        const names = (payload.processes ?? [])
          .map(p => p.displayText ? p.processName + " — " + p.displayText : p.processName)
          .filter(Boolean);
        if (names.length) setProcesses(names);
        else setProcessesUnavailable(true);
      })
      .catch(() => { if (!cancelled) setProcessesUnavailable(true); });
    return () => { cancelled = true; };
  }, []);
  const busy = monitorBusy();
  const field = useFloatingFieldStyle();
  const targets = state.targets.filter(t => `${t.name} ${t.executable}`.toLowerCase().includes(query.toLowerCase()));
  const close = () => { setEditing(null); clearError(); };
  const save = async () => {
    if (!editing) return;
    const ok = editing.id
      ? await run("target.update", { id: editing.id, name: editing.name, executable: editing.executable, enabled: editing.enabled })
      : await run("target.add", { name: editing.name ?? "", executable: editing.executable ?? "", enabled: editing.enabled });
    if (ok) close();
  };
  return <Box pt={8}>
    <HStack justify="space-between" mb={6}>
      <Heading size="lg" color={color.text} textShadow={color.shadow}>监控目标</Heading>
      <Button leftIcon={<Plus size={16} />} colorScheme="teal" size="sm" isDisabled={busy}
        onClick={() => { clearError(); setEditing({ name: "", executable: "", enabled: true }); }}>添加目标</Button>
    </HStack>
    <InputGroup maxW="400px" mb={5}><InputLeftElement pointerEvents="none" color={field.iconColor}><Search size={16} /></InputLeftElement>
      <Input aria-label="搜索目标" placeholder="搜索游戏或进程" value={query} onChange={e => setQuery(e.target.value)} sx={field.sx} /></InputGroup>
    <VStack align="stretch" spacing={3}>
      {targets.map(target => <LiquidGlassCard key={target.id} p={5} borderRadius="xl">
        <HStack spacing={4}>
          <Switch aria-label={`启用 ${target.name}`} isChecked={target.enabled} isDisabled={busy} colorScheme="teal"
            onChange={e => void run("target.update", { id: target.id, enabled: e.target.checked })} />
          <Box flex={1} minW={0}><Text fontWeight="bold" noOfLines={1}>{target.name}</Text><Text fontSize="sm" opacity={.7} noOfLines={1}>{target.executable}</Text></Box>
          <Badge colorScheme={target.enabled ? "green" : "gray"}>{target.enabled ? "已启用" : "未启用"}</Badge>
          <Tooltip label="编辑"><IconButton aria-label={`编辑 ${target.name}`} icon={<Pencil size={16} />} variant="ghost" size="sm" isDisabled={busy}
            onClick={() => { clearError(); setEditing({ ...target }); }} /></Tooltip>
          <Tooltip label="删除"><IconButton aria-label={`删除 ${target.name}`} icon={<Trash2 size={16} />} variant="ghost" colorScheme="red" size="sm" isDisabled={busy}
            onClick={() => { clearError(); setRemoving(target); }} /></Tooltip>
        </HStack>
      </LiquidGlassCard>)}
      {!targets.length && <Text py={12} textAlign="center" color={color.text}>{query ? "没有匹配的目标" : "暂无监控目标"}</Text>}
      {!editing && !removing && <ActionError message={error} />}
    </VStack>
    <PreviewDialog title={editing?.id ? "编辑目标" : "添加目标"} isOpen={editing !== null} onClose={close}>
      <VStack as="form" spacing={4} align="stretch" onSubmit={e => { e.preventDefault(); void save(); }}>
        <FormControl isRequired><FormLabel>游戏名称</FormLabel><Input autoFocus value={editing?.name ?? ""} onChange={e => setEditing({ ...editing, name: e.target.value })} /></FormControl>
        <FormControl isRequired><FormLabel>进程名</FormLabel><Input placeholder="game.exe" value={editing?.executable ?? ""} onChange={e => setEditing({ ...editing, executable: e.target.value })} /></FormControl>
        <FormControl><FormLabel>预览进程列表</FormLabel><Select placeholder={processesUnavailable ? "进程列表不可用" : "选择进程"} isDisabled={processesUnavailable} value="" onChange={e => setEditing({ ...editing, executable: e.target.value })}>
          {processesUnavailable
            ? <option value="">窗口枚举失败，请手动输入进程名</option>
            : processes.map((process, index) => <option key={process + "#" + index}>{process}</option>)}</Select></FormControl>
        <HStack justify="space-between"><Text>启用目标</Text><Switch aria-label="启用目标" colorScheme="teal" isChecked={editing?.enabled ?? true} onChange={e => setEditing({ ...editing, enabled: e.target.checked })} /></HStack>
        <ActionError message={error} /><HStack justify="flex-end"><Button variant="ghost" onClick={close}>取消</Button><Button type="submit" colorScheme="teal" isLoading={pending}>保存</Button></HStack>
      </VStack>
    </PreviewDialog>
    <PreviewDialog title="删除目标" isOpen={!!removing} onClose={() => setRemoving(null)}>
      <Text mb={5}>删除“{removing?.name}”？已有报告将保留。</Text><ActionError message={error} />
      <HStack justify="flex-end"><Button variant="ghost" onClick={() => setRemoving(null)}>取消</Button>
        <Button colorScheme="red" isLoading={pending} onClick={async () => { if (removing && await run("target.remove", { id: removing.id })) setRemoving(null); }}>确认删除</Button></HStack>
    </PreviewDialog>
  </Box>;
}
