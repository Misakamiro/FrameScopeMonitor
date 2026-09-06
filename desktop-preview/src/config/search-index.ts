import { Home, Cpu, Crosshair, FileChartColumn, PanelsTopLeft, Settings, Palette, Info } from "lucide-react";
import type { ComponentType } from "react";
export type SearchCategory = "page" | "builtin-tool" | "optimization" | "thirdparty-tool";
export interface SearchItem {
  id: string; nameKey: string; path: string;
  icon: ComponentType<{ size?: number; strokeWidth?: number }>;
  category: SearchCategory; keywords?: string[]; customIcon?: string;
  action?: "navigate" | "run-tool"; toolId?: string;
}
export const searchIndex: SearchItem[] = [
  { id: "home", nameKey: "首页", path: "/", icon: Home, category: "page", keywords: ["监控", "开始", "停止"] },
  { id: "hardware", nameKey: "硬件信息", path: "/hardware", icon: Cpu, category: "page", keywords: ["CPU", "GPU", "温度", "传感器"] },
  { id: "targets", nameKey: "监控目标", path: "/targets", icon: Crosshair, category: "page", keywords: ["游戏", "进程"] },
  { id: "reports", nameKey: "报告", path: "/reports", icon: FileChartColumn, category: "page", keywords: ["历史", "诊断", "FPS"] },
  { id: "overlay", nameKey: "悬浮监控", path: "/overlay-panel", icon: PanelsTopLeft, category: "page", keywords: ["OSD", "竖排", "悬浮窗"] },
  { id: "settings", nameKey: "设置", path: "/settings", icon: Settings, category: "page" },
  { id: "appearance", nameKey: "外观", path: "/settings?section=appearance", icon: Palette, category: "page", keywords: ["背景", "主题", "壁纸", "字体"] },
  { id: "about", nameKey: "关于", path: "/settings?section=about", icon: Info, category: "page", keywords: ["许可证", "来源"] },
];
export const categoryLabels: Record<SearchCategory, string> = { page: "页面", "builtin-tool": "", optimization: "", "thirdparty-tool": "" };
export const categoryOrder: SearchCategory[] = ["page"];
export function getThirdPartyToolIcon(_id: string) { return undefined; }
