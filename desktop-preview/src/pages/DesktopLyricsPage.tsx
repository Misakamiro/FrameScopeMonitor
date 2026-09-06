/**
 * 桌面歌词独立窗口页面
 *
 * 特性：
 * - 卡拉OK逐字高亮歌词渲染
 * - 单行/双行模式
 * - 悬浮控制栏（上一句/播放/下一句/随机/锁定）
 * - 未锁定：可拖动 + 悬浮显示背景轮廓 + 完整控制
 * - 锁定：鼠标穿透 + 悬浮仅显示解锁按钮
 * - 窗口位置记忆
 */

import { useEffect, useRef, useState, useCallback } from "react";
import { useDesktopLyricsSync } from "@/hooks/useDesktopLyricsSync";
import type { ControlAction } from "@/hooks/useDesktopLyricsSync";
import { LyricsCanvas } from "@/components/desktop-lyrics/LyricsCanvas";
import { LyricsControlBar } from "@/components/desktop-lyrics/LyricsControlBar";
import {
  startDragging,
  setIgnoreCursorEvents,
  saveWindowPosition,
  restoreWindowPosition,
  showUnlockBtn,
  hideUnlockBtn,
  onUnlockBtnClicked,
  isCursorInWindow,
} from "@/lib/desktop-lyrics-window";

export default function DesktopLyricsPage() {
  const {
    song,
    karaokeLines,
    estimatedTime,
    isPlaying,
    playMode,
    settings,
    isLocked,
    volume,
    sendControl,
    setVolume,
    lock,
    unlock,
  } = useDesktopLyricsSync();

  const [isHovered, setIsHovered] = useState(false);
  const isLockedRef = useRef(isLocked);
  isLockedRef.current = isLocked;
  const hideUnlockBtnRef = useRef(settings.hideUnlockBtn);
  hideUnlockBtnRef.current = settings.hideUnlockBtn;

  // 强制 html/body/#root 背景透明
  // index.css 中 :root 设置了 background-color: #242424，
  // 这会填充 WebView2 导致 setIgnoreCursorEvents 穿透失效。
  // 桌面歌词窗口必须确保背景完全透明。
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

  // 恢复窗口位置
  useEffect(() => {
    restoreWindowPosition();
  }, []);

  // 锁定/解锁状态处理
  // 锁定：开启歌词窗口穿透，轮询光标位置决定解锁按钮显隐
  // 解锁：关闭穿透，隐藏解锁按钮
  useEffect(() => {
    if (!isLocked) {
      setIgnoreCursorEvents(false);
      hideUnlockBtn();
      return;
    }

    // 锁定状态：开启穿透
    setIgnoreCursorEvents(true);
    setIsHovered(false);

    let active = true;
    let btnShown = false;
    let intervalId: ReturnType<typeof setInterval>;

    // 每 200ms 轮询：光标在窗口内 → 显示按钮，否则 → 隐藏
    intervalId = setInterval(async () => {
      if (!active || !isLockedRef.current) return;
      try {
        // 隐藏解锁按钮开关开启时，强制隐藏，鼠标移入也不自动显示
        if (hideUnlockBtnRef.current) {
          if (btnShown) {
            btnShown = false;
            hideUnlockBtn();
          }
          return;
        }
        const inside = await isCursorInWindow();
        if (inside && !btnShown) {
          btnShown = true;
          showUnlockBtn();
        } else if (!inside && btnShown) {
          btnShown = false;
          hideUnlockBtn();
        }
      } catch {
        // ignore
      }
    }, 200);

    return () => {
      active = false;
      clearInterval(intervalId);
      hideUnlockBtn();
    };
  }, [isLocked]);

  // 解锁时，解锁按钮窗口消失但光标已在桌面歌词窗口内，
  // mouseenter 不会触发（光标没有跨窗口边界），需要手动显示控制栏
  useEffect(() => {
    if (!isLocked) {
      setIsHovered(true);
    }
  }, [isLocked]);

  // 监听独立解锁按钮窗口的点击事件
  useEffect(() => {
    const setup = async () => {
      const unlisten = await onUnlockBtnClicked(() => {
        unlock();
      });
      return unlisten;
    };

    let unlisten: (() => void) | undefined;
    setup().then((fn) => { unlisten = fn; });
    return () => { unlisten?.(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // 未锁定状态：正常鼠标事件
  const handleMouseEnter = useCallback(() => {
    if (!isLockedRef.current) {
      setIsHovered(true);
    }
  }, []);

  const handleMouseLeave = useCallback(() => {
    if (!isLockedRef.current) {
      setIsHovered(false);
    }
  }, []);

  // 拖动窗口（未锁定时）
  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      if (isLockedRef.current) return;
      // 仅在非控制栏区域触发拖动
      // 控制栏内部会 stopPropagation，所以这里收到的都是背景区域
      startDragging();
      // 拖动结束后保存位置
      const handleUp = () => {
        saveWindowPosition();
        window.removeEventListener("mouseup", handleUp);
      };
      window.addEventListener("mouseup", handleUp);
    },
    []
  );

  // 控制指令
  const handleControl = useCallback(
    (action: ControlAction, value?: number) => {
      if (action === "lock") {
        lock();
      } else if (action === "unlock") {
        unlock();
      } else if (action === "volume") {
        setVolume(value ?? 0.7);
      } else {
        sendControl(action);
      }
    },
    [sendControl, lock, unlock, setVolume]
  );

  // 窗口样式
  const containerStyle: React.CSSProperties = {
    width: "100%",
    height: "100%",
    position: "relative",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    cursor: isLocked ? "default" : isHovered ? "move" : "default",
    transition: "background 0.2s ease, border-radius 0.2s ease",
    // 未锁定 + 悬浮时显示背景轮廓
    ...(isHovered && !isLocked
      ? {
          background: "rgba(0, 0, 0, 0.15)",
          borderRadius: "12px",
          border: "1px solid rgba(255, 255, 255, 0.12)",
        }
      : {}),
  };

  // 无歌曲时显示占位
  if (!song) {
    return (
      <div
        style={containerStyle}
        onMouseEnter={handleMouseEnter}
        onMouseLeave={handleMouseLeave}
        onMouseDown={handleMouseDown}
      >
        <span
          style={{
            fontSize: `${settings.fontSize * 0.7}px`,
            color: settings.baseColor,
            fontWeight: "bold",
            textShadow: `
              -1px -1px 0 rgba(0,0,0,0.8),
              1px -1px 0 rgba(0,0,0,0.8),
              -1px 1px 0 rgba(0,0,0,0.8),
              1px 1px 0 rgba(0,0,0,0.8)
            `,
          }}
        >
          ♪ NexBox 桌面歌词 ♪
        </span>
        {/* 控制栏仅未锁定时显示，锁定时的解锁按钮由独立小窗口提供 */}
        {isHovered && !isLocked && (
          <LyricsControlBar
            isPlaying={isPlaying}
            playMode={playMode}
            volume={volume}
            onControl={handleControl}
          />
        )}
      </div>
    );
  }

  return (
    <div
      style={containerStyle}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
      onMouseDown={handleMouseDown}
    >
      <div
        style={{
          width: "100%",
          height: "100%",
          padding: "8px 16px",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        <LyricsCanvas
          lines={karaokeLines}
          currentTime={estimatedTime}
          fontSize={settings.fontSize}
          fontFamily={settings.fontFamily}
          highlightColor={settings.highlightColor}
          baseColor={settings.baseColor}
          lineCount={settings.lineCount}
          isPlaying={isPlaying}
          showTranslation={settings.showTranslation}
        />
      </div>

      {/* 控制栏仅未锁定时悬浮显示，锁定时的解锁按钮由独立小窗口提供 */}
      {isHovered && !isLocked && (
        <LyricsControlBar
          isPlaying={isPlaying}
          playMode={playMode}
          volume={volume}
          onControl={handleControl}
        />
      )}
    </div>
  );
}
