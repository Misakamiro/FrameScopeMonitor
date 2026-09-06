import type { ComponentType } from "react";
import {
  Gauge,
  GaugeCircle,
  Fan,
  Rocket,
  TrendingUp,
  TrendingDown,
  Cpu,
  SlidersHorizontal,
  Activity,
} from "lucide-react";

/**
 * 处理器电源高级设置 UI 元数据。
 *
 * 注意：本文件仅承载 UI 元数据（图标 / 颜色 / i18n key / 枚举选项），
 * 设置的 GUID、推荐值、默认值等后端数据位于 Rust 侧 power_settings.rs，
 * id 与后端一一对应，避免双重事实源。
 */
export interface PowerAdvancedSettingDef {
  id: string;
  /** 控件类型：percent（滑块+数字输入）| select（下拉选择） */
  type: "percent" | "select";
  icon: ComponentType<{ size?: number; strokeWidth?: number }>;
  color: string;
  titleKey: string;
  descKey: string;
  /** select 类型专用：可选项列表 */
  options?: { value: number; labelKey: string }[];
}

// 预定义颜色池（与后端无关，纯展示用）
const COLORS = [
  "#FF6B9D", "#3182CE", "#38A169", "#DD6B20", "#805AD5",
  "#E53E3E", "#00B5D8", "#D69E2E", "#319795", "#667EEA",
  "#ED64A6", "#F56565", "#48BB78", "#9F7AEA", "#F6AD55",
];

export const powerAdvancedSettings: PowerAdvancedSettingDef[] = [
  {
    id: "processor-min-state",
    type: "percent",
    icon: Gauge,
    color: COLORS[0],
    titleKey: "powerAdvanced.settings.processorMinState.title",
    descKey: "powerAdvanced.settings.processorMinState.desc",
  },
  {
    id: "processor-max-state",
    type: "percent",
    icon: GaugeCircle,
    color: COLORS[1],
    titleKey: "powerAdvanced.settings.processorMaxState.title",
    descKey: "powerAdvanced.settings.processorMaxState.desc",
  },
  {
    id: "system-cooling-policy",
    type: "select",
    icon: Fan,
    color: COLORS[2],
    titleKey: "powerAdvanced.settings.systemCoolingPolicy.title",
    descKey: "powerAdvanced.settings.systemCoolingPolicy.desc",
    options: [
      { value: 0, labelKey: "powerAdvanced.options.coolingPolicy.0" },
      { value: 1, labelKey: "powerAdvanced.options.coolingPolicy.1" },
    ],
  },
  {
    id: "processor-performance-boost-mode",
    type: "select",
    icon: Rocket,
    color: COLORS[3],
    titleKey: "powerAdvanced.settings.processorBoostMode.title",
    descKey: "powerAdvanced.settings.processorBoostMode.desc",
    options: [
      { value: 0, labelKey: "powerAdvanced.options.boostMode.0" },
      { value: 1, labelKey: "powerAdvanced.options.boostMode.1" },
      { value: 2, labelKey: "powerAdvanced.options.boostMode.2" },
      { value: 3, labelKey: "powerAdvanced.options.boostMode.3" },
      { value: 4, labelKey: "powerAdvanced.options.boostMode.4" },
      { value: 5, labelKey: "powerAdvanced.options.boostMode.5" },
      { value: 6, labelKey: "powerAdvanced.options.boostMode.6" },
    ],
  },
  {
    id: "processor-performance-increase-policy",
    type: "select",
    icon: TrendingUp,
    color: COLORS[4],
    titleKey: "powerAdvanced.settings.increasePolicy.title",
    descKey: "powerAdvanced.settings.increasePolicy.desc",
    options: [
      { value: 0, labelKey: "powerAdvanced.options.policy.0" },
      { value: 1, labelKey: "powerAdvanced.options.policy.1" },
      { value: 2, labelKey: "powerAdvanced.options.policy.2" },
      { value: 3, labelKey: "powerAdvanced.options.policy.3" },
    ],
  },
  {
    id: "processor-performance-decrease-policy",
    type: "select",
    icon: TrendingDown,
    color: COLORS[5],
    titleKey: "powerAdvanced.settings.decreasePolicy.title",
    descKey: "powerAdvanced.settings.decreasePolicy.desc",
    options: [
      { value: 0, labelKey: "powerAdvanced.options.policy.0" },
      { value: 1, labelKey: "powerAdvanced.options.policy.1" },
      { value: 2, labelKey: "powerAdvanced.options.policy.2" },
    ],
  },
  {
    id: "processor-core-parking-min-cores",
    type: "percent",
    icon: Cpu,
    color: COLORS[6],
    titleKey: "powerAdvanced.settings.coreParkingMin.title",
    descKey: "powerAdvanced.settings.coreParkingMin.desc",
  },
  {
    id: "processor-core-parking-max-cores",
    type: "percent",
    icon: Cpu,
    color: COLORS[7],
    titleKey: "powerAdvanced.settings.coreParkingMax.title",
    descKey: "powerAdvanced.settings.coreParkingMax.desc",
  },
  {
    id: "processor-energy-performance-preference",
    type: "percent",
    icon: SlidersHorizontal,
    color: COLORS[8],
    titleKey: "powerAdvanced.settings.energyPreference.title",
    descKey: "powerAdvanced.settings.energyPreference.desc",
  },
  {
    id: "processor-performance-increase-threshold",
    type: "percent",
    icon: Activity,
    color: COLORS[9],
    titleKey: "powerAdvanced.settings.increaseThreshold.title",
    descKey: "powerAdvanced.settings.increaseThreshold.desc",
  },
  {
    id: "processor-performance-decrease-threshold",
    type: "percent",
    icon: Activity,
    color: COLORS[10],
    titleKey: "powerAdvanced.settings.decreaseThreshold.title",
    descKey: "powerAdvanced.settings.decreaseThreshold.desc",
  },
];
