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
  const bg = 'var(--framescope-control-bg)';
  const color = 'var(--framescope-control-fg)';
  const borderColor = 'var(--framescope-control-border)';
  const placeholder = 'var(--framescope-surface-muted)';
  const icon = 'var(--framescope-surface-muted)';
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
export function PreviewDialog({ title, isOpen, onClose, children, size = 'md', closeDisabled = false }: {
  title: string; isOpen: boolean; onClose: () => void; children: ReactNode;
  size?: string; closeDisabled?: boolean;
}) {
  const overlayBg = 'rgba(0,0,0,.18)';
  const contentBg = 'var(--framescope-panel-bg)';
  const borderColor = 'var(--framescope-control-border)';
  const color = 'var(--framescope-surface-fg)';
  return (
    <Modal isOpen={isOpen} onClose={onClose} size={size} isCentered scrollBehavior="inside" closeOnOverlayClick={!closeDisabled} closeOnEsc={!closeDisabled}>
      <ModalOverlay bg={overlayBg} backdropFilter="none" />
      <ModalContent data-glass-surface="panel" bg={contentBg} color={color} border="1px solid" borderColor={borderColor}
        borderRadius="xl" mx={4} maxH="80vh" overflow="hidden">
        <ModalHeader fontSize="lg" overflowWrap="anywhere" pr={12}>{title}</ModalHeader>
        <ModalCloseButton isDisabled={closeDisabled} />
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
