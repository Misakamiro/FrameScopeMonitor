"use client";

import { useColorModeValue } from "@chakra-ui/react";
import { useBackground } from "@/contexts/background-context";

export interface GlassSurfaceStyle {
  bg: string;
  border: string;
  borderColor: string;
  backdropFilter: string;
  WebkitBackdropFilter: string;
  /** real 模式下挂多层 inset 阴影（index.css），让透明卡片仍有可见边界 */
  className?: string;
}

/**
 * 裸 Box 卡片表面的玻璃模式样式（问题 3 的统一收敛点：自建卡片只读
 * liquidGlassEnabled、硬编码 bg/border，完全不认 liquidGlassMode）。
 *
 * - 玻璃关闭：不透明卡片底 + 常规边框（与旧硬编码一致）
 * - normal 模式：半透明玻璃底 + blur+saturate backdrop
 * - real 模式：透明底、无 blur、细边框 + real-liquid-glass 阴影 class
 *   （裸 Box 没有折射边缘层，靠阴影保持可见边界）
 *
 * 用法：const glass = useGlassSurfaceStyle(); <Box borderRadius="xl" {...glass} ... />
 */
export function useGlassSurfaceStyle(): GlassSurfaceStyle {
  const { liquidGlassEnabled, liquidGlassBlur, liquidGlassMode } = useBackground();

  const fallbackBg = useColorModeValue("white", "#111111");
  const fallbackBorder = useColorModeValue("gray.200", "#333333");
  const glassBg = useColorModeValue("rgba(255,255,255,0.25)", "rgba(0,0,0,0.25)");
  const glassBorder = useColorModeValue("rgba(255,255,255,0.2)", "rgba(255,255,255,0.1)");
  const realBorder = useColorModeValue("rgba(0,0,0,0.08)", "rgba(255,255,255,0.06)");

  const isReal = liquidGlassEnabled && liquidGlassMode === "real";
  if (!liquidGlassEnabled) {
    return {
      bg: fallbackBg,
      border: "1px solid",
      borderColor: fallbackBorder,
      backdropFilter: "none",
      WebkitBackdropFilter: "none",
    };
  }
  if (isReal) {
    return {
      bg: "transparent",
      border: "1px solid",
      borderColor: realBorder,
      backdropFilter: "none",
      WebkitBackdropFilter: "none",
      className: "real-liquid-glass",
    };
  }
  const backdrop = `blur(${liquidGlassBlur}px) saturate(1.3)`;
  return {
    bg: glassBg,
    border: "1px solid",
    borderColor: glassBorder,
    backdropFilter: backdrop,
    WebkitBackdropFilter: backdrop,
  };
}
