"use client";

import { Box, HStack, Text, useColorModeValue } from "@chakra-ui/react";
import { useBackground } from "@/contexts/background-context";
import { useThemeColor } from "@/contexts/theme-color-context";
import { useGlowEffect, getBorderGlowStyle } from "@/hooks/use-glow-effect";
import { useLiquidGlassRefraction } from "@/components/special/liquid-glass-svg-filter";

interface LiquidGlassMenuItemProps {
  children: React.ReactNode;
  isActive?: boolean;
  onClick?: () => void;
  icon?: React.ElementType;
}

export function LiquidGlassMenuItem({
  children,
  isActive = false,
  onClick,
  icon: Icon
}: LiquidGlassMenuItemProps) {
  const { liquidGlassEnabled, liquidGlassBlur, liquidGlassMode } = useBackground();
  const { getActiveColor, getBorderColor, getContrastTextColor } = useThemeColor();
  const { isHovering, handleMouseMove, handleMouseLeave, handleMouseEnter } = useGlowEffect();
  const { svgSupported } = useLiquidGlassRefraction(liquidGlassEnabled && liquidGlassMode === "real");

  const activeBg = getActiveColor();
  const activeTextFinal = getContrastTextColor();
  // 关闭液态玻璃时：设置子菜单作为独立面板，背景/文字跟随主题（深色模式保持深色）
  const inactiveText = useColorModeValue("#000000", "#ffffff");
  const glassInactiveText = useColorModeValue("gray.900", "#ffffff");
  const defaultInactiveBg = useColorModeValue("#ffffff", "#1a1a1a");
  const hoverBg = useColorModeValue("#f0f0f0", "#2a2a2a");

  const glassInactiveBg = useColorModeValue("rgba(255,255,255,0.25)", "rgba(0,0,0,0.25)");
  const glassHoverBg = useColorModeValue("rgba(255,255,255,0.35)", "rgba(0,0,0,0.35)");
  const glassBorderColor = useColorModeValue("rgba(255,255,255,0.25)", "rgba(255,255,255,0.12)");
  const glassActiveBorder = getBorderColor();
  const outlineColor = getActiveColor();
  const glowColor = useColorModeValue("rgba(255,255,255,0.8)", "rgba(255,255,255,0.5)");

  // 普通模式在底层模糊；透明模式只折射边缘，选中项仍保留主题色。
  const effectiveBlur = liquidGlassEnabled ? liquidGlassBlur : 0;
  const transition = "background 0.45s cubic-bezier(0.4, 0, 0.2, 1), border-color 0.45s cubic-bezier(0.4, 0, 0.2, 1)";
  const isReal = liquidGlassEnabled && liquidGlassMode === "real";

  const backdropFilter = liquidGlassEnabled && !isReal ? `blur(${effectiveBlur}px)` : "none";
  const edgeFilter = svgSupported ? "url(#nexbox-liquid-glass-filter)" : "none";

  return (
    <Box
      className={`jelly-bounce-menu-item${isReal ? " real-liquid-glass" : ""}`}
      onClick={onClick}
      cursor="pointer"
      borderRadius="lg"
      px={3}
      py={2.5}
      bg={isActive ? activeBg : (isReal ? "transparent" : (liquidGlassEnabled ? (isHovering ? glassHoverBg : glassInactiveBg) : (isHovering ? hoverBg : defaultInactiveBg)))}
      border="1px solid"
      borderColor={isActive ? (liquidGlassEnabled ? glassActiveBorder : activeBg) : (isReal && !isHovering ? "rgba(255,255,255,0.06)" : (liquidGlassEnabled ? glassBorderColor : "transparent"))}
      position="relative"
      overflow="hidden"
      zIndex={0}
      color={isActive ? activeTextFinal : (liquidGlassEnabled ? glassInactiveText : inactiveText)}
      onMouseMove={handleMouseMove}
      onMouseLeave={handleMouseLeave}
      onMouseEnter={handleMouseEnter}
      sx={{
        // 与 LiquidGlassCard 一致：根只裁剪，滤镜留在独立底层。
        "::before": {
          content: '""',
          position: "absolute",
          inset: 0,
          zIndex: -1,
          borderRadius: "inherit",
          backdropFilter,
          WebkitBackdropFilter: backdropFilter,
          transition: "backdrop-filter 0.45s",
        },
        transition,
      }}
      _focusVisible={{
        outline: "2px solid",
        outlineColor: outlineColor,
        outlineOffset: "2px"
      }}
    >
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
          style={getBorderGlowStyle(glowColor)}
          opacity={liquidGlassEnabled ? 1 : 0}
          transition="opacity 0.45s cubic-bezier(0.4, 0, 0.2, 1)"
        />
      )}
      <HStack position="relative" zIndex={1}>
        {Icon && <Icon size={18} />}
        <Text fontSize="sm" fontWeight={isActive ? "semibold" : "normal"}>
          {children}
        </Text>
      </HStack>
    </Box>
  );
}
