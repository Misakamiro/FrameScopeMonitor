"use client";

import { Box, Text, useColorModeValue } from "@chakra-ui/react";
import { useState, useCallback, useRef, useEffect } from "react";
import { keyToHotkeyFormat } from "./hotkey-recorder";

/// 鼠标按键映射（左右键禁用，仅中键与侧键可作为快捷键）
function mouseButtonName(button: number): string | null {
  switch (button) {
    case 1: return "MouseMiddle";
    case 3: return "MouseX1";
    case 4: return "MouseX2";
    default: return null;
  }
}

const MODIFIER_LABELS = ["Ctrl", "Shift", "Alt", "Command"];

export function MouseHotkeyRecorder({
  value,
  onChange,
}: {
  value: string;
  onChange: (val: string) => void;
}) {
  const [isRecording, setIsRecording] = useState(false);
  const [displayText, setDisplayText] = useState("");
  const [isInvalid, setIsInvalid] = useState(false);
  const pendingRef = useRef<string[]>([]);
  const justCommittedRef = useRef(false);
  const textColor = useColorModeValue("gray.800", "#ffffff");
  const borderColor = useColorModeValue("gray.200", "#333333");
  const recordBg = useColorModeValue("teal.50", "rgba(0,150,136,0.1)");
  const recordBorder = useColorModeValue("teal.400", "teal.300");
  const invalidBg = useColorModeValue("red.50", "rgba(229,62,62,0.1)");
  const invalidBorder = useColorModeValue("red.400", "red.300");

  const commit = useCallback(
    (combo: string[]) => {
      if (combo.length > 0) {
        onChange(combo.join("+"));
      }
      setIsRecording(false);
      setDisplayText("");
      setIsInvalid(false);
      pendingRef.current = [];
      justCommittedRef.current = true;
    },
    [onChange]
  );

  const cancel = useCallback(() => {
    setIsRecording(false);
    setDisplayText("");
    setIsInvalid(false);
    pendingRef.current = [];
  }, []);

  useEffect(() => {
    if (!isRecording) return;

    const onMouseDown = (e: MouseEvent) => {
      e.preventDefault();
      e.stopPropagation();
      // 左右键不能作为快捷键，提示并保持录制状态
      if (e.button === 0 || e.button === 2) {
        setIsInvalid(true);
        setDisplayText(e.button === 0 ? "左键不可用" : "右键不可用");
        return;
      }
      const parts: string[] = [];
      if (e.ctrlKey) parts.push("Ctrl");
      if (e.shiftKey) parts.push("Shift");
      if (e.altKey) parts.push("Alt");
      if (e.metaKey) parts.push("Command");
      const btn = mouseButtonName(e.button);
      if (btn) parts.push(btn);
      if (parts.length > 0) {
        commit(parts);
      }
    };

    const onKeyDown = (e: KeyboardEvent) => {
      e.preventDefault();
      e.stopPropagation();
      setIsInvalid(false);
      const parts: string[] = [];
      if (e.ctrlKey) parts.push("Ctrl");
      if (e.shiftKey) parts.push("Shift");
      if (e.altKey) parts.push("Alt");
      if (e.metaKey) parts.push("Command");
      const nonModifier = e.key;
      if (!["Control", "Shift", "Alt", "Meta"].includes(nonModifier)) {
        const mapped = keyToHotkeyFormat(nonModifier);
        if (mapped) parts.push(mapped);
      }
      if (parts.length > 0) {
        pendingRef.current = parts;
        setDisplayText(parts.join("+"));
      }
    };

    const onKeyUp = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        cancel();
        return;
      }
      const combo = pendingRef.current;
      if (combo.length > 0) {
        const lastPart = combo[combo.length - 1];
        const hasMainKey = !MODIFIER_LABELS.includes(lastPart);
        if (hasMainKey) {
          commit(combo);
        }
      }
    };

    window.addEventListener("mousedown", onMouseDown, true);
    window.addEventListener("keydown", onKeyDown, true);
    window.addEventListener("keyup", onKeyUp, true);
    return () => {
      window.removeEventListener("mousedown", onMouseDown, true);
      window.removeEventListener("keydown", onKeyDown, true);
      window.removeEventListener("keyup", onKeyUp, true);
    };
  }, [isRecording, commit, cancel]);

  const startRecording = useCallback(() => {
    // 若刚通过鼠标录制提交（该次点击的 click 事件），忽略避免立即重新进入录制
    if (justCommittedRef.current) {
      justCommittedRef.current = false;
      return;
    }
    setIsRecording(true);
    setDisplayText("");
    setIsInvalid(false);
    pendingRef.current = [];
  }, []);

  return (
    <Box
      role="button"
      cursor="pointer"
      onClick={startRecording}
      px={3}
      py={2}
      borderRadius="lg"
      border="2px solid"
      borderColor={isRecording ? (isInvalid ? invalidBorder : recordBorder) : borderColor}
      bg={isRecording ? (isInvalid ? invalidBg : recordBg) : "transparent"}
      transition="all 0.2s"
      _hover={{ borderColor: isRecording ? (isInvalid ? invalidBorder : recordBorder) : borderColor }}
      outline="none"
      minW="180px"
      textAlign="center"
      userSelect="none"
    >
      {isRecording ? (
        <Text color={isInvalid ? "red.400" : "teal.400"} fontSize="sm" fontWeight="medium">
          {displayText || "按下快捷键..."}
        </Text>
      ) : (
        <Text color={textColor} fontSize="sm" fontWeight="medium">
          {value || "无"}
        </Text>
      )}
    </Box>
  );
}
