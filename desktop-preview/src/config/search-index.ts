import { Home, Cpu, Crosshair, FileChartColumn, PanelsTopLeft, Settings, Palette, Info } from "lucide-react";
import type { ComponentType } from "react";
export type SearchCategory = "page" | "setting" | "builtin-tool" | "optimization" | "thirdparty-tool";
export interface SearchItem {
  id: string; nameKey: string; path: string;
  icon: ComponentType<{ size?: number; strokeWidth?: number }>;
  category: SearchCategory; keywords?: string[]; customIcon?: string;
  action?: "navigate" | "run-tool"; toolId?: string;
  locationKeys?: string[];
  targetId?: string;
  fallbackNameKey?: string;
  hintKey?: string;
}
type SettingEntry = [id: string, nameKey: string, keywords?: string[], fallbackNameKey?: string];
const groupLabels: Record<string, string> = {
  "search-appearance-font": "settings.appearanceSettings.font",
  "search-appearance-glass": "settings.appearanceSettings.liquidGlass",
  "search-appearance-theme-color": "settings.appearanceSettings.themeColor",
  "search-appearance-background": "settings.appearanceSettings.customBackground",
  "search-overlay-enable": "overlayPanel.enableOverlay",
  "search-overlay-items": "overlayPanel.displayItems",
  "search-overlay-appearance": "overlayPanel.appearance",
};
function settings(section: string, sectionName: string, entries: SettingEntry[], targetId = `search-settings-${section}`): SearchItem[] {
  return entries.map(([id, nameKey, keywords, fallbackNameKey]) => ({
    id, nameKey, keywords, fallbackNameKey, icon: Settings, category: "setting",
    path: `/settings?section=${section}&focus=${id}`, targetId,
    locationKeys: ["设置", sectionName, ...(groupLabels[targetId] ? [groupLabels[targetId]] : [])],
    hintKey: ["appearance-mr-color", "appearance-mr-theme"].includes(id) ? "选择 MR 背景后可用"
      : targetId === "search-appearance-background" && !["appearance-background", "appearance-background-blur"].includes(id) ? "启用自定义背景后可用"
        : id === "appearance-glass-blur" ? "普通液态玻璃模式下可调"
          : ["appearance-glass-mode", "appearance-island-glass"].includes(id) ? "开启液态玻璃后可用" : undefined,
  }));
}
function overlay(entries: SettingEntry[]): SearchItem[] {
  return entries.map(([id, nameKey, keywords, fallbackNameKey]) => {
    const targetId = id.startsWith("overlay-metric-") || ["overlay-items", "overlay-custom", "overlay-password-maps"].includes(id)
      ? "search-overlay-items" : ["overlay-enable", "overlay-hotkey", "overlay-position", "overlay-startup"].includes(id)
        ? "search-overlay-enable" : "search-overlay-appearance";
    return {
    id, nameKey, fallbackNameKey, icon: PanelsTopLeft, category: "setting",
    keywords: ["OSD", "悬浮窗", "竖排", "灵动岛", ...(keywords ?? [])],
    path: `/overlay-panel?focus=${id}`,
    targetId, locationKeys: ["悬浮监控", groupLabels[targetId]],
    hintKey: ["overlay-width", "overlay-metric-cpu_core_clocks", "overlay-metric-cpu_core_voltages"].includes(id) ? "仅竖排样式可用" : undefined,
  }; });
}
function pageFeatures(path: string, name: string, icon: SearchItem["icon"], entries: SettingEntry[]): SearchItem[] {
  return entries.map(([id, nameKey, keywords, fallbackNameKey]) => ({
    id, nameKey, keywords, fallbackNameKey: fallbackNameKey ?? name, icon, category: "setting",
    path: `${path}?focus=${id}`, targetId: `search-${path === "/" ? "home" : path.slice(1)}`,
    locationKeys: [name],
    hintKey: path === "/reports" && id !== "reports-filter" ? "选择报告并打开详情后可用"
      : path === "/targets" && id !== "targets-add" ? "选择目标后可用" : undefined,
  }));
}
export const searchIndex: SearchItem[] = [
  { id: "home", nameKey: "首页", path: "/", icon: Home, category: "page", keywords: ["监控", "开始", "停止"] },
  { id: "hardware", nameKey: "硬件信息", path: "/hardware", icon: Cpu, category: "page", keywords: ["CPU", "GPU", "温度", "传感器"] },
  { id: "targets", nameKey: "监控目标", path: "/targets", icon: Crosshair, category: "page", keywords: ["游戏", "进程"] },
  { id: "reports", nameKey: "报告", path: "/reports", icon: FileChartColumn, category: "page", keywords: ["历史", "诊断", "FPS"] },
  { id: "overlay", nameKey: "悬浮监控", path: "/overlay-panel", icon: PanelsTopLeft, category: "page", keywords: ["OSD", "竖排", "悬浮窗"] },
  { id: "settings", nameKey: "设置", path: "/settings", icon: Settings, category: "page" },
  { id: "appearance", nameKey: "外观", path: "/settings?section=appearance", icon: Palette, category: "page", keywords: ["背景", "主题", "壁纸", "字体"] },
  { id: "about", nameKey: "关于", path: "/settings?section=about", icon: Info, category: "page", keywords: ["许可证", "来源", "更新", "update"] },
  { id: "general", nameKey: "settings.general", path: "/settings?section=general", icon: Settings, category: "page", keywords: ["通用设置", "常规设置"] },
  { id: "framescope", nameKey: "FrameScope 采样与报告", path: "/settings?section=preview", icon: FileChartColumn, category: "page", keywords: ["采集设置", "日志", "自启动"] },
  ...settings("general", "settings.general", [
    ["general-transition", "settings.generalSettings.pageTransitionLabel", ["页面动画", "过渡", "滑动", "淡化", "slide", "fade"]],
    ["general-close", "settings.generalSettings.closeBehaviorLabel", ["关闭行为", "关闭窗口", "每次询问", "直接关闭", "最小化到托盘", "tray"]],
    ["general-nav-position", "settings.generalSettings.navPositionLabel", ["侧边栏", "顶部导航", "导航位置"], "settings.navigation"],
    ["general-nav-label", "settings.generalSettings.sidebarShowLabel", ["导航文字", "无字", "有字"], "settings.navigation"],
    ["general-nav-order", "settings.generalSettings.navButtonVisibility", ["导航排序", "拖拽", "隐藏菜单", "按钮显示"], "settings.navigation"],
  ]),
  ...settings("appearance", "settings.appearance", [
    ["appearance-theme", "settings.appearanceSettings.themeStyle", ["浅色", "深色", "跟随系统", "light", "dark", "theme"]],
  ]),
  ...settings("appearance", "settings.appearance", [
    ["appearance-font", "settings.appearanceSettings.fontLabel", ["字体", "MiSans", "font"]],
    ["appearance-import-font", "settings.appearanceSettings.importCustomFontLabel", ["TTF", "OTF", "WOFF", "导入字体", "删除字体"], "settings.appearanceSettings.fontLabel"],
  ], "search-appearance-font"),
  ...settings("appearance", "settings.appearance", [
    ["appearance-glass", "settings.appearanceSettings.liquidGlassLabel", ["玻璃效果", "透明", "glass"]],
    ["appearance-glass-mode", "settings.appearanceSettings.liquidGlassModeLabel", ["透明液态玻璃", "普通液态玻璃", "折射"], "settings.appearanceSettings.liquidGlassLabel"],
    ["appearance-glass-blur", "settings.appearanceSettings.liquidGlassBlurLabel", ["玻璃模糊", "blur"], "settings.appearanceSettings.liquidGlassLabel"],
    ["appearance-island-glass", "settings.appearanceSettings.islandLiquidGlassLabel", ["灵动岛效果"], "settings.appearanceSettings.liquidGlassLabel"],
  ], "search-appearance-glass"),
  ...settings("appearance", "settings.appearance", [
    ["appearance-jelly", "settings.appearanceSettings.jellyBounceLabel", ["果冻动画", "弹跳"]],
  ]),
  ...settings("appearance", "settings.appearance", [
    ["appearance-theme-color", "settings.appearanceSettings.themeColor", ["强调色", "配色", "color"]],
    ["appearance-preset-color", "settings.appearanceSettings.presets", ["预设主题色"], "settings.appearanceSettings.themeColor"],
    ["appearance-custom-color", "settings.appearanceSettings.customColor", ["自定义主题色", "颜色选择器"], "settings.appearanceSettings.themeColor"],
    ["appearance-reset-color", "settings.appearanceSettings.resetToDefault", ["恢复默认主题色"], "settings.appearanceSettings.themeColor"],
  ], "search-appearance-theme-color"),
  ...settings("appearance", "settings.appearance", [
    ["appearance-background", "settings.appearanceSettings.customBackgroundLabel", ["壁纸", "自定义背景", "background"]],
    ["appearance-preset-bg", "settings.appearanceSettings.presetBackground", ["预设壁纸"], "settings.appearanceSettings.customBackgroundLabel"],
    ["appearance-image-bg", "settings.appearanceSettings.imageBackground", ["图片壁纸", "上传图片", "添加图片", "删除背景"], "settings.appearanceSettings.customBackgroundLabel"],
    ["appearance-video-bg", "settings.appearanceSettings.dynamicBackground", ["视频壁纸", "动态壁纸", "上传视频", "MP4", "webm"], "settings.appearanceSettings.customBackgroundLabel"],
    ["appearance-mr-bg", "settings.appearanceSettings.mrBackground", ["MR背景", "MR配色", "背景主题色", "背景自定义颜色"], "settings.appearanceSettings.customBackgroundLabel"],
    ["appearance-mr-theme", "settings.appearanceSettings.mrColorTheme", ["MR主题色", "MR配色"], "settings.appearanceSettings.customBackgroundLabel"],
    ["appearance-mr-color", "settings.appearanceSettings.mrColorCustom", ["MR自定义颜色", "MR配色"], "settings.appearanceSettings.customBackgroundLabel"],
    ["appearance-carousel", "settings.appearanceSettings.carouselLabel", ["壁纸轮播", "图片轮播"], "settings.appearanceSettings.customBackgroundLabel"],
    ["appearance-background-blur", "settings.appearanceSettings.backgroundBlurLabel", ["壁纸模糊", "背景清晰度", "blur"]],
  ], "search-appearance-background"),
  ...settings("preview", "FrameScope", [
    ["fs-sample-interval", "全局采样间隔（毫秒）", ["采样频率", "遥测间隔", "sample interval"]],
    ["fs-poll-interval", "目标扫描间隔（毫秒）", ["轮询间隔", "游戏检测", "poll interval"]],
    ["fs-windows-startup", "随 Windows 启动", ["自启动", "开机自启", "登录启动", "autostart", "startup"], "采样与报告"],
    ["fs-monitor-startup", "启动软件时自动开启监控", ["自动监控", "监控自启动"]],
    ["fs-auto-report", "完成后自动打开报告", ["自动报告", "报告自启", "auto open report"]],
    ["fs-detailed-log", "详细日志", ["日志记录", "log"]],
    ["fs-diagnostic-log", "性能诊断日志", ["调试日志", "diagnostic"]],
    ["fs-tray-start", "手动启动时也直接进入托盘", ["最小化启动", "启动隐藏", "后台启动", "tray"]],
  ]),
  ...settings("about", "关于／更新", [
    ["update-startup-check", "启动时检查更新", ["自动检查更新", "开机检查", "update"]],
    ["update-beta", "包含测试版", ["beta", "更新渠道", "预发布"]],
    ["update-accelerated", "使用 GitHub 加速下载", ["更新加速", "代理", "proxy", "gh-proxy", "下载线路"]],
    ["update-check", "检查更新", ["下载更新", "安装更新", "升级", "安装并重启", "update"]],
    ["update-stable", "查找可返回的正式版", ["稳定版", "stable", "返回正式版"], "检查更新"],
    ["update-restore", "恢复上一版", ["版本回滚", "rollback", "恢复版本"], "检查更新"],
    ["about-license", "许可证与来源", ["GPL", "版权", "LICENSE", "NexBox", "开源"], "关于／更新"],
  ]),
  ...overlay([
    ["overlay-enable", "overlayPanel.enableOverlay", ["开启OSD", "关闭悬浮窗"]],
    ["overlay-hotkey", "悬浮窗快捷键", ["快捷键", "热键", "hotkey"], "overlayPanel.enableOverlay"],
    ["overlay-position", "overlayPanel.resetPosition", ["重置位置", "移动悬浮窗", "拖动位置"], "overlayPanel.enableOverlay"],
    ["overlay-startup", "启动软件时自动开启悬浮窗", ["悬浮窗自启动", "开机OSD"]],
    ["overlay-items", "overlayPanel.displayItems", ["显示指标", "硬件监控", "拖拽排序"]],
    ["overlay-custom", "overlayPanel.addCustomItem", ["自定义文字", "添加文字"]],
    ["overlay-style", "overlayPanel.styles", ["OSD样式"]],
    ["overlay-default", "overlayPanel.styles.default", ["默认样式"], "overlayPanel.styles"],
    ["overlay-island", "overlayPanel.styles.dynamicIsland", ["灵动岛样式"], "overlayPanel.styles"],
    ["overlay-vertical", "overlayPanel.styles.verticalPanel", ["竖排样式"], "overlayPanel.styles"],
    ["overlay-font", "字体", ["OSD字体", "Microsoft YaHei"], "overlayPanel.appearance"],
    ["overlay-font-color", "overlayPanel.fontColor", ["文字颜色"]],
    ["overlay-opacity", "overlayPanel.opacity", ["背景不透明度", "背景透明度", "opacity"]],
    ["overlay-size", "大小", ["字号", "字体大小", "font size"]],
    ["overlay-width", "单行宽度", ["行宽", "竖排宽度"], "overlayPanel.appearance"],
    ["overlay-password-maps", "三角洲密码地图设置", ["密码地图", "航天基地", "长弓溪谷"], "overlayPanel.displayItems"],
    ...[
      ["time", "时间"], ["fps", "FPS"], ["fps_1low", "1% Low"], ["fps_01low", "0.1% Low"],
      ["cpu_usage", "CPU占用"], ["cpu_temp", "CPU温度"], ["cpu_clock", "CPU频率"], ["cpu_voltage", "CPU电压"],
      ["cpu_power", "CPU功耗"], ["cpu_core_clocks", "CPU分核频率"], ["cpu_core_voltages", "CPU分核电压"],
      ["cpu_fan_speed", "CPU风扇转速"], ["gpu_usage", "GPU占用"], ["gpu_temp", "GPU温度"],
      ["gpu_clock", "GPU频率"], ["gpu_power", "GPU功耗"], ["gpu_vram", "GPU显存占用"],
      ["gpu_memory_clock", "GPU显存频率"], ["gpu_voltage", "GPU电压"], ["gpu_fan_speed", "GPU风扇转速"],
      ["memory_usage", "内存占用"], ["ssd_temp", "硬盘温度"], ["game_ping", "游戏延迟"], ["delta_password", "三角洲密码"],
    ].map(([id, label]): SettingEntry => [`overlay-metric-${id}`, label, ["显示项", "指标", id], "overlayPanel.displayItems"]),
  ]),
  ...[
    ["hardware-sensors", "全部传感器状态", ["传感器窗口", "sensor"]],
    ["hardware-driver", "安装驱动（获取CPU温度）", ["PawnIO", "驱动安装", "CPU温度缺失"]],
    ["hardware-gpu-select", "显卡选择", ["独显", "核显", "GPU", "选择显卡"]],
    ["hardware-copy", "hardware.copyAll", ["复制硬件", "硬件报告"]],
    ["hardware-clear", "清除数据", ["清理缓存", "重置硬件"]],
    ["hardware-board", "hardware.motherboard", ["BIOS", "主板型号", "固件"]],
  ].map(([id, nameKey, keywords]): SearchItem => ({ id: id as string, nameKey: nameKey as string,
    keywords: keywords as string[], icon: Cpu, category: "setting", path: `/hardware?focus=${id}`,
    targetId: "search-hardware", fallbackNameKey: id === "hardware-gpu-select" ? "hardware.gpu" : "hardware.title", locationKeys: ["硬件信息"] })),
  ...pageFeatures("/", "首页", Home, [
    ["home-monitor-start", "启动监控", ["开始监控", "自动检测游戏"], "FrameScope"],
    ["home-monitor-stop", "停止监控", ["结束采集", "停止记录"], "停止"],
    ["home-targets", "管理目标", ["目标管理"], "FrameScope"],
    ["home-hardware", "硬件配置", ["硬件概览", "配置摘要"], "FrameScope"],
  ]),
  ...pageFeatures("/targets", "监控目标", Crosshair, [
    ["targets-add", "添加目标", ["添加游戏", "新建目标", "自定义目标", "目标名称", "进程名", "选择进程"]],
    ["targets-edit", "编辑目标", ["修改游戏", "修改进程", "编辑游戏"], "添加目标"],
    ["targets-enable", "启用目标", ["启用游戏", "禁用游戏", "启用目标监控"]],
    ["targets-delete", "删除目标", ["移除游戏", "删除游戏"], "添加目标"],
  ]),
  ...pageFeatures("/reports", "报告", FileChartColumn, [
    ["reports-filter", "报告类型", ["筛选报告", "完整报告", "部分报告", "诊断报告", "错误报告"]],
    ["reports-detail", "报告详情", ["查看详情", "平均FPS", "帧时间", "1% Low"]],
    ["reports-open", "打开图表报告", ["交互报告", "报告图表"]],
    ["reports-directory", "报告目录", ["打开报告文件夹", "原始数据", "CSV"]],
    ["reports-regenerate", "重新生成报告", ["报告重建", "重新生成", "刷新图表"]],
    ["reports-diagnose", "生成诊断", ["诊断日志", "报告诊断"]],
    ["reports-delete", "删除记录", ["删除报告", "回收站", "移除报告"]],
  ]),
];
export const categoryLabels: Record<SearchCategory, string> = { page: "页面", setting: "功能与设置", "builtin-tool": "", optimization: "", "thirdparty-tool": "" };
export const categoryOrder: SearchCategory[] = ["page", "setting"];
export function getThirdPartyToolIcon(_id: string) { return undefined; }

function normalize(value: string) { return value.normalize("NFKC").trim().toLowerCase(); }
export function findSearchResults(query: string, items: SearchItem[], t: (key: string) => string): SearchItem[] {
  const normalized = normalize(query);
  if (!normalized) return [];
  const tokens = normalized.split(/\s+/);
  const compactQuery = normalized.replace(/\s+/g, "");
  const score = (item: SearchItem) => {
    const name = normalize(t(item.nameKey)).replace(/\s+/g, "");
    return name === compactQuery ? 0 : name.startsWith(compactQuery) ? 1 : name.includes(compactQuery) ? 2 : 3;
  };
  return items.filter(item => {
    const text = normalize([t(item.nameKey), item.nameKey, ...(item.keywords ?? []), ...(item.locationKeys ?? []).map(key => t(key))].join(" "));
    const compactText = text.replace(/\s+/g, "");
    return tokens.every(token => text.includes(token) || compactText.includes(token));
  }).sort((a, b) => score(a) - score(b));
}
