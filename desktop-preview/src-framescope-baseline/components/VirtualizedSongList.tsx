/**
 * 虚拟化歌曲列表 — 渐进式加载
 *
 * 策略：初始渲染前 BATCH_SIZE 首，滚动接近底部时自动加载下一批。
 * 避免一次性渲染数百个 SongRow（每个含图片/按钮/Tooltip）导致卡顿。
 *
 * 性能：
 * - 使用 IntersectionObserver 检测底部哨兵元素，无需手动监听 scroll
 * - 批量大小 50，足以覆盖一般屏幕高度，滚动时无感知
 * - 加载更多时插入新 DOM 节点，已渲染的不受影响
 */

import { useEffect, useRef, useState, memo, useCallback, type ReactNode } from "react";
import { Box, VStack, Spinner, Text } from "@chakra-ui/react";

interface VirtualizedSongListProps<T> {
  items: T[];
  renderItem: (item: T, index: number) => ReactNode;
  batchSize?: number;
  /** 滚动到距底部多少 px 时触发加载下一批 */
  triggerDistance?: number;
  /** key 前缀，用于切歌时重置 */
  resetKey?: string | number;
  /** 空状态文案 */
  emptyText?: string;
  /** loading 状态（外部加载中） */
  loading?: boolean;
  /** 外部 loading 时的文案 */
  loadingText?: string;
  /** 自定义滚动容器 sx */
  sx?: Record<string, unknown>;
  /** spacing between items */
  spacing?: string | number;
  /** 自定义滚动容器样式 */
  scrollbarSx?: Record<string, unknown>;
  /** 当接近列表末尾时触发的回调（用于从后端加载更多） */
  onLoadMore?: () => void;
  /** 外部判断是否还有更多数据（后端未加载完） */
  hasMoreServer?: boolean;
}

const DEFAULT_BATCH = 50;
const DEFAULT_TRIGGER = 300;

function VirtualizedSongListInner<T>({
  items,
  renderItem,
  batchSize = DEFAULT_BATCH,
  triggerDistance = DEFAULT_TRIGGER,
  resetKey,
  emptyText = "暂无曲目",
  loading = false,
  loadingText = "加载中...",
  sx,
  spacing = 1,
  scrollbarSx,
  onLoadMore,
  hasMoreServer = false,
}: VirtualizedSongListProps<T>) {
  const [visibleCount, setVisibleCount] = useState(batchSize);
  const sentinelRef = useRef<HTMLDivElement>(null);
  const scrollRef = useRef<HTMLDivElement>(null);

  // resetKey 变化时重置（切歌单/切搜索结果）
  useEffect(() => {
    setVisibleCount(batchSize);
    if (scrollRef.current) {
      scrollRef.current.scrollTop = 0;
    }
  }, [resetKey, batchSize]);

  // IntersectionObserver 检测哨兵元素进入视口
  const loadMore = useCallback(() => {
    setVisibleCount((prev) => {
      if (prev >= items.length) {
        // 本地数据已全部展示，触发后端加载更多
        onLoadMore?.();
        return prev;
      }
      return Math.min(prev + batchSize, items.length);
    });
  }, [items.length, batchSize, onLoadMore]);

  useEffect(() => {
    const sentinel = sentinelRef.current;
    const root = scrollRef.current;
    if (!sentinel || !root) return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting) {
          loadMore();
        }
      },
      {
        root,
        rootMargin: `${triggerDistance}px 0px`,
        threshold: 0,
      }
    );

    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [loadMore, triggerDistance]);

  // 当 items 增加时（如异步加载完成），确保 visibleCount 至少为 batchSize
  useEffect(() => {
    if (items.length > 0 && visibleCount === 0) {
      setVisibleCount(batchSize);
    }
  }, [items.length, visibleCount, batchSize]);

  const visibleItems = items.slice(0, visibleCount);
  const hasMore = visibleCount < items.length;

  if (loading) {
    return (
      <VStack py={6}>
        <Spinner size="sm" />
        <Text fontSize="xs" color="gray.500">{loadingText}</Text>
      </VStack>
    );
  }

  if (items.length === 0) {
    return (
      <Text fontSize="xs" color="gray.500" py={4} textAlign="center">
        {emptyText}
      </Text>
    );
  }

  return (
    <Box
      ref={scrollRef}
      flex={1}
      overflowY="auto"
      overflowX="hidden"
      sx={{
        ...scrollbarSx,
        ...sx,
      }}
    >
      <VStack spacing={spacing} align="stretch">
        {visibleItems.map((item, i) => renderItem(item, i))}
      </VStack>

      {/* 底部哨兵：进入视口时触发加载下一批 / 后端更多 */}
      {(hasMore || hasMoreServer) && (
        <Box ref={sentinelRef} py={3} textAlign="center">
          <Spinner size="xs" />
          <Text fontSize="xs" color="gray.500" mt={1}>
            {hasMore ? `加载更多... (${visibleCount}/${items.length})` : "加载更多..."}
          </Text>
        </Box>
      )}

      {/* 全部加载完成时显示总数 */}
      {!hasMore && !hasMoreServer && items.length > batchSize && (
        <Text fontSize="xs" color="gray.500" py={2} textAlign="center" opacity={0.6}>
          共 {items.length} 首
        </Text>
      )}
    </Box>
  );
}

export const VirtualizedSongList = memo(VirtualizedSongListInner) as <T>(
  props: VirtualizedSongListProps<T>
) => React.JSX.Element;
