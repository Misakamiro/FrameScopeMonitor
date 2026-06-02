export interface VirtualListWindowInput {
  rowCount: number;
  rowHeight: number;
  scrollTop: number;
  viewportHeight: number;
  overscan: number;
}

export interface VirtualListWindowState {
  startIndex: number;
  endIndex: number;
  renderedCount: number;
  paddingTop: number;
  paddingBottom: number;
}

export function getVirtualListWindow({
  rowCount,
  rowHeight,
  scrollTop,
  viewportHeight,
  overscan,
}: VirtualListWindowInput): VirtualListWindowState {
  const safeRowCount = Math.max(0, Math.floor(rowCount));
  if (safeRowCount === 0) {
    return {
      startIndex: 0,
      endIndex: 0,
      renderedCount: 0,
      paddingTop: 0,
      paddingBottom: 0,
    };
  }

  const safeRowHeight = Math.max(1, rowHeight);
  const safeViewportHeight = Math.max(safeRowHeight, viewportHeight);
  const safeOverscan = Math.max(0, Math.floor(overscan));
  const firstVisibleIndex = Math.min(
    safeRowCount - 1,
    Math.max(0, Math.floor(Math.max(0, scrollTop) / safeRowHeight)),
  );
  const startIndex = Math.max(0, firstVisibleIndex - safeOverscan);
  const visibleRows = Math.ceil(safeViewportHeight / safeRowHeight) + 1;
  const endIndex = Math.min(safeRowCount, startIndex + visibleRows + safeOverscan * 2);

  return {
    startIndex,
    endIndex,
    renderedCount: endIndex - startIndex,
    paddingTop: startIndex * safeRowHeight,
    paddingBottom: Math.max(0, safeRowCount - endIndex) * safeRowHeight,
  };
}
