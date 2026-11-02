"use client";

import { Button, type ButtonProps } from "@chakra-ui/react";
import { useBackground } from "@/contexts/background-context";
import { useThemeColor } from "@/contexts/theme-color-context";
import { useLiquidGlassRefraction } from "@/components/special/liquid-glass-svg-filter";
import { LiquidGlassEdges } from "./liquid-glass-card";

type LiquidGlassButtonProps=ButtonProps;

export function LiquidGlassButton({ children, className, ...props }: LiquidGlassButtonProps) {
  const { liquidGlassEnabled, liquidGlassBlur, liquidGlassMode } = useBackground();
  const { getActiveColor, getHoverColor, getContrastTextColor } = useThemeColor();
  const { svgSupported } = useLiquidGlassRefraction(liquidGlassEnabled && liquidGlassMode === "real");

  const glassBg = getActiveColor();

  // 模糊立即生效：页面切换动画期间的 backdrop-filter 关闭由 .page-animating 类统一处理
  const effectiveBlur = liquidGlassEnabled ? liquidGlassBlur : 0;
  const isReal = liquidGlassEnabled && liquidGlassMode === "real";

  const backdropFilter = liquidGlassEnabled && !isReal ? `blur(${effectiveBlur}px)` : "none";

  return (
    <Button
      className={`jelly-bounce-button${isReal ? " real-liquid-glass" : ""}${className ? ` ${className}` : ""}`}
      bg={glassBg}
      color={getContrastTextColor()}
      border="1px solid"
      borderColor={liquidGlassEnabled ? getHoverColor() : glassBg}
      backdropFilter={backdropFilter}
      sx={{
        WebkitBackdropFilter: backdropFilter,
        transform: "translateZ(0)",
        WebkitTransform: "translateZ(0)",
        /* backfaceVisibility: hidden 删除 —— 合成层圆角 AA 丢失的主开关（问题 1） */
        willChange: "auto",
        transition: "border-color 0.45s cubic-bezier(0.4, 0, 0.2, 1), backdrop-filter 0.45s cubic-bezier(0.4, 0, 0.2, 1)",
      }}
      _hover={{
        bg: getActiveColor(),
        filter: "brightness(0.94)",
      }}
      {...props}
    >
      {isReal && svgSupported && <LiquidGlassEdges filter="url(#nexbox-liquid-glass-filter)" />}
      {children}
    </Button>
  );
}
