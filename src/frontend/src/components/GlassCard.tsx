import type { ReactNode } from "react";
import { motion, type HTMLMotionProps } from "framer-motion";
import { motionTokens } from "../theme/motion";
import "./components.css";

interface GlassCardProps extends Omit<HTMLMotionProps<"div">, "children"> {
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
    <motion.div
      className={["glass-card", density === "compact" ? "glass-card--compact" : "", className]
        .filter(Boolean)
        .join(" ")}
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      transition={motionTokens.springStandard}
      {...props}
    >
      {children}
    </motion.div>
  );
}
