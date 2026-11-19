"use client";

import React, { useCallback, useEffect, useId, useMemo, useRef, useState, useSyncExternalStore } from "react";
import { motion, useAnimationControls, type TargetAndTransition } from "framer-motion";
import {getMotionPreferences, subscribeMotion, useMotionPreferences} from '@/lib/motion-preferences';
import {jellyScales} from '@/lib/ui-effects';
import {uiActivity} from '@/lib/ui-activity';
import { useColorModeValue } from "@chakra-ui/react";
import { Tooltip } from "@/components/ui/motion-components";
import { useThemeColor } from "@/contexts/theme-color-context";
import { useBackground } from "@/contexts/background-context";
import { useLiquidGlassRefraction, ISLAND_FILTER_ID } from "@/components/special/liquid-glass-svg-filter";
import {
  Zap,
  MemoryStick,
  Globe,
  Cpu,
  Layers,
  HardDrive,
  AudioWaveform,
  Gamepad2,
  Rocket,
  Sparkles,
  ShieldCheck,
  Crosshair,
  Mouse,
  SlidersHorizontal,
  Wrench,
  Monitor,
  Music,
  Download,
  Bluetooth,
  Image,
  Search,
  FileText,
  LayoutGrid,
  PanelsTopLeft,
  Gauge,
  Settings,
} from "lucide-react";

export type IslandStatus = "success" | "error" | "info" | "warning" | "loading" | "blue";

/** 预设语义图标键，按页面/操作类型区分（如电源计划→闪电） */
export type IconKey =
  | "power"
  | "memory"
  | "network"
  | "cpu"
  | "layers"
  | "disk"
  | "audio"
  | "gamepad"
  | "rocket"
  | "sparkles"
  | "shield"
  | "target"
  | "mouse"
  | "gpu"
  | "filter"
  | "wrench"
  | "monitor"
  | "music"
  | "download"
  | "bluetooth"
  | "image"
  | "search"
  | "file"
  | "layout"
  | "panels"
  | "gauge"
  | "settings";

export interface IslandOptions {
  id?: string;
  title?: string;
  description?: string;
  status?: IslandStatus;
  duration?: number | null;
  isClosable?: boolean;
  /** 兼容 Chakra toast 传入的 variant（如 "left-accent"），灵动岛样式固定，忽略该值 */
  variant?: string;
  icon?: React.ReactNode;
  /** 预设语义图标键，优先于 status 默认图标 */
  iconKey?: IconKey;
  onCloseComplete?: () => void;
  /** 持久基线（如更新下载岛）：无自动关闭，普通提示消失后自动恢复显示 */
  persistent?: boolean;
  /** 有值时展开态渲染进度条（0-100） */
  progress?: number;
  /** 覆盖默认点击行为（如完成态点击重启安装） */
  onClick?: () => void;
  /** 音乐播放灵动岛：渲染专辑封面 + 音量波动，悬停展开更高的播放控制布局 */
}

interface IslandItem {
  id: string;
  title?: string;
  description?: string;
  status: IslandStatus;
  duration: number | null;
  isClosable?: boolean;
  icon?: React.ReactNode;
  iconKey?: IconKey;
  onCloseComplete?: () => void;
  persistent?: boolean;
  progress?: number;
  onClick?: () => void;
  timer?: ReturnType<typeof setTimeout>;
}

/* ------------------------------------------------------------------ */
/* 提示文案精简：灵动岛只显示核心动作短语，去掉冗长解释，保持简洁。    */
/* 例如「未找到运行中的 ACE 进程」→「未找到」，「已限制 N 个进程」→「已限制」 */
/* ------------------------------------------------------------------ */
const SHORT_VERBS = [
  "未找到",
  "未检测到",
  "未发现",
  "已限制",
  "已优化",
  "已恢复",
  "已启用",
  "已禁用",
  "已清除",
  "已清理",
  "已设置",
  "已应用",
  "已暂停",
  "已更新",
  "已重置",
  "已关闭",
  "已开启",
  "已锁定",
  "已解锁",
  "已添加",
  "已删除",
  "已保存",
  "已安装",
  "已复制",
  "已上传",
  "已下载",
  "已提升",
  "已分配",
  "导入失败",
  "应用失败",
];

function shortenMessage(text?: string): string | undefined {
  if (!text) return text;
  const t0 = text.trim();
  if (!t0) return t0;
  let t = t0;

  // 0) 盘符前缀：如 "C: 优化(TRIM)完成" → 保留盘符，精简剩余部分 → "C 已优化"
  //    （避免盘符冒号被当成句号截断，导致只显示 "C"）
  const driveMatch = t.match(/^([A-Za-z]):\s*(.*)$/);
  if (driveMatch && driveMatch[2].trim()) {
    const rest = driveMatch[2].trim();
    const shortened = shortenVerb(rest);
    if (shortened !== rest) {
      return `${driveMatch[1]} ${shortened}`;
    }
    return t;
  }

  return shortenVerb(t);
}

function shortenVerb(t: string): string {
  // 1) 已知动作动词开头 → 只保留动词本身（未找到 / 已限制 / 已优化…）
  for (const v of SHORT_VERBS) {
    if (t.startsWith(v)) return v;
  }

  // 2) 「需要管理员权限」类长句 → 统一为短提示
  if (t.includes("需要管理员权限")) return "需要管理员权限";

  // 3) 特定长句映射为短词
  if (t.includes("应用注册表强制限制")) return "已限制";
  if (t.includes("IFEO 强制优先级")) return t.includes("清除") ? "已清除" : "已应用";
  if (t.includes("分配指定核心")) return "已分配";

  // 4) 完成/成功态 → 提取动作词，如 "优化(TRIM)完成" → "已优化"、"Optimization complete" → "Optimized"
  if (/完成|成功/.test(t)) {
    if (/优化|整理/.test(t)) return "已优化";
    if (/恢复/.test(t)) return "已恢复";
    if (/清理/.test(t)) return "已清理";
    if (/清除/.test(t)) return "已清除";
    if (/更新/.test(t)) return "已更新";
    if (/安装/.test(t)) return "已安装";
    if (/下载/.test(t)) return "已下载";
    if (/上传/.test(t)) return "已上传";
    if (/应用/.test(t)) return "已应用";
    if (/保存/.test(t)) return "已保存";
    if (/复制/.test(t)) return "已复制";
    if (/重置/.test(t)) return "已重置";
  } else if (/complete|done|finished|successful/i.test(t)) {
    const lower = t.toLowerCase();
    if (/optimiz|trim|defrag/.test(lower)) return "Optimized";
    if (/restor/.test(lower)) return "Restored";
    if (/clean/.test(lower)) return "Cleaned";
    if (/clear/.test(lower)) return "Cleared";
    if (/updat/.test(lower)) return "Updated";
    if (/install/.test(lower)) return "Installed";
    if (/download/.test(lower)) return "Downloaded";
    if (/upload/.test(lower)) return "Uploaded";
    if (/appl/.test(lower)) return "Applied";
    if (/sav/.test(lower)) return "Saved";
    if (/cop/.test(lower)) return "Copied";
    if (/reset/.test(lower)) return "Reset";
  }

  // 5) 截断到第一句主干：去掉「。」/「，」后的解释、冒号后的细节、尾部括号说明
  for (const sep of ["。", "，", "：", ":", "（", "("]) {
    const idx = t.indexOf(sep);
    if (idx > 0) {
      t = t.slice(0, idx).trim();
      break;
    }
  }

  return t;
}

/* ------------------------------------------------------------------ */
/* 模块级全局 store：只保留单个灵动岛，新提示替换旧提示                */
/* ------------------------------------------------------------------ */
let current: IslandItem | null = null;
// 持久基线（如更新下载岛）：普通提示显示时被覆盖，提示关闭后自动恢复
let pending: IslandItem | null = null;
let revision = 0;
const listeners = new Set<() => void>();
let idSeed = 0;
// getSnapshot 必须返回缓存引用（仅 store 变化时替换），否则 useSyncExternalStore 会无限重渲染
let cachedSnapshot: { item: IslandItem | null; revision: number } = { item: null, revision: 0 };

function emit() {
  cachedSnapshot = { item: current, revision };
  for (const l of listeners) l();
}

function buildItem(options: IslandOptions): IslandItem {
  const id = options.id ?? `island-${++idSeed}`;
  return {
    id,
    // 保留原始完整文案：折叠时显示精简版，悬停展开时显示详情
    title: options.title,
    description: options.description,
    status: options.status ?? "info",
    // 显式 null = 不自动关闭（如持久更新岛）；undefined 才用默认 2000ms
    duration: options.duration === null ? null : options.duration ?? 2000,
    isClosable: options.isClosable,
    icon: options.icon,
    iconKey: options.iconKey,
    onCloseComplete: options.onCloseComplete,
    persistent: options.persistent,
    progress: options.progress,
    onClick: options.onClick,
  };
}

function show(options: IslandOptions) {
  const next = buildItem(options);
  if (current) revision++; // 已有提示，触发「缩小→换内容→扩散」
  if (current?.timer) clearTimeout(current.timer);
  current = next;
  if (typeof next.duration === "number" && next.duration > 0) {
    next.timer = setTimeout(() => close(next.id), next.duration);
  }
  emit();
}

function update(id: string, options: IslandOptions) {
  show({ ...options, id });
}

/** 显示持久基线（无自动关闭）。若当前正显示普通提示则不覆盖，等待其关闭后由 close() 恢复 */
function showPersistent(options: IslandOptions) {
  const next = buildItem({ ...options, duration: null });
  pending = next;
  if (!current) {
    revision++;
    current = next;
    emit();
  } else if (current.id === next.id) {
    // 当前已是同 id 基线：直接换内容（触发 replace 动画）
    revision++;
    if (current.timer) clearTimeout(current.timer);
    current = next;
    emit();
  }
}

/**
 * 更新持久基线。animate=true 触发 replace 动画（状态切换：下载中→完成）；
 * animate=false 静默更新（进度实时刷新，避免每 200ms 缩→扩动画）。
 * 当前正显示普通提示时仅暂存 pending，待其关闭后恢复。
 */
function updatePersistent(id: string, options: IslandOptions, animate = true) {
  if (!pending || pending.id !== id) return;
  const next = { ...pending, ...options, id };
  pending = next;
  if (current?.id === id) {
    if (animate) revision++;
    if (current.timer) clearTimeout(current.timer);
    current = next;
    emit();
  }
}

/** 关闭持久基线：清除 pending；若正显示基线则淡出，否则不影响当前普通提示 */
function closePersistent(id: string) {
  if (!pending || pending.id !== id) return;
  pending = null;
  if (current?.id === id) {
    current = null;
    revision++;
    emit();
  }
}

function close(id: string) {
  if (!current || current.id !== id) return;
  if (current.timer) clearTimeout(current.timer);
  const cb = current.onCloseComplete;
  // 若存在持久基线且被关闭的不是它 → 恢复基线显示（replace 动画）
  if (pending && pending.id !== id) {
    revision++;
    current = pending;
  } else {
    current = null;
  }
  emit();
  cb?.();
}

function closeAll() {
  if (current?.timer) clearTimeout(current.timer);
  // 清空普通提示后恢复持久基线；当前已是基线或无变化时不重复触发动画
  if (pending && current?.id !== pending.id) {
    revision++;
    current = pending;
    emit();
  } else if (!pending && current) {
    current = null;
    revision++;
    emit();
  }
}

function isActive(id: string) {
  return current?.id === id;
}

/** 悬停展开时暂停自动关闭 */
function hold(id: string) {
  if (!current || current.id !== id) return;
  if (current.timer) clearTimeout(current.timer);
  current.timer = undefined;
}

/** 移开后恢复自动关闭计时 */
function extend(id: string) {
  if (!current || current.id !== id) return;
  if (current.timer) clearTimeout(current.timer);
  if (typeof current.duration === "number" && current.duration > 0) {
    current.timer = setTimeout(() => close(current.id), current.duration);
  }
}

function subscribe(cb: () => void) {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

function getSnapshot() {
  return cachedSnapshot;
}

export type DynamicIslandToast = ((options: IslandOptions) => void) & {
  update: typeof update;
  isActive: typeof isActive;
  close: typeof close;
  closeAll: typeof closeAll;
  showPersistent: typeof showPersistent;
  updatePersistent: typeof updatePersistent;
  closePersistent: typeof closePersistent;
};

/** 返回与 Chakra useToast 兼容的调用签名，旧 toast({...}) 调用体无需改动。
 *  传入 defaultIcon 后，该页面的成功/信息提示会自动带上对应语义图标。 */
export function useDynamicIsland(defaultIcon?: IconKey): DynamicIslandToast {
  return useMemo(() => makeToast(defaultIcon), [defaultIcon]);
}

function makeToast(defaultIcon?: IconKey): DynamicIslandToast {
  const resolveIcon = (options: IslandOptions): IslandOptions => {
    if (options.icon || options.iconKey) return options;
    if (defaultIcon && (options.status === "success" || options.status === "info")) {
      return { ...options, iconKey: defaultIcon };
    }
    return options;
  };
  const fn = ((options: IslandOptions) => show(resolveIcon(options))) as DynamicIslandToast;
  fn.update = (id, options) => update(id, resolveIcon(options));
  fn.isActive = isActive;
  fn.close = close;
  fn.closeAll = closeAll;
  fn.showPersistent = (options) => showPersistent(resolveIcon(options));
  fn.updatePersistent = (id, options, animate) => updatePersistent(id, resolveIcon(options), animate);
  fn.closePersistent = closePersistent;
  return fn;
}

/* ------------------------------------------------------------------ */
/* 状态图标：自绘 SVG（渐变 + 发光），info/loading 复用品牌主题色        */
/* ------------------------------------------------------------------ */
function StatusIcon({
  status,
  iconKey,
  primaryColor,
  size = 22,
}: {
  status: IslandStatus;
  iconKey?: IconKey;
  primaryColor: string;
  size?: number;
}) {
  const gid = useId().replace(/:/g, "");
  const palette: Record<IslandStatus, { from: string; to: string }> = {
    success: { from: "#34d399", to: "#10b981" },
    error: { from: "#f87171", to: "#ef4444" },
    warning: { from: "#fbbf24", to: "#f59e0b" },
    info: { from: primaryColor, to: primaryColor },
    loading: { from: primaryColor, to: primaryColor },
    blue: { from: "#3b82f6", to: "#22d3ee" },
  };
  const p = palette[status];
  const glowColor = status === "info" || status === "loading" ? `${primaryColor}55` : `${p.to}55`;

  return (
    <div
      style={{
        position: "relative",
        width: size,
        height: size,
        borderRadius: 999,
        flexShrink: 0,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        boxShadow: `0 0 14px 0 ${glowColor}, inset 0 1px 0 rgba(255,255,255,0.35)`,
      }}
    >
      <svg width={size} height={size} viewBox="0 0 36 36">
        <defs>
          <linearGradient id={`ig-${gid}`} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0%" stopColor={p.from} stopOpacity="0.8" />
            <stop offset="100%" stopColor={p.to} stopOpacity="1" />
          </linearGradient>
        </defs>
        <circle cx="18" cy="18" r="18" fill={`url(#ig-${gid})`} />
        {!iconKey && <StatusSymbol status={status} />}
      </svg>
      {iconKey && (
        <div
          style={{
            position: "absolute",
            inset: 0,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            pointerEvents: "none",
          }}
        >
          <SemanticIcon iconKey={iconKey} />
        </div>
      )}
      {status === "loading" && (
        <motion.svg
          width={size * 0.55}
          height={size * 0.55}
          viewBox="0 0 24 24"
          style={{ position: "absolute", inset: 0, margin: "auto" }}
          animate={{ rotate: 360 }}
          transition={{ repeat: Infinity, duration: 0.9, ease: "linear" }}
        >
          <circle cx="12" cy="12" r="9" fill="none" stroke="rgba(255,255,255,0.35)" strokeWidth="2.6" />
          <path
            d="M12 3a9 9 0 0 1 8.4 5.2"
            fill="none"
            stroke="#fff"
            strokeWidth="2.6"
            strokeLinecap="round"
          />
        </motion.svg>
      )}
    </div>
  );
}

function StatusSymbol({ status }: { status: IslandStatus }) {
  if (status === "success")
    return <path d="M12.2 18.2 16.3 22.4 23.6 14.6" fill="none" stroke="#fff" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" />;
  if (status === "error")
    return <g><path d="M18 12.2v7.6" stroke="#fff" strokeWidth="2.8" strokeLinecap="round" /><circle cx="18" cy="22.6" r="1.4" fill="#fff" /></g>;
  if (status === "warning")
    return <g><path d="M18 10.4 25.6 22.6H10.4Z" fill="none" stroke="#fff" strokeWidth="2.2" strokeLinejoin="round" /><path d="M18 15.2v4.2" stroke="#fff" strokeWidth="2.4" strokeLinecap="round" /><circle cx="18" cy="21.8" r="1.3" fill="#fff" /></g>;
  if (status === "info")
    return <g><circle cx="18" cy="13.2" r="1.4" fill="#fff" /><path d="M17.1 17.4h1.8v5.4h-1.8z" fill="#fff" /></g>;
  if (status === "blue")
    return <g><circle cx="18" cy="13.2" r="1.4" fill="#fff" /><path d="M17.1 17.4h1.8v5.4h-1.8z" fill="#fff" /></g>;
  return null;
}

function SemanticIcon({ iconKey }: { iconKey: IconKey }) {
  const p = { size: 15, strokeWidth: 2.4, color: "#fff" as const, strokeLinecap: "round" as const, strokeLinejoin: "round" as const };
  switch (iconKey) {
    case "power":     return <Zap {...p} />;
    case "memory":    return <MemoryStick {...p} />;
    case "network":   return <Globe {...p} />;
    case "cpu":       return <Cpu {...p} />;
    case "layers":    return <Layers {...p} />;
    case "disk":      return <HardDrive {...p} />;
    case "audio":     return <AudioWaveform {...p} />;
    case "gamepad":   return <Gamepad2 {...p} />;
    case "rocket":    return <Rocket {...p} />;
    case "sparkles":  return <Sparkles {...p} />;
    case "shield":    return <ShieldCheck {...p} />;
    case "target":    return <Crosshair {...p} />;
    case "mouse":     return <Mouse {...p} />;
    case "gpu":       return <Cpu {...p} />;
    case "filter":    return <SlidersHorizontal {...p} />;
    case "wrench":    return <Wrench {...p} />;
    case "monitor":   return <Monitor {...p} />;
    case "music":     return <Music {...p} />;
    case "download":  return <Download {...p} />;
    case "bluetooth": return <Bluetooth {...p} />;
    case "image":     return <Image {...p} />;
    case "search":    return <Search {...p} />;
    case "file":      return <FileText {...p} />;
    case "layout":    return <LayoutGrid {...p} />;
    case "panels":    return <PanelsTopLeft {...p} />;
    case "gauge":     return <Gauge {...p} />;
    case "settings":  return <Settings {...p} />;
    default: return null;
  }
}

/* ------------------------------------------------------------------ */
/* 音乐灵动岛音量波动：【不触碰正在播放的 <audio>】                    */
/* createMediaElementSource 会接管音频元素输出，且 WebView 的          */
/* AudioContext 未激活时会导致静音，严重破坏播放。因此改为纯前端       */
/* 驱动的平滑波动动画：播放期间跳动，暂停时归零（仅作视觉「音量波动」）。 */
/* ------------------------------------------------------------------ */
const EXPANDED_WIDTH = 320;
const COLLAPSED_HEIGHT = 30;

export function DynamicIslandHost() {
  const snapshot = useSyncExternalStore(subscribe, getSnapshot);
  const { item, revision } = snapshot;
  const { config } = useThemeColor();
  const primaryColor = config.primaryColor;
  const { liquidGlassEnabled, islandLiquidGlassEnabled } = useBackground();
  const { svgSupported } = useLiquidGlassRefraction();
  // 总开关 + 灵动岛子开关（设置页液态玻璃卡片内，默认关闭）同时打开才启用真实折射；不支持时回退磨砂玻璃
  const islandGlass = liquidGlassEnabled && islandLiquidGlassEnabled && svgSupported;
  const controls = useAnimationControls();
  const motionPreferences=useMotionPreferences();
  const contentDuration=motionPreferences.reduced||motionPreferences.mode==='off'?0:.18;
  const startIsland=useCallback((target:TargetAndTransition)=>{
    const prefs=getMotionPreferences(), duration=prefs.reduced||prefs.mode==='off'?0:.18;
    const bounce=!prefs.reduced&&prefs.jelly&&prefs.strength>0&&target.width!==undefined&&target.width!==30;
    const scales=jellyScales(prefs.strength);
    const destination={...target,scale:1};
    return new Promise<void>(resolve=>{
      const stop=subscribeMotion(()=>{controls.stop();controls.set(destination);stop();resolve();});
      void controls.start({...destination,...(bounce?{scale:[1,scales.peak,scales.compression,scales.settle,1]}:{}),
        transition:{duration,ease:'easeOut',width:{duration:prefs.mode==='fade'?0:duration},height:{duration:prefs.mode==='fade'?0:duration},scale:{duration:bounce?.62:0}}}).then(()=>{stop();resolve();},()=>{stop();resolve();});
    });
  },[controls]);
  const [displayed, setDisplayed] = useState<IslandItem | null>(null);
  const [textVisible, setTextVisible] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const [expandedTextVisible, setExpandedTextVisible] = useState(false);
  const textRef = useRef<HTMLDivElement>(null);
  const [textOverflowing, setTextOverflowing] = useState(false);

  const itemRef = useRef<IslandItem | null>(item);
  const visibleRef = useRef(false);
  const expandedRef = useRef(false);
  const lastRevisionRef = useRef(revision);
  const chainRef = useRef<Promise<void>>(Promise.resolve());
  const islandRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    itemRef.current = item;
  }, [item]);

  // 静默实时更新（如下载进度）：同 id 时同步 displayed 内容，不触发动画
  useEffect(() => {
    if (item && displayed?.id === item.id) {
      setDisplayed(item);
    }
  }, [item, displayed?.id]);

  // 文字显示不全时用右端淡出代替省略号：实时测量是否溢出
  useEffect(() => {
    const el = textRef.current;
    if (!el) return;
    const check = () => setTextOverflowing(el.scrollWidth > el.clientWidth + 1);
    check();
    const ro = new ResizeObserver(check);
    ro.observe(el);
    return () => ro.disconnect();
  }, [displayed, textVisible, expanded]);

  const run = useCallback((fn: () => Promise<void>) => {
    chainRef.current = chainRef.current.then(fn).catch(() => {});
  }, []);

  const playAppear = useCallback(async () => {
    if (visibleRef.current) return;
    const next = itemRef.current;
    if (!next) return;
    visibleRef.current = true;
    expandedRef.current = false;
    setExpanded(false);
    setExpandedTextVisible(false);
    setDisplayed(next);
    setTextVisible(false);
    // 1. 淡化出圆形（图标已直接显示在圆形上）
    const baseHeight = COLLAPSED_HEIGHT;
    await startIsland({ opacity: 1, width: 30, height: baseHeight });
    if (!visibleRef.current || !itemRef.current) return;
    // 2. 文字浮现，向两侧扩散成胶囊
    setTextVisible(true);
    const targetWidth = "auto";
    await startIsland({ width: targetWidth });
  }, [startIsland]);

  const playReplace = useCallback(async () => {
    const next = itemRef.current;
    if (!next) return;
    // 展开中收到新提示：直接换内容，保持展开状态
    if (expandedRef.current) {
      setDisplayed(next);
      return;
    }
    // 1. 缩成圆形（隐藏文字，图标保留在圆形上）
    setTextVisible(false);
    await startIsland({ width: 30 });
    if (!visibleRef.current || !itemRef.current) return;
    // 2. 换上新内容，再扩散成胶囊
    setDisplayed(next);
    setTextVisible(true);
    const targetWidth = "auto";
    await startIsland({ width: targetWidth });
  }, [startIsland]);

  const playDismiss = useCallback(async () => {
    // 1. 缩成圆形（图标保留在圆形上）
    expandedRef.current = false;
    setExpanded(false);
    setExpandedTextVisible(false);
    setTextVisible(false);
    await startIsland({ width: 30 });
    // 2. 淡出
    await startIsland({ opacity: 0 });
    visibleRef.current = false;
    setDisplayed(null);
  }, [startIsland]);

  const playExpand = useCallback(async () => {
    const next = itemRef.current;
    if (!next || expandedRef.current) return;

    // 音乐岛：折叠文字固定在顶部、不随胶囊高度移动，可即时同步展开/淡出/放大


    expandedRef.current = true;
    setExpanded(true);
    // 横向扩散成圆角长矩形（折叠短文案保持可见，稍后交叉淡出）
    // 持久更新岛为单行长条（30px 高）；普通提示按内容行数决定高度
    const expandHeight =
      next.persistent ? COLLAPSED_HEIGHT : next.description ? 62 : 34;
    await startIsland({
      width: EXPANDED_WIDTH,
      height: expandHeight,
    });
    if (!expandedRef.current) return;
    // 详细内容淡入，短文案淡出
    setExpandedTextVisible(true);
    setTextVisible(false);
  }, [startIsland]);

  const playCollapse = useCallback(async () => {
    if (!expandedRef.current) return;

    // 音乐岛：折叠文字固定在顶部、位置不变，可即时同步淡入/收缩


    expandedRef.current = false;
    // 1. 详情淡出（折叠短文案暂不显示，避免缩小过程中文字移动）
    setExpandedTextVisible(false);
    // 2. 切换回折叠内容（外层宽度仍为展开宽，overflow:hidden 使内层切换不产生可见跳变）
    setExpanded(false);
    // 3. 等一帧让折叠布局提交，避免「auto」测量到展开内容宽度的歧义
    await new Promise<void>((r) => requestAnimationFrame(() => requestAnimationFrame(() => r())));
    if (expandedRef.current) return; // 若期间被重新展开则中止
    // 4. 直接收缩回小灵动岛（胶囊），不再经过圆形
    const baseHeight = COLLAPSED_HEIGHT;
    const targetWidth = "auto";
    await startIsland({ width: targetWidth, height: baseHeight });
    // 5. 收缩完成后，短文案淡化浮现（纯透明度变化，无位置移动）
    setTextVisible(true);
  }, [startIsland]);

  const handleMouseEnter = useCallback(() => {
    const id = itemRef.current?.id;
    if (!visibleRef.current || !id || expandedRef.current) return;
    run(playExpand);
    hold(id); // 悬停期间不自动关闭
  }, [run, playExpand]);

  const handleMouseLeave = useCallback(() => {
    const id = itemRef.current?.id;
    if (!visibleRef.current || !expandedRef.current || !id) return;
    run(playCollapse);
    extend(id); // 移开后恢复自动关闭
  }, [run, playCollapse]);

  // 兜底：展开期间监听全局鼠标移动，光标确实离开灵动岛区域（含一定边距）则自动收起。
  // 解决个别情况下 onMouseLeave/onPointerLeave 未触发导致「移走光标仍保持展开」。
  const pendingLeaveRef = useRef(false);
  useEffect(() => {
    if (!expanded) return;
    const onMove = (e: MouseEvent) => {
      if (!expandedRef.current) return;
      const el = islandRef.current;
      if (!el) return;
      const r = el.getBoundingClientRect();
      const m = 24; // 容错边距，避免在边缘来回时误收
      const out =
        e.clientX < r.left - m ||
        e.clientX > r.right + m ||
        e.clientY < r.top - m ||
        e.clientY > r.bottom + m;
      if (out && !pendingLeaveRef.current) {
        pendingLeaveRef.current = true;
        requestAnimationFrame(() => {
          pendingLeaveRef.current = false;
          handleMouseLeave();
        });
      }
    };
    window.addEventListener("mousemove", onMove);
    return () => window.removeEventListener("mousemove", onMove);
  }, [expanded, handleMouseLeave]);

  // 点击：有自定义 onClick 直接触发（如完成态重启安装）；
  // 展开态收起为小灵动岛；持久岛折叠态点击展开（查看版本+进度）；普通提示折叠态点击关闭
  const handleClick = useCallback(() => {
    const it = itemRef.current;
    const id = it?.id;
    if (!it || !id) return;
    if (it.onClick) {
      it.onClick();
      return;
    }
    if (expandedRef.current) {
      run(playCollapse);
      extend(id); // 收起后恢复自动关闭计时
    } else if (it.persistent) {
      run(playExpand);
      hold(id); // 持久岛无自动关闭，hold 无副作用
    } else {
      close(id);
    }
  }, [run, playCollapse, playExpand]);

  useEffect(() => {
    if (item) {
      if (!visibleRef.current) {
        run(playAppear);
      } else if (revision !== lastRevisionRef.current) {
        run(playReplace);
      }
      lastRevisionRef.current = revision;
    } else if (visibleRef.current) {
      run(playDismiss);
    }
  }, [item, revision, run, playAppear, playReplace, playDismiss]);

  const pillBg = useColorModeValue("rgba(255,255,255,0.9)", "rgba(20,20,20,0.88)");
  const glassBg = useColorModeValue("rgba(255,255,255,0.10)", "rgba(12,12,12,0.22)");
  // 真实液态玻璃：SVG 位移折射 + 轻模糊 + 提饱和；折射强度/边缘带在 liquid-glass-svg-filter.tsx 顶部可调
  const glassBackdrop = `url(#${ISLAND_FILTER_ID}) blur(2px) saturate(1.35)`;
  const pillBorder = useColorModeValue("rgba(0,0,0,0.08)", "rgba(255,255,255,0.12)");
  const titleColor = useColorModeValue("#1a1a1a", "#ffffff");
  const descColor = useColorModeValue("rgba(0,0,0,0.62)", "rgba(255,255,255,0.66)");
  const barTrack = useColorModeValue("rgba(0,0,0,0.08)", "rgba(255,255,255,0.14)");

  // 折叠：只显示精简短文案；展开：显示完整标题 + 详细描述
  const collapsedText = displayed
    ? shortenMessage(displayed.title || displayed.description || "")
    : "";
  const detailTitle = displayed?.title || displayed?.description || "";
  const detailDesc =
    displayed?.title && displayed?.description ? displayed.description : undefined;

  return (
    <div
      style={{
        position: "fixed",
        top: 4,
        left: "50%",
        transform: "translateX(-50%)",
        zIndex: 1001,
        display: "flex",
        alignItems: "center",
        pointerEvents: "none",
      }}
    >
      <motion.div
        ref={islandRef}
        animate={controls}
        initial={{ opacity: 0, width: 30, height: COLLAPSED_HEIGHT }}
        role="status"
        aria-live="polite"
        onClick={handleClick}
        onMouseEnter={handleMouseEnter}
        onMouseLeave={handleMouseLeave}
        onPointerLeave={handleMouseLeave}
        className={islandGlass ? "real-liquid-glass" : undefined}
        style={{
          borderRadius: (expanded ? 20 : 999),
          background: islandGlass ? glassBg : pillBg,
          border: `1px solid ${pillBorder}`,
          backdropFilter: islandGlass ? glassBackdrop : "blur(20px)",
          WebkitBackdropFilter: islandGlass ? glassBackdrop : "blur(20px)",
          overflow: "hidden",
          cursor: "pointer",
          pointerEvents: displayed ? "auto" : "none",
          display: "flex",
          alignItems: "center",
          position: "relative",
        }}
      >
        
        <>
        {/* 图标始终显示（圆形阶段即图标），扩散/收缩时文字淡入淡出 */}
        <div style={{ padding: "0 0 0 4px", flexShrink: 0, display: "flex" }}>
          {displayed?.icon ??
            (displayed && (
              <StatusIcon
                status={displayed.status}
                iconKey={displayed.iconKey}
                primaryColor={primaryColor}
                size={22}
              />
            ))}
        </div>
        {/* 折叠状态：精简短文案（单行） */}
        <motion.div
          animate={{ opacity: textVisible && !expanded ? 1 : 0 }}
          transition={{ duration: contentDuration }}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            minWidth: 0,
            whiteSpace: "nowrap",
            paddingRight: 10,
            width: expanded ? 0 : "auto",
            overflow: "hidden",
          }}
        >
          {/* 中间留空 */}
          <div style={{ flex: 1, minWidth: 12 }} />
          <div
            ref={textRef}
            style={{
              fontSize: 11.5,
              fontWeight: 600,
              color: titleColor,
              whiteSpace: "nowrap",
              overflow: "hidden",
              maxWidth: 120,
              minWidth: 0,
              // 溢出时右端淡出，而不是显示省略号
              maskImage: textOverflowing
                ? "linear-gradient(to right, #000 88%, transparent 100%)"
                : undefined,
              WebkitMaskImage: textOverflowing
                ? "linear-gradient(to right, #000 88%, transparent 100%)"
                : undefined,
            }}
          >
            {collapsedText}
          </div>
        </motion.div>
          {/* 展开状态：持久更新岛为单行长条（中进度条/标题 + 右版本号）；普通提示为两行标题+描述 */}
          <div
            style={{
              display: "flex",
              minWidth: 0,
              width: expanded ? "auto" : 0,
              overflow: "hidden",
              ...(displayed?.persistent
                ? { flex: expanded ? 1 : 0, alignItems: "center", padding: "0 12px 0 4px" }
                : {
                    flexDirection: "column",
                    justifyContent: "center",
                    // 无描述（如「已切换为默认」单行提示）时文字右对齐，图标在左、文字靠右
                    marginLeft: detailDesc ? undefined : "auto",
                    padding: "5px 12px 5px 4px",
                    gap: 4,
                  }),
            }}
          >
            <motion.div
              animate={{ opacity: expandedTextVisible ? 1 : 0 }}
              transition={{ duration: contentDuration }}
              style={{
                display: "flex",
                minWidth: 0,
                ...(displayed?.persistent
                  ? { flex: 1, alignItems: "center", gap: 10 }
                  : { flexDirection: "column", gap: 4 }),
              }}
            >
              {displayed?.persistent ? (
                <>
                  {/* 单行长条：中间进度条(或标题) + 百分比 + 右侧版本号 */}
                  {displayed.progress !== undefined ? (
                    <div
                      style={{
                        flex: 1,
                        height: 7,
                        borderRadius: 999,
                        background: barTrack,
                        overflow: "hidden",
                      }}
                    >
                      <div
                        style={{
                          height: "100%",
                          borderRadius: 999,
                          background: primaryColor,
                          width: `${Math.max(0, Math.min(100, displayed.progress))}%`,
                          transition: "width 0.2s",
                        }}
                      />
                    </div>
                  ) : (
                    <div
                      style={{
                        flex: 1,
                        fontSize: 12,
                        fontWeight: 700,
                        color: titleColor,
                        whiteSpace: "nowrap",
                      }}
                    >
                      {detailTitle}
                    </div>
                  )}
                  {displayed.progress !== undefined && (
                    <div
                      style={{
                        fontSize: 10.5,
                        fontWeight: 700,
                        color: titleColor,
                        fontVariantNumeric: "tabular-nums",
                      }}
                    >
                      {Math.round(displayed.progress)}%
                    </div>
                  )}
                  {detailDesc && (
                    <div
                      style={{
                        fontSize: 11,
                        fontWeight: 600,
                        color: descColor,
                        whiteSpace: "nowrap",
                        flexShrink: 0,
                      }}
                    >
                      {detailDesc}
                    </div>
                  )}
                </>
              ) : displayed?.progress !== undefined ? (
                <>
                  {/* 标题行：左侧标题 + 右侧版本号 */}
                  <div style={{ display: "flex", alignItems: "baseline", gap: 10, minWidth: 0 }}>
                    <div
                      style={{
                        fontSize: 12,
                        fontWeight: 700,
                        color: titleColor,
                        whiteSpace: "nowrap",
                        overflow: "hidden",
                      }}
                    >
                      {detailTitle}
                    </div>
                    {detailDesc && (
                      <div
                        style={{
                          fontSize: 10.5,
                          color: descColor,
                          whiteSpace: "nowrap",
                          overflow: "hidden",
                        }}
                      >
                        {detailDesc}
                      </div>
                    )}
                  </div>
                  {/* 进度条行：主题色填充 + 百分比 */}
                  <div style={{ display: "flex", alignItems: "center", gap: 8, minWidth: 0 }}>
                    <div
                      style={{
                        flex: 1,
                        height: 6,
                        borderRadius: 999,
                        background: barTrack,
                        overflow: "hidden",
                      }}
                    >
                      <div
                        style={{
                          height: "100%",
                          borderRadius: 999,
                          background: primaryColor,
                          width: `${Math.max(0, Math.min(100, displayed.progress))}%`,
                          transition: "width 0.2s",
                        }}
                      />
                    </div>
                    <div
                      style={{
                        fontSize: 10.5,
                        fontWeight: 700,
                        color: titleColor,
                        fontVariantNumeric: "tabular-nums",
                      }}
                    >
                      {Math.round(displayed.progress)}%
                    </div>
                  </div>
                </>
              ) : (
                <>
                  <div
                    style={{
                      fontSize: 12,
                      fontWeight: 700,
                      color: titleColor,
                      whiteSpace: "nowrap",
                      overflow: "hidden",
                    }}
                  >
                    {detailTitle}
                  </div>
                  {detailDesc && (
                    <div
                      style={{
                        fontSize: 10.5,
                        lineHeight: 1.35,
                        color: descColor,
                        maxHeight: 30,
                        overflow: "hidden",
                      }}
                    >
                      {detailDesc}
                    </div>
                  )}
                </>
              )}
            </motion.div>
          </div>
        </>
      </motion.div>
    </div>
  );
}
