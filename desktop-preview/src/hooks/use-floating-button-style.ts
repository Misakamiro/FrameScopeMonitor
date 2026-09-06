import { useMemo } from "react";
import { useColorModeValue } from "@chakra-ui/react";

/**
 * 顶栏悬浮按钮的统一胶囊样式（与全局搜索/工具提示同思路）：
 * 半透明底色让文字在亮/暗壁纸上都保持可读。供直接铺在背景上的按钮使用。
 */
export function useFloatingButtonStyle() {
  const bg = useColorModeValue("rgba(255,255,255,0.6)", "rgba(28,28,32,0.55)");
  const bgHover = useColorModeValue("rgba(255,255,255,0.85)", "rgba(28,28,32,0.75)");
  const borderColor = useColorModeValue("rgba(0,0,0,0.14)", "rgba(255,255,255,0.2)");
  const color = useColorModeValue("gray.800", "#f0f0f0");
  return useMemo(() => ({
    bg,
    borderColor,
    color,
    _hover: { bg: bgHover },
  }), [bg, bgHover, borderColor, color]);
}
