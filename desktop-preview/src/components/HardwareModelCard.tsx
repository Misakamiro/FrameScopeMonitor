import React, { useEffect, useState } from "react";
import { Box, Text, VStack, HStack, Spinner, useColorModeValue } from "@chakra-ui/react";
import { useTranslation } from "react-i18next";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import type { HardwareInfo } from "@/lib/hardware";
import { getHardwareInfo } from "@/lib/hardware";
import { useCallback } from "react";
import { invoke } from "@tauri-apps/api/core";
import { useAppStartup } from "@/contexts/app-startup-context";

export default function HardwareModelCard() {
  const { t } = useTranslation();
  // 复用应用启动时已缓存的硬件信息，避免每次进入主页重新加载
  const { hardwareInfo: startupHardwareInfo } = useAppStartup();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hardware, setHardware] = useState<HardwareInfo | null>(startupHardwareInfo);
  const [osShort, setOsShort] = useState<string | null>(null);

  // 仅当全局缓存未加载时才自行获取；已有缓存则直接使用，不重复调用后端
  useEffect(() => {
    if (startupHardwareInfo) {
      setHardware(startupHardwareInfo);
      setLoading(false);
      return;
    }
    if (hardware) return;

    let mounted = true;
    setLoading(true);
    setError(null);

    const load = async () => {
      try {
        const info = await getHardwareInfo();
        if (mounted) setHardware(info);
      } catch (e) {
        if (mounted) setError((e as Error)?.message || String(e));
      } finally {
        if (mounted) setLoading(false);
      }
    };

    load();
    return () => {
      mounted = false;
    };
  }, [startupHardwareInfo, hardware]);

  const detectOs = useCallback(async () => {
    try {
      const version = await invoke<string>("get_os_version");
      // long_os_version 返回如 "Windows 11 Pro" 或 "Windows 10 Home"
      const match = version.match(/(Windows \d+)/);
      setOsShort(match ? match[1] : version);
    } catch {
      try {
        const ua = typeof navigator !== "undefined" ? navigator.userAgent || "" : "";
        if (ua.includes("Windows")) {
          setOsShort(ua.includes("Windows NT 10.0") ? "Windows 10" : "Windows");
        } else if (ua.includes("Mac")) setOsShort("macOS");
        else if (ua.includes("Linux")) setOsShort("Linux");
        else setOsShort(null);
      } catch {
        setOsShort(null);
      }
    }
  }, []);

  useEffect(() => {
    detectOs();
  }, [detectOs]);

  const labelColor = useColorModeValue("gray.700", "#ffffff");
  const valueColor = useColorModeValue("gray.800", "#e6e6e6");

  const renderMemorySummary = () => {
    if (!hardware || !hardware.memory || hardware.memory.length === 0) return null;

    const partNumbers = hardware.memory
      .map((m) => (m.part_number || "").trim())
      .filter((p) => p && p !== "未知");

    if (partNumbers.length > 0) {
      const counts = new Map<string, number>();
      for (const pn of partNumbers) counts.set(pn, (counts.get(pn) || 0) + 1);
      return Array.from(counts.entries()).map(([pn, cnt]) => `${pn}${cnt > 1 ? ` x${cnt}` : ""}`).join("; ");
    }

    // fallback to capacity summary
    const caps = hardware.memory.map((m) => Math.round(m.capacity_gb || 0));
    const capCounts = new Map<number, number>();
    for (const c of caps) capCounts.set(c, (capCounts.get(c) || 0) + 1);
    return Array.from(capCounts.entries()).map(([cap, cnt]) => `${cap}GB${cnt > 1 ? ` x${cnt}` : ""}`).join("; ");
  };

  if (loading) {
    return (
      <LiquidGlassCard px={3} py={2} boxShadow="sm" minW="260px" maxW="360px">
        <HStack spacing={3} align="center">
          <Spinner size="sm" />
          <Text fontSize="sm">{t("home.hardwareModel.loading") || t("home.loading")}</Text>
        </HStack>
      </LiquidGlassCard>
    );
  }

  if (error || !hardware) {
    return (
      <LiquidGlassCard px={3} py={2} boxShadow="sm" minW="260px" maxW="360px">
        <Text fontSize="sm">{t("home.hardwareModel.unknown") || "未知型号"}</Text>
      </LiquidGlassCard>
    );
  }

  const lines: Array<{ label: string; value: string } > = [];

  if (hardware.cpu && hardware.cpu.name) {
    lines.push({ label: t("hardware.processor") || "CPU", value: hardware.cpu.name });
  }

  if (hardware.gpu && hardware.gpu.length > 0) {
    hardware.gpu.forEach((g, idx) => {
      const label = idx === 0 ? (t("hardware.gpu") || "GPU") : `${t("hardware.gpu")} ${idx + 1}`;
      lines.push({ label, value: g.name });
    });
  }

  const memSummary = renderMemorySummary();
  if (memSummary) {
    const totalGb = hardware?.memory?.reduce((s, m) => s + (m.capacity_gb || 0), 0) || 0;
    const totalStr = totalGb % 1 === 0 ? `${Math.round(totalGb)}GB` : `${totalGb.toFixed(1)}GB`;
    const totalLabel = t("hardware.totalCapacity") || "总容量";
    lines.push({ label: t("hardware.ram") || "内存", value: `${memSummary} · ${totalLabel}: ${totalStr}` });
  }

  if (hardware.motherboard) {
    lines.push({ label: t("hardware.motherboard") || "主板", value: hardware.motherboard.product });
  }

  if (hardware.disk && hardware.disk.length > 0) {
    const diskList = hardware.disk.map(d => `${d.model} (${(d.size_gb ?? 0).toFixed(0)}GB)`);
    const diskDisplay = diskList.length <= 2 ? diskList.join("; ") : `${diskList.slice(0,2).join("; ")} +${diskList.length - 2}`;
    lines.push({ label: t("hardware.storage") || "硬盘", value: diskDisplay });
  }

  // 操作系统放在最下面
  if (osShort) {
    lines.push({ label: t("hardware.os") || "操作系统", value: osShort });
  }

  return (
    <LiquidGlassCard px={3} py={2} boxShadow="sm" minW="260px" maxW="360px">
      <VStack spacing={1} align="start">
        {lines.map((l, i) => (
          <Box key={i}>
            <Text fontSize="xs" color={labelColor}>
              {l.label}
            </Text>
            <Text fontSize="sm" color={valueColor} fontWeight="semibold">
              {l.value}
            </Text>
          </Box>
        ))}
      </VStack>
    </LiquidGlassCard>
  );
}
