"use client";

import { Box, BoxProps, useColorModeValue } from "@chakra-ui/react";
import { useBackground } from "@/contexts/background-context";
import { getBorderGlowStyle } from "@/hooks/use-glow-effect";
import { useLiquidGlassRefraction } from "@/components/special/liquid-glass-svg-filter";
import { useMemo } from "react";

interface LiquidGlassCardProps extends BoxProps {
  children: React.ReactNode;
  className?: string;
  isDashed?: boolean;
  /** 强制启用液态玻璃效果（忽略全局开关） */
  forceGlass?: boolean;
}

export function LiquidGlassCard({
  children,
  className,
  isDashed = false,
  forceGlass = false,
  ...allProps
}: LiquidGlassCardProps) {
  // 问题 3 修复：调用方传入的 bg/borderColor/backdropFilter 多为针对普通模式
  // 硬编码的样式（如 borderColor="white"），若展开在 cardStyles 之后会覆盖组件
  // 按玻璃模式算好的值——real 模式下就变成「一圈不透明白边 + 透明背景」，看起来
  // 像玻璃没生效。外观类属性一律以组件计算值为准（当前无调用方合法使用它们）；
  // 布局类属性（p/flex/minH/…）仍由调用方覆盖。
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const { bg: _bg, borderColor: _borderColor, backdropFilter: _backdropFilter, WebkitBackdropFilter: _webkitBackdropFilter, ...props } = allProps as Record<string, unknown>;
  const { liquidGlassEnabled, liquidGlassBlur, liquidGlassMode } = useBackground();
  const glassOn = forceGlass || liquidGlassEnabled;
  const { svgSupported } = useLiquidGlassRefraction(glassOn && liquidGlassMode === "real");

  // 强制玻璃时使用明显的模糊值，避免全局默认 1px 看起来像没有效果
  const effectiveGlassBlur = forceGlass && !liquidGlassEnabled ? 16 : liquidGlassBlur;

  // 强制玻璃时背景稍微加一点不透明度，保证内容可读、不会透到看不清
  const glassBgColor = useColorModeValue(
    forceGlass ? "rgba(255,255,255,0.4)" : "rgba(255,255,255,0.25)",
    forceGlass ? "rgba(0,0,0,0.45)" : "rgba(0,0,0,0.25)"
  );
  const glassBorderColor = useColorModeValue(
    forceGlass ? "rgba(255,255,255,0.35)" : "rgba(255,255,255,0.2)",
    forceGlass ? "rgba(255,255,255,0.16)" : "rgba(255,255,255,0.1)"
  );
  const glowColor = useColorModeValue("rgba(255,255,255,0.8)", "rgba(255,255,255,0.5)");
  const defaultBg = useColorModeValue("white", "#111111");
  const defaultBorder = useColorModeValue("gray.200", "#333333");

  // 模糊立即生效：页面切换动画期间的 backdrop-filter 关闭由 .page-animating 类统一处理
  const effectiveBlur = glassOn ? effectiveGlassBlur : 0;
  const isReal = glassOn && liquidGlassMode === "real" && !isDashed;

  // 卡片主体：零滤镜
  const backdropFilter = useMemo(() => {
    if (!glassOn) return "none";
    if (isReal) return "none";
    return `blur(${effectiveBlur}px) saturate(1.3)`;
  }, [glassOn, isReal, effectiveBlur]);

  // 边缘折射条用的 filter
  const edgeFilter = svgSupported ? "url(#nexbox-liquid-glass-filter)" : "none";

  // 结构裁剪（Round 8，问题 1 圆角毛刺的根治）：backdrop-filter 从根元素挪到
  // zIndex:-1 的 ::before 底层，根只保留 borderRadius + overflow:hidden——
  // 裁剪发生在根的**绘制阶段**（根不是合成层），圆角走完整抗锯齿路径；
  // 旧方案根自身带 backdrop-filter 会被强制提升为合成层，圆角只能走无 AA 的
  // mask 裁剪（毛刺根因）。玻璃色随 backdropFilter 同层，视觉不变。
  const glassUnderlay = useMemo(() => ({
    content: '""',
    position: "absolute",
    inset: 0,
    zIndex: -1,
    borderRadius: "inherit",
    backdropFilter,
    WebkitBackdropFilter: backdropFilter,
    transition: "backdrop-filter 0.45s",
  }), [backdropFilter]);

  const cardStyles = useMemo(() => {
    const base: any = {
      // 玻璃色留在根：stacking context 内负 z 层画在根背景之上，模糊采样会把
      // 蒙版一起处理（均匀色模糊不变），视觉与旧版（bg+blur 同元素）等效
      bg: isReal ? "transparent" : (glassOn ? glassBgColor : defaultBg),
      borderRadius: "xl" as const,
      border: isDashed ? "1px dashed" : "1px solid",
      borderColor: isReal ? "rgba(255,255,255,0.06)" : (glassOn ? glassBorderColor : defaultBorder),
      overflow: "hidden",
      // zIndex:0 让根成为 stacking context：::before(-1) 被限制在卡片内部
      // （bg 之上、children 之下），否则 -1 会掉到页面级底层压掉模糊层
      zIndex: 0,
      sx: { "::before": glassUnderlay },
      transition: "background 0.45s, border-color 0.45s",
    };
    return base;
  }, [glassUnderlay, glassOn, glassBgColor, defaultBg, glassBorderColor, defaultBorder, isDashed, isReal]);

  const borderGlowStyle = useMemo(() => getBorderGlowStyle(glowColor), [glowColor]);

  return (
    <Box
      className={`jelly-bounce-card${isReal ? " real-liquid-glass" : ""}${className ? ` ${className}` : ""}`}
      {...cardStyles}
      position="relative"
      {...props}
    >
      {/* 边缘折射条 —— 独立叠加层，仅覆盖边缘 10px，卡片内部完全不受影响 */}
      {isReal && svgSupported && (
        <>
          <Box position="absolute" top={0} left={0} w="100%" h="10px" zIndex={0}
            backdropFilter={edgeFilter} sx={{ WebkitBackdropFilter: edgeFilter }}
            bg="transparent" pointerEvents="none" />
          <Box position="absolute" bottom={0} left={0} w="100%" h="10px" zIndex={0}
            backdropFilter={edgeFilter} sx={{ WebkitBackdropFilter: edgeFilter }}
            bg="transparent" pointerEvents="none" />
          <Box position="absolute" left={0} top={0} w="10px" h="100%" zIndex={0}
            backdropFilter={edgeFilter} sx={{ WebkitBackdropFilter: edgeFilter }}
            bg="transparent" pointerEvents="none" />
          <Box position="absolute" right={0} top={0} w="10px" h="100%" zIndex={0}
            backdropFilter={edgeFilter} sx={{ WebkitBackdropFilter: edgeFilter }}
            bg="transparent" pointerEvents="none" />
        </>
      )}

      {!isReal && (
        <Box
          style={borderGlowStyle}
          opacity={glassOn ? 1 : 0}
          transition="opacity 0.45s cubic-bezier(0.4, 0, 0.2, 1)"
        />
      )}
      {children}
    </Box>
  );
}
