import { useEffect, useMemo, useState } from "react";
import {
  Modal, ModalOverlay, ModalContent, ModalHeader, ModalCloseButton, ModalBody,
  Divider, Text, useColorModeValue,
} from "@chakra-ui/react";
import {
  getPreviewState, subscribePreviewState, previewAction,
  type PreviewAction, type PreviewActionPayloads,
} from "@/preview/runtime";
import type { ReactNode } from "react";

// Floating search/filter fields sit directly on the background; give them the same
// translucent-pill treatment as GlobalSearch so they stay readable on any wallpaper.
export function useFloatingFieldStyle() {
  const bg = useColorModeValue("rgba(255,255,255,0.6)", "rgba(28,28,32,0.55)");
  const color = useColorModeValue("gray.800", "#f0f0f0");
  const borderColor = useColorModeValue("rgba(0,0,0,0.14)", "rgba(255,255,255,0.2)");
  const placeholder = useColorModeValue("gray.500", "gray.400");
  const icon = useColorModeValue("gray.600", "gray.300");
  return useMemo(() => ({
    iconColor: icon,
    sx: {
      bg,
      color,
      borderColor,
      _hover: { borderColor },
      _focus: { borderColor, boxShadow: "none" },
      "&::placeholder": { color: placeholder, opacity: 0.9 },
      "& option": { color: "gray.800", bg: "white" },
    },
  }), [bg, color, borderColor, placeholder, icon]);
}

export function usePreviewState() {
  const [state, setState] = useState(getPreviewState);
  useEffect(() => {
    const unsubscribe = subscribePreviewState(setState);
    setState(getPreviewState());
    return unsubscribe;
  }, []);
  return state;
}

export function usePreviewAction() {
  const [error, setError] = useState("");
  const [pending, setPending] = useState(false);
  async function run<K extends PreviewAction>(action: K, payload?: PreviewActionPayloads[K]) {
    setError("");
    setPending(true);
    try {
      await previewAction(action, payload);
      return true;
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
      return false;
    } finally {
      setPending(false);
    }
  }
  return { run, error, pending, clearError: () => setError("") };
}

// HardwareDetailModal's surface, sizing and scroll pattern, shared by business dialogs.
export function PreviewDialog({ title, isOpen, onClose, children }: {
  title: string; isOpen: boolean; onClose: () => void; children: ReactNode;
}) {
  const overlayBg = useColorModeValue("blackAlpha.600", "blackAlpha.700");
  const contentBg = useColorModeValue("white", "#1a1a1a");
  const borderColor = useColorModeValue("gray.200", "#2a2a2a");
  const color = useColorModeValue("gray.800", "#e0e0e0");
  return (
    <Modal isOpen={isOpen} onClose={onClose} size="md" isCentered scrollBehavior="inside">
      <ModalOverlay bg={overlayBg} backdropFilter="blur(4px)" />
      <ModalContent bg={contentBg} color={color} border="1px solid" borderColor={borderColor}
        borderRadius="xl" mx={4} maxH="80vh" overflow="hidden">
        <ModalHeader fontSize="lg">{title}</ModalHeader>
        <ModalCloseButton />
        <Divider borderColor={borderColor} />
        <ModalBody pb={6} overflowY="auto">{children}</ModalBody>
      </ModalContent>
    </Modal>
  );
}

export function ActionError({ message }: { message: string }) {
  const color = useColorModeValue("red.600", "red.300");
  return message ? <Text role="alert" fontSize="sm" color={color} wordBreak="break-word">{message}</Text> : null;
}

export const phaseLabels = {
  idle: "未启动", waiting: "等待目标进程", capturing: "正在采集",
  stopping: "停止采集", generating: "正在生成报告", completed: "已完成", error: "采集失败",
};

export function formatMetric(value: number | null, unit = "") {
  return value === null ? "不可用" : `${value.toFixed(1)}${unit}`;
}
