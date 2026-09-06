/**
 * 桌面歌词解锁按钮独立窗口页面
 *
 * 此窗口永不穿透，固定在歌词窗口顶部中央。
 * CSS 控制视觉显隐：默认半透明，hover 时完全显示。
 * 点击时调用 Rust 命令 unlock_lyrics 完成解锁流程。
 */
import { useCallback, useEffect } from "react";
import { invoke } from "@tauri-apps/api/core";
import { Unlock } from "lucide-react";
import { Tooltip } from "@chakra-ui/react";

export default function LyricsUnlockBtnPage() {
  // 确保窗口背景完全透明（该窗口设置了 transparent: true）
  useEffect(() => {
    const html = document.documentElement;
    const body = document.body;
    const root = document.getElementById("root");
    const prevHtmlBg = html.style.background;
    const prevBodyBg = body.style.background;
    const prevRootBg = root?.style.background;

    html.style.background = "transparent";
    body.style.background = "transparent";
    if (root) root.style.background = "transparent";

    return () => {
      html.style.background = prevHtmlBg;
      body.style.background = prevBodyBg;
      if (root) root.style.background = prevRootBg || "";
    };
  }, []);
  const handleUnlock = useCallback(async () => {
    try {
      await invoke("unlock_lyrics");
    } catch (e) {
      console.error("[LyricsUnlockBtn] unlock_lyrics failed:", e);
    }
  }, []);

  return (
    <div
      style={{
        width: "100%",
        height: "100%",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        background: "transparent",
        cursor: "default",
      }}
    >
      <Tooltip label="解锁歌词">
        <div
          onClick={handleUnlock}
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            width: "36px",
            height: "36px",
            borderRadius: "50%",
            cursor: "pointer",
            opacity: 0.35,
            transition: "opacity 0.2s ease, background 0.15s ease",
            background: "transparent",
          }}
          onMouseEnter={(e) => {
            e.currentTarget.style.opacity = "1";
            e.currentTarget.style.background = "rgba(0,0,0,0.35)";
          }}
          onMouseLeave={(e) => {
            e.currentTarget.style.opacity = "0.35";
            e.currentTarget.style.background = "transparent";
          }}
        >
          <Unlock size={20} color="rgba(255,255,255,0.95)" />
        </div>
      </Tooltip>
    </div>
  );
}
