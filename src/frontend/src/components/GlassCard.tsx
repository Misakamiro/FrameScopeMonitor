import type { HTMLAttributes, ReactNode } from "react";
import "./components.css";

interface GlassCardProps extends Omit<HTMLAttributes<HTMLDivElement>, "children"> {
  children: ReactNode;
  density?: "normal" | "compact";
}

export function GlassCard({
  children,
  className,
  density = "normal",
  ...props
}: GlassCardProps) {
  return (
    <div
      className={["glass-card", density === "compact" ? "glass-card--compact" : "", className]
        .filter(Boolean)
        .join(" ")}
      {...props}
    >
      {children}
    </div>
  );
}
