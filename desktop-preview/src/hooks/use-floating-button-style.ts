import { useMemo } from "react";

/**
 * 顶栏悬浮按钮的统一胶囊样式（与全局搜索/工具提示同思路）：
 * 半透明底色让文字在亮/暗壁纸上都保持可读。供直接铺在背景上的按钮使用。
 */
export function useFloatingButtonStyle() {
  const bg = 'var(--framescope-control-bg)';
  const bgHover = 'var(--framescope-control-hover)';
  const borderColor = 'var(--framescope-control-border)';
  const color = 'var(--framescope-control-fg)';
  return useMemo(() => ({
    bg,
    borderColor,
    color,
    _hover: { bg: bgHover },
  }), [bg, bgHover, borderColor, color]);
}
