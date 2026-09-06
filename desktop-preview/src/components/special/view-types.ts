import type { ComponentType } from "react";

export interface ViewItem {
  id: string;
  path: string;
  icon: ComponentType<{ size?: number; strokeWidth?: number; color?: string }>;
  titleKey: string;
  descriptionKey: string;
  color: string;
  beta?: boolean;
}
