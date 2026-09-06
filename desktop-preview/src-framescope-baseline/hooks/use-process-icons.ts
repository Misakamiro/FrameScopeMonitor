import { invoke } from "@tauri-apps/api/core";
import { useCallback, useRef, useState } from "react";

/**
 * 进程图标 hook：以 exe 路径为键缓存 data URI。
 * - 批量去重缺失项，一次 IPC 获取所有未缓存图标（后端 get_process_icons 批量提取）
 * - inflight 防重入，避免列表自动刷新时重复触发请求
 */
export function useProcessIcons() {
  const cacheRef = useRef<Record<string, string>>({});
  const inflightRef = useRef(false);
  const [icons, setIcons] = useState<Record<string, string>>({});

  const ensureIcons = useCallback(async (exePaths: string[]) => {
    const missing = exePaths.filter(p => p && !(p in cacheRef.current));
    if (missing.length === 0 || inflightRef.current) return;
    inflightRef.current = true;
    try {
      const dataUris = await invoke<string[]>("get_process_icons", { exePaths: missing });
      const next: Record<string, string> = {};
      missing.forEach((p, i) => {
        if (dataUris[i]) next[p] = dataUris[i];
      });
      cacheRef.current = { ...cacheRef.current, ...next };
      setIcons({ ...cacheRef.current });
    } catch {
      // 单次批量获取失败不影响列表渲染
    } finally {
      inflightRef.current = false;
    }
  }, []);

  return { icons, ensureIcons };
}
