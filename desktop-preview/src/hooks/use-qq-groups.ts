import { invoke, convertFileSrc } from "@tauri-apps/api/core";
import { useState, useEffect, useCallback } from "react";

/** 单个 QQ 群信息 */
export interface QqGroup {
  name: string;
  number: string;
  link: string;
  /** 群图标 URL（远程，如 gitee raw），为空时前端用默认 QQ 图标。与社区工具图标同样直接 <img src> 加载 */
  icon?: string;
}

/**
 * 拉取官方 QQ 群列表（后端从 gitee qq_groups.json 获取，含内置兜底），
 * 返回 { groups, loading, reload }。供首页弹窗与设置页共用。
 */
export function useQQGroups() {
  const [groups, setGroups] = useState<QqGroup[]>([]);
  const [loading, setLoading] = useState(false);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await invoke<QqGroup[]>("get_qq_groups");
      setGroups(data || []);
    } catch (e) {
      console.error("[QQGroups] get_qq_groups failed:", e);
      setGroups([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    reload();
  }, [reload]);

  return { groups, loading, reload };
}

/** 打开外链（优先 Rust 端系统浏览器，兜底 plugin-opener） */
export async function openExternal(url: string) {
  try {
    await invoke("open_system_browser", { url });
    return;
  } catch (e) {
    console.warn("[QQGroups] open_system_browser failed:", e);
  }
  try {
    const { openUrl } = await import("@tauri-apps/plugin-opener");
    await openUrl(url);
  } catch (e) {
    console.error("[QQGroups] openUrl failed:", e);
  }
}

/**
 * 解析群图标 URL 为可显示地址：让后端从 gitee 下载到缓存，再用 convertFileSrc 走 Tauri 资产协议。
 * （WebView 通常无法直接显示 gitee raw 图；走后端下载即可正常显示，来源仍是 gitee）
 * 返回 { src, loading }：loading 为 true 表示图标还没下载好。
 */
export function useQQIcon(url?: string) {
  const [state, setState] = useState<{ src?: string; loading: boolean }>({ src: undefined, loading: false });

  useEffect(() => {
    if (!url) {
      setState({ src: undefined, loading: false });
      return;
    }
    setState((s) => ({ ...s, loading: true }));
    let alive = true;
    (async () => {
      try {
        const path = await invoke<string>("get_qq_group_icon", { url });
        if (alive) setState({ src: path ? convertFileSrc(path) : undefined, loading: false });
      } catch (e) {
        console.error("[QQGroups] get_qq_group_icon failed:", e);
        if (alive) setState({ src: undefined, loading: false });
      }
    })();
    return () => {
      alive = false;
    };
  }, [url]);

  return state;
}