/** 自定义页面上的卡片实例 */
export interface CustomCardInstance {
  /** 唯一实例 ID（允许同一工具添加多次） */
  instanceId: string;
  /** 对应 searchIndex 中的 item id */
  itemId: string;
  /** 在画布上的位置 */
  position: { x: number; y: number };
  /** 卡片尺寸 */
  size: { width: number; height: number };
  /** 圆角半径 (px) */
  borderRadius: number;
}

/** 自定义页面的完整配置 */
export interface CustomDashboardConfig {
  /** 所有卡片实例 */
  cards: CustomCardInstance[];
  /** 画布背景模式 */
  backgroundMode: "transparent" | "grid" | "dots";
}

/** 导航栏排序后的路径数组 */
export type NavOrder = string[];
