import {
  Box,
  Flex,
  HStack,
  Text,
  Badge,
  VStack,
  Divider,
  useColorModeValue,
  Button,
  Slider,
  SliderTrack,
  SliderFilledTrack,
  SliderThumb,
  Tooltip,
} from "@chakra-ui/react";
import { useDynamicIsland } from "@/components/ui/dynamic-island";

import {
  LuMonitor,
  LuInfo,
  LuSettings,
  LuCheck,
  LuUpload,
  LuX,
  LuPalette,
  LuSlidersHorizontal,
} from "react-icons/lu";
import { useState, useRef, useEffect, useMemo, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { useBackground } from "@/contexts/background-context";
import { useThemeColor } from "@/contexts/theme-color-context";
import { useThemeMode } from "@/contexts/theme-mode-context";

import { useFont } from "@/contexts/font-context";
import { PRESET_COLORS, hexToRgba } from "@/lib/color-utils";
import { CustomColorPicker } from "@/components/special/custom-color-picker";
import { LiquidGlassCard } from "@/components/special/liquid-glass-card";
import { LiquidGlassButton } from "@/components/special/liquid-glass-button";
import { LiquidGlassMenuItem } from "@/components/special/liquid-glass-menu-item";
import { ThemeSwitch } from "@/components/special/theme-switch";
import { CustomSelect } from "@/components/special/custom-select";
import { convertFileSrc } from "@tauri-apps/api/core";
import { open } from "@tauri-apps/plugin-dialog";
import { useSearchParams } from "react-router-dom";
import {
  DndContext,
  closestCenter,
  PointerSensor,
  KeyboardSensor,
  useSensor,
  useSensors,
  DragEndEvent,
  type Modifier,
} from "@dnd-kit/core";
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { GripVertical } from "lucide-react";
import { store } from "@/lib/store";
import { FrameScopeSettings } from "@/preview/settings/FrameScopeSettings";
import { DEFAULT_NAV_ORDER, normalizeNavOrder } from "@/preview/settings/navigation";

// Adapted from NexBox 9.5.3 SettingsPage.tsx (c800dc5), GPL-3.0.

/** 限制导航栏拖拽只能沿竖直方向移动，禁止左右（向右）拖动 */
const restrictToVerticalAxis: Modifier = ({ transform }) => ({
  ...transform,
  x: 0,
});

const settingItems = [
  { id: "general", labelKey: "settings.general", icon: LuSettings },
  { id: "appearance", labelKey: "settings.appearance", icon: LuMonitor },
  { id: "preview", labelKey: "FrameScope", icon: LuSlidersHorizontal },
  { id: "about", labelKey: "settings.about", icon: LuInfo },
];

function SortableNavItem({
  id,
  label,
  visible,
  onToggle,
  dragHandleColor,
  labelColor,
  dragBg,
}: {
  id: string;
  label: string;
  visible: boolean;
  onToggle: () => void;
  dragHandleColor: string;
  labelColor: string;
  dragBg: string;
}) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id });

  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.8 : 1,
    zIndex: isDragging ? 10 : 1,
    background: isDragging ? dragBg : undefined,
    borderRadius: isDragging ? "md" : undefined,
  };

  return (
    <Box ref={setNodeRef} style={style}>
      <Divider />
      <HStack justify="space-between" py={2} pl={1}>
        <HStack spacing={2} flex={1}>
          <Box
            cursor="grab"
            color={dragHandleColor}
            _hover={{ color: labelColor }}
            _active={{ cursor: "grabbing" }}
            {...attributes}
            {...listeners}
            display="flex"
            alignItems="center"
          >
            <GripVertical size={16} />
          </Box>
          <Text fontSize="sm" color={labelColor} fontWeight="medium">
            {label}
          </Text>
        </HStack>
        <ThemeSwitch
          size="md"
          isChecked={visible}
          onChange={onToggle}
        />
      </HStack>
    </Box>
  );
}

function GeneralSettings() {
  const { t } = useTranslation();
  const toast = useDynamicIsland("settings");
  const { config, getContrastTextColor } = useThemeColor();
  const { liquidGlassEnabled, liquidGlassBlur } = useBackground();
  // 模糊立即生效：页面切换动画期间的 backdrop-filter 关闭由 .page-animating 类统一处理
  const effectiveBlur = liquidGlassEnabled ? liquidGlassBlur : 0;

  const [closeBehavior, setCloseBehavior] = useState<string>(() => {
    return localStorage.getItem("nexbox_close_behavior") === "minimize" ? "minimize" : "close";
  });
  const [sidebarShowLabel, setSidebarShowLabel] = useState(false);
  const [navVisibility, setNavVisibility] = useState<Record<string, boolean>>({});
  const [navPosition, setNavPosition] = useState<"left" | "top">("left");
  const NAV_ORDER_KEY = "nexbox_nav_order";
  const [navOrder, setNavOrder] = useState<string[]>(DEFAULT_NAV_ORDER);
  const navSensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );
  const dragHandleColor = useColorModeValue("gray.400", "gray.500");
  const sortableDragBg = useColorModeValue("blue.50", "rgba(59,130,246,0.1)");
  const [pageTransitionMode, setPageTransitionMode] = useState<"slide" | "fade" | "off">("fade");
  const titleColor = useColorModeValue("gray.800", "#ffffff");
  const labelColor = useColorModeValue("gray.700", "#ffffff");
  const subLabelColor = useColorModeValue("gray.500", "#ffffff");
  const segmentedControlBg = useColorModeValue(
    liquidGlassEnabled ? "rgba(255,255,255,0.24)" : "rgba(255,255,255,0.78)",
    liquidGlassEnabled ? "rgba(18,18,18,0.34)" : "rgba(18,18,18,0.58)"
  );
  const segmentedControlBorder = useColorModeValue(
    liquidGlassEnabled ? "rgba(255,255,255,0.34)" : "rgba(255,255,255,0.72)",
    liquidGlassEnabled ? "rgba(255,255,255,0.12)" : "rgba(255,255,255,0.08)"
  );
  const segmentedControlHoverBg = useColorModeValue(
    liquidGlassEnabled ? "rgba(255,255,255,0.18)" : "rgba(255,255,255,0.96)",
    liquidGlassEnabled ? "rgba(255,255,255,0.08)" : "rgba(255,255,255,0.08)"
  );
  const segmentedControlShadow = useColorModeValue(
    liquidGlassEnabled ? "0 16px 34px rgba(15, 23, 42, 0.14)" : "0 10px 30px rgba(15, 23, 42, 0.08)",
    liquidGlassEnabled ? "0 18px 38px rgba(0, 0, 0, 0.28)" : "0 12px 30px rgba(0, 0, 0, 0.24)"
  );
  const segmentedActiveBg = useColorModeValue(
    `linear-gradient(135deg, ${hexToRgba(config.primaryColor, liquidGlassEnabled ? 0.26 : 0.2)} 0%, ${hexToRgba(config.primaryColor, liquidGlassEnabled ? 0.42 : 0.34)} 100%)`,
    `linear-gradient(135deg, ${hexToRgba(config.primaryColor, liquidGlassEnabled ? 0.38 : 0.32)} 0%, ${hexToRgba(config.primaryColor, liquidGlassEnabled ? 0.24 : 0.18)} 100%)`
  );
  const segmentedActiveBorder = useColorModeValue(
    hexToRgba(config.primaryColor, liquidGlassEnabled ? 0.34 : 0.28),
    hexToRgba(config.primaryColor, liquidGlassEnabled ? 0.5 : 0.42)
  );
  const segmentedActiveShadow = useColorModeValue(
    `0 10px 24px ${hexToRgba(config.primaryColor, liquidGlassEnabled ? 0.22 : 0.18)}`,
    `0 12px 28px ${hexToRgba(config.primaryColor, liquidGlassEnabled ? 0.3 : 0.24)}`
  );
  const segmentedActiveOverlayGradient = useColorModeValue(
    liquidGlassEnabled
      ? "linear(to-b, rgba(255,255,255,0.5), rgba(255,255,255,0.12))"
      : "linear(to-b, rgba(255,255,255,0.42), rgba(255,255,255,0.08))",
    liquidGlassEnabled
      ? "linear(to-b, rgba(255,255,255,0.22), rgba(255,255,255,0.03))"
      : "linear(to-b, rgba(255,255,255,0.16), rgba(255,255,255,0.02))"
  );
  const segmentedContainerGlow = useColorModeValue(
    hexToRgba(config.primaryColor, liquidGlassEnabled ? 0.14 : 0.08),
    hexToRgba(config.primaryColor, liquidGlassEnabled ? 0.18 : 0.1)
  );
  const segmentedGlassSheen = useColorModeValue(
    "linear-gradient(135deg, rgba(255,255,255,0.58) 0%, rgba(255,255,255,0.08) 52%, rgba(255,255,255,0.02) 100%)",
    "linear-gradient(135deg, rgba(255,255,255,0.12) 0%, rgba(255,255,255,0.04) 52%, rgba(255,255,255,0.01) 100%)"
  );
  const segmentedActiveText = getContrastTextColor();

  const saveSetting = useCallback((key: string, value: string | boolean) => {
    localStorage.setItem(key, String(value));
    void store.set(key, value).then(() => store.save()).catch((error: unknown) => {
      toast({ title: "设置保存失败", description: String(error), status: "error", duration: 3000 });
    });
  }, [toast]);

  useEffect(() => {
    let disposed = false;
    async function loadSettings() {
      const read = async <T,>(key: string, fallback: T): Promise<T> =>
        (await store.get<T>(key)) ?? fallback;
      const [labels, position, transition, close, order, visibility] = await Promise.all([
        read("nexbox_sidebar_show_label", localStorage.getItem("nexbox_sidebar_show_label") === "true"),
        read("nexbox_nav_position", localStorage.getItem("nexbox_nav_position") || "left"),
        read("nexbox_page_transition", localStorage.getItem("nexbox_page_transition") || "fade"),
        read("nexbox_close_behavior", localStorage.getItem("nexbox_close_behavior") || "close"),
        read("nexbox_nav_order", localStorage.getItem("nexbox_nav_order")),
        Promise.all(DEFAULT_NAV_ORDER.map(async (path) => {
          const key = `nexbox_nav_visible_${path.replace(/\//g, "").replace(/-/g, "_")}`;
          return [path, await read(key, localStorage.getItem(key) !== "false")] as const;
        })),
      ]);
      if (disposed) return;
      setSidebarShowLabel(labels);
      setNavPosition(position === "top" ? "top" : "left");
      setPageTransitionMode(transition === "slide" || transition === "off" ? transition : "fade");
      setCloseBehavior(close === "minimize" ? "minimize" : "close");
      setNavOrder(normalizeNavOrder(order));
      setNavVisibility(Object.fromEntries(visibility));
    }
    void loadSettings().catch((error: unknown) => {
      if (!disposed) toast({ title: "设置读取失败", description: String(error), status: "error", duration: 3000 });
    });
    return () => { disposed = true; };
  }, [toast]);

  const handleCloseBehaviorChange = (value: string) => {
    setCloseBehavior(value);
    saveSetting("nexbox_close_behavior", value);
    window.dispatchEvent(new CustomEvent("close-behavior-changed"));
  };

  const handleSidebarShowLabelChange = (value: string) => {
    const newValue = value === "true";
    setSidebarShowLabel(newValue);
    saveSetting("nexbox_sidebar_show_label", newValue);
    window.dispatchEvent(new CustomEvent("sidebar-show-label-changed", { detail: newValue }));
  };

  const handleNavItemToggle = (path: string) => {
    const key = `nexbox_nav_visible_${path.replace(/\//g, "").replace(/-/g, "_")}`;
    const newValue = navVisibility[path] === false;
    setNavVisibility(prev => ({ ...prev, [path]: newValue }));
    saveSetting(key, newValue);
    window.dispatchEvent(new CustomEvent("nav-visibility-changed", { detail: { path, visible: newValue } }));
  };

  const handleNavDragEnd = useCallback((event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;
    const oldIndex = navOrder.indexOf(active.id as string);
    const newIndex = navOrder.indexOf(over.id as string);
    if (oldIndex === -1 || newIndex === -1) return;
    const newOrder = arrayMove(navOrder, oldIndex, newIndex);
    setNavOrder(newOrder);
    const str = JSON.stringify(newOrder);
    saveSetting(NAV_ORDER_KEY, str);
    window.dispatchEvent(new CustomEvent("nav-order-changed"));
  }, [navOrder, saveSetting]);

  const handleNavPositionChange = (value: string) => {
    const newValue = value as "left" | "top";
    setNavPosition(newValue);
    saveSetting("nexbox_nav_position", newValue);
    window.dispatchEvent(new CustomEvent("nav-position-changed", { detail: newValue }));
  };


  const handlePageTransitionChange = (newMode: "slide" | "fade" | "off") => {
    setPageTransitionMode(newMode);
    saveSetting("nexbox_page_transition", newMode);
    window.dispatchEvent(new CustomEvent("page-transition-setting-changed", { detail: newMode }));
  };

  return (
    <Box>
      <Text fontSize="lg" fontWeight="bold" mb={6} color={titleColor}>
        {t("settings.generalSettings.title")}
      </Text>

      <Box mb={6}>
        <Text
          fontSize="xs"
          fontWeight="semibold"
          color={subLabelColor}
          mb={3}
          textTransform="uppercase"
          letterSpacing="0.05em"
        >
          {t("settings.generalSettings.pageTransitionLabel")}
        </Text>
        <LiquidGlassCard px={4} py={3} boxShadow="sm">
          <VStack spacing={0} align="stretch">
            <HStack justify="space-between" py={2}>
              <Box flex={1}>
                <Text fontSize="sm" color={labelColor} fontWeight="medium">
                  {t("settings.generalSettings.pageTransitionLabel")}
                </Text>
                <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                  {t("settings.generalSettings.pageTransitionDesc")}
                </Text>
              </Box>
              <HStack
                spacing={1}
                p={1}
                borderRadius="xl"
                border="1px solid"
                borderColor={segmentedControlBorder}
                bg={segmentedControlBg}
                boxShadow={segmentedControlShadow}
                backdropFilter={liquidGlassEnabled ? `blur(${effectiveBlur}px) saturate(160%)` : "blur(14px)"}
                transition="backdrop-filter 0.45s cubic-bezier(0.4, 0, 0.2, 1)"
                position="relative"
                overflow="hidden"
              >
                {liquidGlassEnabled && (
                  <Box
                    position="absolute"
                    inset="0"
                    pointerEvents="none"
                    bgGradient={segmentedGlassSheen}
                    opacity={0.9}
                  />
                )}
                <Box
                  position="absolute"
                  inset="0"
                  pointerEvents="none"
                  borderRadius="inherit"
                  boxShadow={`inset 0 1px 0 rgba(255,255,255,0.28), inset 0 0 0 1px ${segmentedContainerGlow}`}
                  opacity={liquidGlassEnabled ? 1 : 0.72}
                />
                {(["slide", "fade", "off"] as const).map((mode) => (
                  <Box
                    key={mode}
                    as="button"
                    type="button"
                    minW="74px"
                    px={3.5}
                    py={2}
                    borderRadius="lg"
                    border="1px solid"
                    borderColor={pageTransitionMode === mode ? segmentedActiveBorder : "transparent"}
                    bg={pageTransitionMode === mode ? segmentedActiveBg : "transparent"}
                    color={pageTransitionMode === mode ? segmentedActiveText : subLabelColor}
                    fontSize="sm"
                    fontWeight={pageTransitionMode === mode ? "semibold" : "medium"}
                    letterSpacing="0.01em"
                    boxShadow={pageTransitionMode === mode ? segmentedActiveShadow : "none"}
                    position="relative"
                    transition="color 0.16s ease, transform 0.16s ease"
                    transform={pageTransitionMode === mode ? "translateY(-1px)" : "translateY(0)"}
                    _hover={{
                      bg: pageTransitionMode === mode ? segmentedActiveBg : segmentedControlHoverBg,
                      color: pageTransitionMode === mode ? segmentedActiveText : labelColor,
                    }}
                    _active={{
                      transform: pageTransitionMode === mode ? "translateY(0)" : "scale(0.98)",
                    }}
                    _focusVisible={{
                      outline: "none",
                      boxShadow: `0 0 0 3px ${hexToRgba(config.primaryColor, 0.24)}`,
                    }}
                    aria-pressed={pageTransitionMode === mode}
                    onClick={() => handlePageTransitionChange(mode)}
                  >
                    <Box
                      position="absolute"
                      inset="1px"
                      borderRadius="inherit"
                      opacity={pageTransitionMode === mode ? 1 : 0}
                      transition="none"
                      pointerEvents="none"
                      bgGradient={segmentedActiveOverlayGradient}
                    />
                    <Text position="relative" zIndex={1}>
                      {mode === "slide" ? t("settings.generalSettings.pageTransitionSlide", "滑动") :
                       mode === "fade" ? t("settings.generalSettings.pageTransitionFade", "淡化") :
                       t("settings.generalSettings.pageTransitionOff", "关闭")}
                    </Text>
                  </Box>
                ))}
              </HStack>
            </HStack>
          </VStack>
        </LiquidGlassCard>
      </Box>

      <Box mb={6}>
        <Text
          fontSize="xs"
          fontWeight="semibold"
          color={subLabelColor}
          mb={3}
          textTransform="uppercase"
          letterSpacing="0.05em"
        >
          {t("settings.generalSettings.closeBehavior")}
        </Text>
        <LiquidGlassCard px={4} py={3} boxShadow="sm">
          <HStack justify="space-between" py={2}>
            <Box flex={1}>
              <Text fontSize="sm" color={labelColor} fontWeight="medium">
                {t("settings.generalSettings.closeBehaviorLabel")}
              </Text>
              <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                {t("settings.generalSettings.closeBehaviorDesc")}
              </Text>
            </Box>
            <CustomSelect
              value={closeBehavior}
              onChange={handleCloseBehaviorChange}
              options={[
                { value: "close", label: t("settings.generalSettings.closeDirectly") },
                { value: "minimize", label: t("settings.generalSettings.minimizeToTray") },
              ]}
              width="140px"
            />
          </HStack>
        </LiquidGlassCard>
      </Box>

      <Box mb={6}>
        <Text
          fontSize="xs"
          fontWeight="semibold"
          color={subLabelColor}
          mb={3}
          textTransform="uppercase"
          letterSpacing="0.05em"
        >
          {t("settings.navigation")}
        </Text>
        <LiquidGlassCard px={4} py={3} boxShadow="sm">
          <VStack spacing={0} align="stretch">
            <HStack justify="space-between" py={2}>
              <Box flex={1}>
                <Text fontSize="sm" color={labelColor} fontWeight="medium">
                  {t("settings.generalSettings.navPositionLabel")}
                  <Badge
                    ml={1.5}
                    fontSize="0.6rem"
                    variant="subtle"
                    px={1.5}
                    py={0.5}
                    borderRadius="full"
                    color={config.primaryColor}
                    bg={hexToRgba(config.primaryColor, 0.15)}
                  >
                    BETA
                  </Badge>
                </Text>
                <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                  {t("settings.generalSettings.navPositionDesc")}
                </Text>
              </Box>
              <CustomSelect
                value={navPosition}
                onChange={handleNavPositionChange}
                options={[
                  { value: "left", label: t("settings.generalSettings.navPositionLeft") },
                  { value: "top", label: t("settings.generalSettings.navPositionTop") },
                ]}
                width="100px"
              />
            </HStack>
            <Divider />
            <HStack justify="space-between" py={2}>
              <Box flex={1}>
                <Text fontSize="sm" color={labelColor} fontWeight="medium">
                  {t("settings.generalSettings.sidebarShowLabel")}
                </Text>
                <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                  {t("settings.generalSettings.sidebarShowLabelDesc")}
                </Text>
              </Box>
              <CustomSelect
                value={String(sidebarShowLabel)}
                onChange={handleSidebarShowLabelChange}
                options={[
                  { value: "false", label: t("settings.generalSettings.sidebarShowLabelNoText") },
                  { value: "true", label: t("settings.generalSettings.sidebarShowLabelWithText") },
                ]}
                width="100px"
              />
            </HStack>
            <Divider />
            <DndContext sensors={navSensors} collisionDetection={closestCenter} onDragEnd={handleNavDragEnd} modifiers={[restrictToVerticalAxis]}>
              <SortableContext items={navOrder} strategy={verticalListSortingStrategy}>
                {navOrder.map((path) => {
                  const labelMap: Record<string, string> = {
                    "/hardware": t("sidebar.hardware"),
                    "/": "总览",
                    "/targets": "监控目标",
                    "/reports": "报告",
                    "/overlay-panel": "悬浮面板",
                    "/settings": t("sidebar.settings", "设置"),
                  };
                  return (
                    <SortableNavItem
                      key={path}
                      id={path}
                      label={labelMap[path] || path}
                      visible={navVisibility[path] !== false}
                      onToggle={() => handleNavItemToggle(path)}
                      dragHandleColor={dragHandleColor}
                      labelColor={labelColor}
                      dragBg={sortableDragBg}
                    />
                  );
                })}
              </SortableContext>
            </DndContext>
          </VStack>
        </LiquidGlassCard>
      </Box>
    </Box>
  );
}

function ThemeColorSettings() {
  const { t } = useTranslation();
  const {
    config,
    setPrimaryColor,
    resetToDefault,
  } = useThemeColor();
  
  const labelColor = useColorModeValue("gray.700", "#ffffff");
  const subLabelColor = useColorModeValue("gray.500", "#ffffff");
  const cardBorder = useColorModeValue("gray.200", "#333333");
  const presetBorderColor = useColorModeValue("gray.200", "#444444");
  const presetActiveBorderColor = useColorModeValue("gray.400", "#666666");
  
  const handlePresetClick = (color: string) => {
    setPrimaryColor(color);
  };
  
  return (
    <Box mb={6}>
      <Text
        fontSize="xs"
        fontWeight="semibold"
        color={subLabelColor}
        mb={3}
        textTransform="uppercase"
        letterSpacing="0.05em"
      >
        {t("settings.appearanceSettings.themeColor")}
      </Text>
      <LiquidGlassCard px={4} py={3} boxShadow="sm">
        <VStack spacing={4} align="stretch">
          <Box>
            <HStack mb={2}>
              <LuPalette size={14} />
              <Text fontSize="sm" color={labelColor} fontWeight="medium">
                {t("settings.appearanceSettings.presets")}
              </Text>
            </HStack>
            <HStack spacing={2} flexWrap="wrap">
              {PRESET_COLORS.map((preset) => (
                <Tooltip key={preset.value} label={t(preset.labelKey)}>
                  <Box
                    w="32px"
                    h="32px"
                    borderRadius="lg"
                    bg={preset.value}
                    cursor="pointer"
                    border="2px solid"
                    borderColor={config.primaryColor === preset.value ? presetActiveBorderColor : presetBorderColor}
                    boxShadow={config.primaryColor === preset.value ? "0 0 0 2px rgba(255,255,255,0.2)" : "none"}
                    onClick={() => handlePresetClick(preset.value)}
                    transition="all 0.2s"
                    _hover={{ transform: "scale(1.1)" }}
                  />
                </Tooltip>
              ))}
            </HStack>
          </Box>
          
          <Divider borderColor={cardBorder} />
          
          <Box>
            <Text fontSize="xs" color={subLabelColor} mb={2}>
              {t("settings.appearanceSettings.customColor")}
            </Text>
            <CustomColorPicker color={config.primaryColor} onChange={setPrimaryColor} />
          </Box>
          
          <HStack justify="flex-end">
            <Button
              size="xs"
              variant="ghost"
              onClick={resetToDefault}
            >
              {t("settings.appearanceSettings.resetToDefault")}
            </Button>
          </HStack>
        </VStack>
      </LiquidGlassCard>
    </Box>
  );
}

function AppearanceSettings() {
  const { t } = useTranslation();
  const { getActiveColor } = useThemeColor();
  const { font, setFont, fontOptions, importCustomFont, removeCustomFont, importing } = useFont();
  const {
    backgroundMode,
    customBgImages,
    activeBgIndex,
    dynamicBgVideo,
    setBackgroundMode,
    addCustomBgImage,
    removeCustomBgImage,
    setActiveBgIndex,
    setDynamicBgVideo,
    liquidGlassEnabled,
    setLiquidGlassEnabled,
    liquidGlassBlur,
    setLiquidGlassBlur,
    liquidGlassMode,
    setLiquidGlassMode,
    islandLiquidGlassEnabled,
    setIslandLiquidGlassEnabled,
    backgroundBlur,
    setBackgroundBlur,
    activePresetIndex,
    presetBackgrounds,
    setActivePresetIndex,
    carouselEnabled,
    setCarouselEnabled,
    jellyBounceEnabled,
    setJellyBounceEnabled,
    mrColorMode,
    mrCustomColor,
    setMrColorMode,
    setMrCustomColor,
  } = useBackground();
  const videoPreviewSrc = useMemo(() => dynamicBgVideo ? convertFileSrc(dynamicBgVideo) : null, [dynamicBgVideo]);
  const titleColor = useColorModeValue("gray.800", "#ffffff");
  const cardBorder = useColorModeValue("gray.200", "#333333");
  const labelColor = useColorModeValue("gray.700", "#ffffff");
  const subLabelColor = useColorModeValue("gray.500", "#ffffff");
  const emptySlotBg = useColorModeValue("gray.100", "#1a1a1a");
  const emptySlotBorder = useColorModeValue("gray.200", "#333333");
  const activeSlotBorder = useColorModeValue("blue.400", "blue.300");
  const modeButtonActiveBg = useColorModeValue("blue.500", "blue.400");
  const modeButtonActiveColor = useColorModeValue("gray.800", "white");
  const modeButtonInactiveBg = useColorModeValue("gray.100", "#1a1a1a");
  const modeButtonInactiveBorder = useColorModeValue("gray.200", "#333333");
  const glassSectionBorder = useColorModeValue("rgba(0,0,0,0.08)", "rgba(255,255,255,0.08)");
  const toast = useDynamicIsland("settings");

  const themeOptions = [
    { value: "system", label: "跟随系统" },
    { value: "light", label: "浅色" },
    { value: "dark", label: "深色" },
  ];

  const { themeMode, setThemeMode } = useThemeMode();

  const handleThemeChange = (value: string) => {
    setThemeMode(value as "system" | "light" | "dark");
  };

  const handleImageUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      const reader = new FileReader();
      reader.onloadend = () => {
        const result = reader.result as string;
        const success = addCustomBgImage(result);
        if (!success) {
          toast({
            title: t("settings.appearanceSettings.maxImagesReached") || "最多只能添加3张背景图片",
            status: "warning",
            duration: 2000,
            isClosable: true,
          });
        }
      };
      reader.readAsDataURL(file);
    }
    e.target.value = "";
  };

  const handleVideoUpload = async () => {
    try {
      const filePath = await open({
        multiple: false,
        filters: [{ name: "Video", extensions: ["mp4", "webm"] }],
      });
      if (filePath) {
        const ext = filePath.split(".").pop()?.toLowerCase();
        const supportedFormats = ["mp4", "webm"];
        if (ext && !supportedFormats.includes(ext)) {
          toast({
            title: t("settings.appearanceSettings.unsupportedVideoFormat"),
            status: "warning",
            duration: 3000,
            isClosable: true,
          });
          return;
        }
        setDynamicBgVideo(filePath);
      }
    } catch (error) {
      console.error("选择视频文件失败:", error);
      toast({ title: "选择视频文件失败", description: String(error), status: "error", duration: 3000 });
    }
  };

  const handleAddClick = () => {
    if (customBgImages.length >= 3) {
      toast({
        title: t("settings.appearanceSettings.maxImagesReached") || "最多只能添加3张背景图片",
        status: "warning",
        duration: 2000,
        isClosable: true,
      });
      return;
    }
    const input = document.getElementById("bg-upload-new") as HTMLInputElement;
    input?.click();
  };

  const handleVideoAddClick = () => {
    handleVideoUpload();
  };

  return (
    <Box>
      <Text fontSize="lg" fontWeight="bold" mb={6} color={titleColor}>
        {t("settings.appearanceSettings.title")}
      </Text>

      <Box mb={6}>
        <Text
          fontSize="xs"
          fontWeight="semibold"
          color={subLabelColor}
          mb={3}
          textTransform="uppercase"
          letterSpacing="0.05em"
        >
          {t("settings.appearanceSettings.theme")}
        </Text>
        <LiquidGlassCard px={4} py={3} boxShadow="sm">
          <HStack justify="space-between">
            <Text fontSize="sm" color={labelColor}>
              {t("settings.appearanceSettings.themeStyle")}
            </Text>
            <CustomSelect
              value={themeMode}
              onChange={handleThemeChange}
              options={themeOptions}
              width="140px"
            />
          </HStack>
        </LiquidGlassCard>
      </Box>

      <Box mb={6}>
        <Text
          fontSize="xs"
          fontWeight="semibold"
          color={subLabelColor}
          mb={3}
          textTransform="uppercase"
          letterSpacing="0.05em"
        >
          {t("settings.appearanceSettings.font")}
        </Text>
        <LiquidGlassCard px={4} py={3} boxShadow="sm">
          <VStack spacing={0} align="stretch">
            <HStack justify="space-between">
              <Box flex={1}>
                <Text fontSize="sm" color={labelColor} fontWeight="medium">
                  {t("settings.appearanceSettings.fontLabel")}
                </Text>
                <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                  {t("settings.appearanceSettings.fontDesc")}
                </Text>
              </Box>
              <CustomSelect
                value={font}
                onChange={(val) => setFont(val)}
                options={fontOptions.map((opt) => ({
                  value: opt.value,
                  label: opt.label,
                }))}
                width="180px"
              />
            </HStack>
            <Divider />
            <HStack justify="space-between" py={2}>
              <Box flex={1}>
                <Text fontSize="sm" color={labelColor} fontWeight="medium">
                  {t("settings.appearanceSettings.importCustomFontLabel")}
                </Text>
                <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                  {t("settings.appearanceSettings.importCustomFontDesc")}
                </Text>
              </Box>
              <HStack spacing={2}>
                {importing && <Text fontSize="xs" color={subLabelColor}>{t("settings.appearanceSettings.importingFont")}</Text>}
                <LiquidGlassButton
                  size="xs"
                  variant="outline"
                  borderRadius="md"
                  leftIcon={<LuUpload size={12} />}
                  isLoading={importing}
                  onClick={() => {
                    const input = document.getElementById("custom-font-upload") as HTMLInputElement;
                    input?.click();
                  }}
                >
                  {t("settings.appearanceSettings.importFontButton")}
                </LiquidGlassButton>
              </HStack>
            </HStack>
            {fontOptions.filter(f => f.isCustom).length > 0 && (
              <>
                <Divider />
                {fontOptions.filter(f => f.isCustom).map((cf) => (
                  <HStack key={cf.value} justify="space-between" py={2}>
                    <Text fontSize="sm" color={labelColor} fontWeight="medium">
                      {cf.label}
                    </Text>
                    <HStack spacing={2}>
                      <Tooltip label={t("settings.appearanceSettings.removeFont")}>
                        <Box
                          w="24px"
                          h="24px"
                          borderRadius="md"
                          display="flex"
                          alignItems="center"
                          justifyContent="center"
                          cursor="pointer"
                          transition="all 0.2s"
                          _hover={{ bg: "rgba(255,0,0,0.1)" }}
                          onClick={() => removeCustomFont(cf.value)}
                        >
                          <LuX size={14} color="red" />
                        </Box>
                      </Tooltip>
                    </HStack>
                  </HStack>
                ))}
              </>
            )}
          </VStack>
        </LiquidGlassCard>
        <input
          id="custom-font-upload"
          type="file"
          accept=".ttf,.otf,.woff,.woff2"
          style={{ display: "none" }}
          onChange={async (e) => {
            const file = e.target.files?.[0];
            if (file) {
              try {
                await importCustomFont(file);
              } catch (err) {
                if (err instanceof Error && err.message === "DUPLICATE_FONT") {
                  toast({
                    title: t("settings.appearanceSettings.duplicateFont"),
                    status: "warning",
                    duration: 2000,
                    isClosable: true,
                  });
                } else {
                  toast({
                    title: t("settings.appearanceSettings.importFontFailed"),
                    status: "error",
                    duration: 2000,
                    isClosable: true,
                  });
                }
              }
            }
            e.target.value = "";
          }}
        />
      </Box>

      <Box mb={6}>
        <Text
          fontSize="xs"
          fontWeight="semibold"
          color={subLabelColor}
          mb={3}
          textTransform="uppercase"
          letterSpacing="0.05em"
        >
          {t("settings.appearanceSettings.liquidGlass")}
          <Badge
            ml={2}
            fontSize="0.6rem"
            colorScheme="purple"
            variant="subtle"
            px={2}
            py={0.5}
            borderRadius="full"
          >
            BETA
          </Badge>
        </Text>
        <LiquidGlassCard px={4} py={3} boxShadow="sm">
          <HStack justify="space-between">
            <Box flex={1}>
              <Text fontSize="sm" color={labelColor} fontWeight="medium">
                {t("settings.appearanceSettings.liquidGlassLabel")}
              </Text>
              <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                {t("settings.appearanceSettings.liquidGlassDesc")}
              </Text>
            </Box>
            <ThemeSwitch
              size="md"
              isChecked={liquidGlassEnabled}
              onChange={() => setLiquidGlassEnabled(!liquidGlassEnabled)}
            />
          </HStack>
          {liquidGlassEnabled && (
            <Box mt={4} pt={3} borderTop="1px solid" borderColor={useColorModeValue("rgba(0,0,0,0.08)", "rgba(255,255,255,0.08)")}>
              <HStack justify="space-between" mb={2}>
                <Text fontSize="sm" color={labelColor}>
                  {t("settings.appearanceSettings.liquidGlassModeLabel")}
                </Text>
                <CustomSelect
                  value={liquidGlassMode}
                  onChange={(val) => setLiquidGlassMode(val as "normal" | "real")}
                  width="160px"
                  options={[
                    { value: "normal", label: t("settings.appearanceSettings.liquidGlassModeNormal") },
                    { value: "real", label: t("settings.appearanceSettings.liquidGlassModeReal") },
                  ]}
                />
              </HStack>
              <Text fontSize="xs" color={subLabelColor} mt={1}>
                {liquidGlassMode === "real"
                  ? t("settings.appearanceSettings.liquidGlassModeRealDesc")
                  : t("settings.appearanceSettings.liquidGlassModeNormalDesc")}
              </Text>
            </Box>
          )}
          {liquidGlassEnabled && liquidGlassMode === "normal" && (
            <Box mt={4} pt={3} borderTop="1px solid" borderColor={useColorModeValue("rgba(0,0,0,0.08)", "rgba(255,255,255,0.08)")}>
              <HStack justify="space-between" mb={2}>
                <Text fontSize="sm" color={labelColor}>
                  {t("settings.appearanceSettings.liquidGlassBlurLabel")}
                </Text>
                <Text fontSize="sm" color={getActiveColor()} fontWeight="bold">{liquidGlassBlur}</Text>
              </HStack>
              <Slider
                value={liquidGlassBlur}
                min={1}
                max={100}
                step={1}
                onChange={(val) => setLiquidGlassBlur(val)}
              >
                <SliderTrack bg={useColorModeValue("gray.200", "gray.700")}>
                  <SliderFilledTrack bg={getActiveColor()} />
                </SliderTrack>
                <SliderThumb />
              </Slider>
            </Box>
          )}
          {liquidGlassEnabled && (
            <Box mt={4} pt={3} borderTop="1px solid" borderColor={glassSectionBorder}>
              <HStack justify="space-between">
                <Box flex={1}>
                  <Text fontSize="sm" color={labelColor} fontWeight="medium">
                    {t("settings.appearanceSettings.islandLiquidGlassLabel")}
                  </Text>
                  <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                    {t("settings.appearanceSettings.islandLiquidGlassDesc")}
                  </Text>
                </Box>
                <ThemeSwitch
                  size="md"
                  isChecked={islandLiquidGlassEnabled}
                  onChange={() => setIslandLiquidGlassEnabled(!islandLiquidGlassEnabled)}
                />
              </HStack>
            </Box>
          )}
        </LiquidGlassCard>
      </Box>

      <Box mb={6}>
        <Text
          fontSize="xs"
          fontWeight="semibold"
          color={subLabelColor}
          mb={3}
          textTransform="uppercase"
          letterSpacing="0.05em"
        >
          {t("settings.appearanceSettings.jellyBounce")}
          <Badge
            ml={2}
            fontSize="0.6rem"
            colorScheme="pink"
            variant="subtle"
            px={2}
            py={0.5}
            borderRadius="full"
          >
            BETA
          </Badge>
        </Text>
        <LiquidGlassCard px={4} py={3} boxShadow="sm">
          <HStack justify="space-between">
            <Box flex={1}>
              <Text fontSize="sm" color={labelColor} fontWeight="medium">
                {t("settings.appearanceSettings.jellyBounceLabel")}
              </Text>
              <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                {t("settings.appearanceSettings.jellyBounceDesc")}
              </Text>
            </Box>
            <ThemeSwitch
              size="md"
              isChecked={jellyBounceEnabled}
              onChange={() => setJellyBounceEnabled(!jellyBounceEnabled)}
            />
          </HStack>
        </LiquidGlassCard>
      </Box>

      <ThemeColorSettings />

      <Box mb={6}>
        <Text
          fontSize="xs"
          fontWeight="semibold"
          color={subLabelColor}
          mb={3}
          textTransform="uppercase"
          letterSpacing="0.05em"
        >
          {t("settings.appearanceSettings.customBackground")}
        </Text>
        <LiquidGlassCard px={4} py={3} boxShadow="sm">
          <VStack spacing={4} align="stretch">
            <HStack justify="space-between">
              <Text fontSize="sm" color={labelColor} fontWeight="medium">
                {t("settings.appearanceSettings.customBackgroundLabel")}
              </Text>
              <ThemeSwitch
                size="md"
                isChecked={backgroundMode !== "none"}
                onChange={() => {
                  if (backgroundMode === "none") {
                    setBackgroundMode("image");
                  } else {
                    setBackgroundMode("none");
                  }
                }}
              />
            </HStack>

            {backgroundMode !== "none" && (
              <>
                <Divider borderColor={cardBorder} />

                <HStack spacing={2}>
                  <LiquidGlassCard
                    flex={1}
                    px={3}
                    py={2}
                    cursor="pointer"
                    onClick={() => setBackgroundMode("preset")}
                    border="1px solid"
                    borderColor={backgroundMode === "preset" ? getActiveColor() : "transparent"}
                    bg={backgroundMode === "preset" ? `${getActiveColor()}15` : "transparent"}
                  >
                    <Text
                      fontSize="sm"
                      color={backgroundMode === "preset" ? modeButtonActiveColor : labelColor}
                      textAlign="center"
                      fontWeight="medium"
                    >
                      {t("settings.appearanceSettings.presetBackground")}
                    </Text>
                  </LiquidGlassCard>
                  <LiquidGlassCard
                    flex={1}
                    px={3}
                    py={2}
                    cursor="pointer"
                    onClick={() => setBackgroundMode("image")}
                    border="1px solid"
                    borderColor={backgroundMode === "image" ? getActiveColor() : "transparent"}
                    bg={backgroundMode === "image" ? `${getActiveColor()}15` : "transparent"}
                  >
                    <Text
                      fontSize="sm"
                      color={backgroundMode === "image" ? modeButtonActiveColor : labelColor}
                      textAlign="center"
                      fontWeight="medium"
                    >
                      {t("settings.appearanceSettings.imageBackground")}
                    </Text>
                  </LiquidGlassCard>
                  <LiquidGlassCard
                    flex={1}
                    px={3}
                    py={2}
                    cursor="pointer"
                    onClick={() => setBackgroundMode("dynamic")}
                    border="1px solid"
                    borderColor={backgroundMode === "dynamic" ? getActiveColor() : "transparent"}
                    bg={backgroundMode === "dynamic" ? `${getActiveColor()}15` : "transparent"}
                  >
                    <Text
                      fontSize="sm"
                      color={backgroundMode === "dynamic" ? modeButtonActiveColor : labelColor}
                      textAlign="center"
                      fontWeight="medium"
                    >
                      {t("settings.appearanceSettings.dynamicBackground")}
                    </Text>
                  </LiquidGlassCard>
                  <LiquidGlassCard
                    flex={1}
                    px={3}
                    py={2}
                    cursor="pointer"
                    onClick={() => setBackgroundMode("mr")}
                    border="1px solid"
                    borderColor={backgroundMode === "mr" ? getActiveColor() : "transparent"}
                    bg={backgroundMode === "mr" ? `${getActiveColor()}15` : "transparent"}
                  >
                    <Text
                      fontSize="sm"
                      color={backgroundMode === "mr" ? modeButtonActiveColor : labelColor}
                      textAlign="center"
                      fontWeight="medium"
                    >
                      {t("settings.appearanceSettings.mrBackground")}
                    </Text>
                  </LiquidGlassCard>
                </HStack>

                {backgroundMode === "preset" && (
                  <>
                    <HStack justify="space-between" w="full">
                      <Box>
                        <Text fontSize="sm" color={labelColor} fontWeight="medium">
                          {t("settings.appearanceSettings.carouselLabel")}
                        </Text>
                        <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                          {t("settings.appearanceSettings.carouselDesc")}
                        </Text>
                      </Box>
                      <ThemeSwitch
                        size="md"
                        isChecked={carouselEnabled}
                        onChange={() => setCarouselEnabled(!carouselEnabled)}
                      />
                    </HStack>
                    <HStack spacing={2} justify="flex-end">
                      {presetBackgrounds.map((preset, index) => (
                        <Box
                          key={preset.id}
                          position="relative"
                          w="160px"
                          h="90px"
                          borderRadius="lg"
                          overflow="hidden"
                          border="2px solid"
                          borderColor={index === activePresetIndex ? activeSlotBorder : emptySlotBorder}
                          cursor="pointer"
                          onClick={() => setActivePresetIndex(index)}
                          transition="all 0.2s"
                          _hover={{ borderColor: activeSlotBorder, transform: "scale(1.02)" }}
                        >
                          <img
                            src={preset.path}
                            alt={preset.name}
                            style={{
                              width: "100%",
                              height: "100%",
                              objectFit: "cover",
                            }}
                          />
                          {index === activePresetIndex && (
                            <Box
                              position="absolute"
                              bottom={1}
                              left="50%"
                              transform="translateX(-50%)"
                              bg="blue.500"
                              borderRadius="full"
                              px={1.5}
                              py={0.5}
                            >
                              <LuCheck size={10} color="white" />
                            </Box>
                          )}
                        </Box>
                      ))}
                    </HStack>
                  </>
                )}

                {backgroundMode === "image" && (
                  <HStack spacing={2} justify="flex-end">
                    {[0, 1, 2].map((index) => (
                      <Box
                        key={index}
                        position="relative"
                        w="160px"
                        h="90px"
                        borderRadius="lg"
                        overflow="hidden"
                        border="2px solid"
                        borderColor={
                          customBgImages[index]
                            ? index === activeBgIndex
                              ? activeSlotBorder
                              : cardBorder
                            : emptySlotBorder
                        }
                        cursor={customBgImages[index] ? "pointer" : "default"}
                        onClick={() => {
                          if (customBgImages[index]) {
                            setActiveBgIndex(index);
                          }
                        }}
                        transition="all 0.2s"
                        _hover={
                          customBgImages[index]
                            ? { borderColor: activeSlotBorder, transform: "scale(1.02)" }
                            : {}
                        }
                      >
                        {customBgImages[index] ? (
                          <>
                            <img
                              src={customBgImages[index]}
                              alt={`Background ${index + 1}`}
                              style={{
                                width: "100%",
                                height: "100%",
                                objectFit: "cover",
                              }}
                            />
                            <Box
                              position="absolute"
                              top={1}
                              right={1}
                              onClick={(e) => {
                                e.stopPropagation();
                                removeCustomBgImage(index);
                              }}
                              bg="blackAlpha.600"
                              borderRadius="full"
                              p={0.5}
                              cursor="pointer"
                              _hover={{ bg: "blackAlpha.800" }}
                              transition="all 0.2s"
                            >
                              <LuX size={12} color="white" />
                            </Box>
                            {index === activeBgIndex && (
                              <Box
                                position="absolute"
                                bottom={1}
                                left="50%"
                                transform="translateX(-50%)"
                                bg="blue.500"
                                borderRadius="full"
                                px={1.5}
                                py={0.5}
                              >
                                <LuCheck size={10} color="white" />
                              </Box>
                            )}
                          </>
                        ) : (
                          <Flex
                            w="100%"
                            h="100%"
                            bg={emptySlotBg}
                            align="center"
                            justify="center"
                            cursor="pointer"
                            onClick={handleAddClick}
                            _hover={{ bg: useColorModeValue("gray.200", "#222222") }}
                            transition="all 0.2s"
                          >
                            <LuUpload size={18} color={subLabelColor} />
                          </Flex>
                        )}
                      </Box>
                    ))}
                    <input
                      id="bg-upload-new"
                      type="file"
                      accept="image/*"
                      style={{ display: "none" }}
                      onChange={handleImageUpload}
                    />
                  </HStack>
                )}

                {backgroundMode === "dynamic" && (
                  <HStack spacing={2} justify="flex-end">
                    <Box
                      position="relative"
                      w="160px"
                      h="90px"
                      borderRadius="lg"
                      overflow="hidden"
                      border="2px solid"
                      borderColor={dynamicBgVideo ? activeSlotBorder : emptySlotBorder}
                    >
                      {dynamicBgVideo ? (
                        <>
                          <video
                            src={videoPreviewSrc!}
                            style={{
                              width: "100%",
                              height: "100%",
                              objectFit: "cover",
                            }}
                            muted
                            loop
                            autoPlay
                          />
                          <Box
                            position="absolute"
                            top={1}
                            right={1}
                            onClick={() => setDynamicBgVideo(null)}
                            bg="blackAlpha.600"
                            borderRadius="full"
                            p={0.5}
                            cursor="pointer"
                            _hover={{ bg: "blackAlpha.800" }}
                            transition="all 0.2s"
                          >
                            <LuX size={12} color="white" />
                          </Box>
                        </>
                      ) : (
                        <Flex
                          w="100%"
                          h="100%"
                          bg={emptySlotBg}
                          align="center"
                          justify="center"
                          cursor="pointer"
                          onClick={handleVideoAddClick}
                          _hover={{ bg: useColorModeValue("gray.200", "#222222") }}
                          transition="all 0.2s"
                          flexDirection="column"
                          gap={1}
                        >
                          <LuUpload size={20} color={subLabelColor} />
                          <Text fontSize="xs" color={subLabelColor}>
                            {t("settings.appearanceSettings.uploadVideo")}
                          </Text>
                        </Flex>
                      )}
                    </Box>
                  </HStack>
                )}

                {backgroundMode === "mr" && (
                  <VStack spacing={3} align="stretch">
                    <LiquidGlassCard px={4} py={3}>
                      <HStack spacing={3}>
                        <Box
                          w="160px"
                          h="90px"
                          borderRadius="lg"
                          overflow="hidden"
                          flexShrink={0}
                          position="relative"
                        >
                          <img
                            src="/logo/MR.png"
                            alt="MR"
                            style={{
                              width: "100%",
                              height: "100%",
                              objectFit: "cover",
                              display: "block",
                            }}
                          />
                        </Box>
                        <Box flex={1}>
                          <Text fontSize="sm" color={labelColor} fontWeight="medium">
                            {t("settings.appearanceSettings.mrBackground")}
                          </Text>
                          <Text fontSize="xs" color={subLabelColor} mt={1}>
                            {t("settings.appearanceSettings.mrBackgroundDesc")}
                          </Text>
                        </Box>
                      </HStack>
                    </LiquidGlassCard>

                    <LiquidGlassCard px={4} py={3}>
                      <VStack spacing={3} align="stretch">
                        <HStack spacing={3}>
                          <Box
                            as="button"
                            flex={1}
                            px={3}
                            py={2}
                            borderRadius="lg"
                            border="2px solid"
                            borderColor={mrColorMode === "theme" ? getActiveColor() : cardBorder}
                            bg={mrColorMode === "theme" ? `${getActiveColor()}15` : "transparent"}
                            onClick={() => setMrColorMode("theme")}
                            textAlign="center"
                            cursor="pointer"
                            transition="all 0.2s"
                            _hover={{ borderColor: getActiveColor() }}
                          >
                            <Text fontSize="sm" fontWeight="medium" color={labelColor}>
                              {t("settings.appearanceSettings.mrColorTheme")}
                            </Text>
                            <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                              {t("settings.appearanceSettings.mrColorThemeDesc")}
                            </Text>
                          </Box>
                          <Box
                            as="button"
                            flex={1}
                            px={3}
                            py={2}
                            borderRadius="lg"
                            border="2px solid"
                            borderColor={mrColorMode === "custom" ? getActiveColor() : cardBorder}
                            bg={mrColorMode === "custom" ? `${getActiveColor()}15` : "transparent"}
                            onClick={() => setMrColorMode("custom")}
                            textAlign="center"
                            cursor="pointer"
                            transition="all 0.2s"
                            _hover={{ borderColor: getActiveColor() }}
                          >
                            <Text fontSize="sm" fontWeight="medium" color={labelColor}>
                              {t("settings.appearanceSettings.mrColorCustom")}
                            </Text>
                            <Text fontSize="xs" color={subLabelColor} mt={0.5}>
                              {t("settings.appearanceSettings.mrColorCustomDesc")}
                            </Text>
                          </Box>
                        </HStack>
                        {mrColorMode === "custom" && (
                          <HStack justify="flex-end">
                            <CustomColorPicker color={mrCustomColor} onChange={setMrCustomColor} />
                          </HStack>
                        )}
                      </VStack>
                    </LiquidGlassCard>
                  </VStack>
                )}
              </>
            )}

            <Divider borderColor={cardBorder} />
            <HStack justify="space-between">
              <Text fontSize="sm" color={labelColor} fontWeight="medium">
                {t("settings.appearanceSettings.backgroundBlurLabel")}
              </Text>
              <Text fontSize="sm" color={getActiveColor()} fontWeight="bold">{backgroundBlur}px</Text>
            </HStack>
            <Slider
              value={backgroundBlur}
              min={0}
              max={30}
              step={1}
              onChange={(val) => setBackgroundBlur(val)}
            >
              <SliderTrack bg={useColorModeValue("gray.200", "gray.700")}>
                <SliderFilledTrack bg={getActiveColor()} />
              </SliderTrack>
              <SliderThumb />
            </Slider>
            <Text fontSize="xs" color={subLabelColor}>
              {t("settings.appearanceSettings.backgroundBlurHint")}
            </Text>
          </VStack>
        </LiquidGlassCard>
      </Box>
    </Box>
  );
}


export default function SettingsPage() {
  const [activeItem, setActiveItem] = useState("general");
  const [searchParams] = useSearchParams();
  const { t } = useTranslation();
  const { config } = useThemeColor();
  const { jellyBounceEnabled } = useBackground();
  const isFirstRender = useRef(true);

  // 支持通过 URL ?section=xxx 定位子菜单（如托盘"检查更新"导航到 /settings?section=about）
  useEffect(() => {
    const section = searchParams.get("section");
    if (section && settingItems.some((item) => item.id === section)) {
      setActiveItem(section);
    }
  }, [searchParams]);

  // 子菜单切换时触发果冻弹跳动画（跳过首次挂载，避免与路由切换动画重复）
  useEffect(() => {
    if (isFirstRender.current) {
      isFirstRender.current = false;
      return;
    }
    if (!jellyBounceEnabled) return;
    document.body.classList.remove("jelly-bounce-active");
    void document.body.offsetWidth; // 强制 reflow
    document.body.classList.add("jelly-bounce-active");
    const timer = setTimeout(() => {
      document.body.classList.remove("jelly-bounce-active");
    }, 700);
    return () => {
      clearTimeout(timer);
      document.body.classList.remove("jelly-bounce-active");
    };
  }, [activeItem, jellyBounceEnabled]);

  return (
    <Flex gap={6} pt={8}>
      <Box w="180px" flexShrink={0} position="sticky" top={8} alignSelf="flex-start">
        <VStack spacing={0.5} align="stretch">
          {settingItems.map((item) => {
            const Icon = item.icon;
            const isActive = activeItem === item.id;

            return (
              <LiquidGlassMenuItem
                key={item.id}
                isActive={isActive}
                onClick={() => setActiveItem(item.id)}
                icon={item.icon}
              >
                {t(item.labelKey)}
              </LiquidGlassMenuItem>
            );
          })}
        </VStack>
      </Box>

      <Box flex={1}>
        {/* 直接渲染子菜单，不做 motion 动画：opacity/transform 动画会让 backdrop-filter 失效，
            导致液态玻璃卡片在切换时先透明、动画结束后瞬间出现模糊 */}
        <div key={activeItem} style={{ position: 'relative', zIndex: 1 }}>
          {activeItem === "general" && <GeneralSettings />}
          {activeItem === "appearance" && <AppearanceSettings />}
          {activeItem === "preview" && <FrameScopeSettings />}
          {activeItem === "about" && <AboutSettings />}
        </div>
      </Box>
    </Flex>
  );
}
