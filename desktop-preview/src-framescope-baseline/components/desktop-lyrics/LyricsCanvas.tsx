/**
 * 桌面歌词渲染组件
 *
 * 特性：
 * - 当前行使用双层叠加实现卡拉OK逐字高亮效果
 * - 支持单行(仅当前句)和双行(当前句+下一句)模式
 * - 文字描边保证在任意背景下可读
 * - RAF 更新进度，60fps 流畅
 */

import { useEffect, useRef, useState, memo, useMemo } from "react";
import type { KaraokeLine } from "@/types/music";
import { getLineProgress, calculateScrollOffset } from "@/lib/karaoke-lyrics";

interface LyricsCanvasProps {
  lines: KaraokeLine[];
  currentTime: number;
  fontSize: number;
  fontFamily: string;
  highlightColor: string;
  baseColor: string;
  lineCount: 1 | 2;
  isPlaying: boolean;
  showTranslation: boolean;
}

/** 计算当前活跃行索引 */
function calcActiveIndex(lines: KaraokeLine[], currentTime: number): number {
  if (lines.length === 0) return -1;
  let idx = -1;
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].time <= currentTime) idx = i;
    else break;
  }
  return idx;
}

/** 当前行的卡拉OK效果渲染 */
function ActiveKaraokeLine({
  line,
  nextLine,
  currentTime,
  fontSize,
  fontFamily,
  highlightColor,
  baseColor,
  isPlaying,
  showTranslation,
}: {
  line: KaraokeLine;
  nextLine?: KaraokeLine;
  currentTime: number;
  fontSize: number;
  fontFamily: string;
  highlightColor: string;
  baseColor: string;
  isPlaying: boolean;
  showTranslation: boolean;
}) {
  const overlayRef = useRef<HTMLSpanElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const scrollRef = useRef<HTMLSpanElement>(null);
  const [scrollNeeded, setScrollNeeded] = useState(false);
  const scrollLimitRef = useRef(0);
  const currentTimeRef = useRef(currentTime);
  currentTimeRef.current = currentTime;

  // 文字描边样式
  const textShadow = `
    -1px -1px 0 rgba(0,0,0,0.8),
    1px -1px 0 rgba(0,0,0,0.8),
    -1px 1px 0 rgba(0,0,0,0.8),
    1px 1px 0 rgba(0,0,0,0.8),
    0 0 8px rgba(0,0,0,0.5)
  `;

  // 检测超长歌词
  useEffect(() => {
    if (!containerRef.current || !scrollRef.current) return;
    const checkOverflow = () => {
      if (!scrollRef.current || !containerRef.current) return;
      const overflow = scrollRef.current.scrollWidth - containerRef.current.clientWidth;
      const needed = overflow > 4;
      setScrollNeeded(needed);
      scrollLimitRef.current = Math.max(0, overflow / 2);
    };
    checkOverflow();
    const timer = setTimeout(checkOverflow, 100);
    return () => clearTimeout(timer);
  }, [line.text, fontSize]);

  // RAF 更新进度（仅播放时运行）
  useEffect(() => {
    if (!isPlaying || !overlayRef.current) return;
    const el = overlayRef.current;
    el.style.width = "0%";

    let rafId: number;
    let running = true;
    const tick = () => {
      if (!running) return;
      const t = currentTimeRef.current;
      const progress = getLineProgress(line, nextLine, t);
      el.style.width = `${(progress * 100).toFixed(2)}%`;

      if (scrollNeeded && scrollRef.current) {
        const offset = calculateScrollOffset(progress, scrollLimitRef.current);
        scrollRef.current.style.transform = `translate3d(${offset.toFixed(2)}px, 0, 0)`;
      }

      rafId = requestAnimationFrame(tick);
    };
    rafId = requestAnimationFrame(tick);
    return () => {
      running = false;
      cancelAnimationFrame(rafId);
    };
  }, [line, nextLine, scrollNeeded, isPlaying]);

  const sharedStyle: React.CSSProperties = {
    display: "inline-block",
    whiteSpace: "nowrap",
    fontSize: `${fontSize}px`,
    fontFamily: fontFamily || undefined,
    fontWeight: "bold",
    lineHeight: 1.4,
    letterSpacing: "0px",
    textShadow,
  };

  const maskFade = Math.min(18, Math.max(6, fontSize * 0.3));
  const maskGradient = `linear-gradient(90deg, #000 0%, #000 calc(100% - ${maskFade}px), rgba(0,0,0,0.3) 100%)`;

  return (
    <div
      ref={containerRef}
      style={{
        textAlign: "center",
        overflow: "hidden",
        position: "relative",
        padding: "2px 0",
      }}
    >
      <span
        ref={scrollRef}
        style={{
          display: "inline-block",
          position: "relative",
          whiteSpace: "nowrap",
          transform: "translate3d(0, 0, 0)",
          willChange: "transform",
        }}
      >
        {/* 底层：完整歌词，暗色 */}
        <span style={{ ...sharedStyle, color: baseColor }}>
          {line.text}
        </span>
        {/* 顶层：高亮歌词，用 width 裁剪 */}
        <span
          ref={overlayRef}
          style={{
            ...sharedStyle,
            position: "absolute",
            top: 0,
            left: 0,
            width: "0%",
            overflow: "hidden",
            color: highlightColor,
            maskImage: maskGradient,
            WebkitMaskImage: maskGradient,
          }}
        >
          {line.text}
        </span>
      </span>
      {/* 当前行翻译：清晰硬描边 + 更大字号 */}
      {showTranslation && line.translation && (
        <div
          style={{
            marginTop: "2px",
            fontSize: `${Math.max(18, Math.round(fontSize * 0.65))}px`,
            fontFamily: fontFamily || undefined,
            color: baseColor,
            fontWeight: "bold",
            lineHeight: 1.3,
            textShadow: `
              -1px -1px 0 rgba(0,0,0,0.9),
              1px -1px 0 rgba(0,0,0,0.9),
              -1px 1px 0 rgba(0,0,0,0.9),
              1px 1px 0 rgba(0,0,0,0.9)
            `,
            WebkitFontSmoothing: "antialiased",
            textAlign: "center",
            whiteSpace: "nowrap",
            overflow: "hidden",
            textOverflow: "ellipsis",
            maxWidth: "100%",
          }}
        >
          {line.translation}
        </div>
      )}
    </div>
  );
}

function LyricsCanvasInner({
  lines,
  currentTime,
  fontSize,
  fontFamily,
  highlightColor,
  baseColor,
  lineCount,
  isPlaying,
  showTranslation,
}: LyricsCanvasProps) {
  const activeIndex = useMemo(
    () => calcActiveIndex(lines, currentTime),
    [lines, currentTime]
  );

  // 空歌词占位
  if (lines.length === 0 || activeIndex < 0) {
    return (
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          height: "100%",
          width: "100%",
        }}
      >
        <span
          style={{
            fontSize: `${fontSize * 0.7}px`,
            fontFamily: fontFamily || undefined,
            color: baseColor,
            fontWeight: "bold",
            textShadow: `
              -1px -1px 0 rgba(0,0,0,0.8),
              1px -1px 0 rgba(0,0,0,0.8),
              -1px 1px 0 rgba(0,0,0,0.8),
              1px 1px 0 rgba(0,0,0,0.8)
            `,
          }}
        >
          ♪ 暂无歌词 ♪
        </span>
      </div>
    );
  }

  const currentLine = lines[activeIndex];
  const nextLine = lines[activeIndex + 1];

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        width: "100%",
        gap: "4px",
      }}
    >
      <ActiveKaraokeLine
        line={currentLine}
        nextLine={nextLine}
        currentTime={currentTime}
        fontSize={fontSize}
        fontFamily={fontFamily}
        highlightColor={highlightColor}
        baseColor={baseColor}
        isPlaying={isPlaying}
        showTranslation={showTranslation}
      />

      {lineCount === 2 && nextLine && (
        <div
          style={{
            overflow: "hidden",
            textAlign: "center",
            padding: "2px 0",
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
          }}
        >
          <span
            style={{
              display: "inline-block",
              whiteSpace: "nowrap",
              fontSize: `${fontSize * 0.7}px`,
              fontFamily: fontFamily || undefined,
              fontWeight: "bold",
              color: baseColor,
              opacity: 0.6,
              textShadow: `
                -1px -1px 0 rgba(0,0,0,0.6),
                1px -1px 0 rgba(0,0,0,0.6),
                -1px 1px 0 rgba(0,0,0,0.6),
                1px 1px 0 rgba(0,0,0,0.6)
              `,
            }}
          >
            {nextLine.text}
          </span>
          {showTranslation && nextLine.translation && (
            <span
              style={{
                display: "block",
                maxWidth: "100%",
                whiteSpace: "nowrap",
                overflow: "hidden",
                textOverflow: "ellipsis",
                fontSize: `${Math.max(14, Math.round(fontSize * 0.48))}px`,
                fontFamily: fontFamily || undefined,
                fontWeight: "bold",
                color: baseColor,
                opacity: 0.6,
                lineHeight: 1.3,
                textShadow: `
                  -1px -1px 0 rgba(0,0,0,0.8),
                  1px -1px 0 rgba(0,0,0,0.8),
                  -1px 1px 0 rgba(0,0,0,0.8),
                  1px 1px 0 rgba(0,0,0,0.8)
                `,
                WebkitFontSmoothing: "antialiased",
              }}
            >
              {nextLine.translation}
            </span>
          )}
        </div>
      )}
    </div>
  );
}

export const LyricsCanvas = memo(LyricsCanvasInner);
