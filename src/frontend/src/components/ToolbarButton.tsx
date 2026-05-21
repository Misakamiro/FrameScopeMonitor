import type { LucideIcon } from "lucide-react";
import { motion, type HTMLMotionProps } from "framer-motion";
import { motionTokens } from "../theme/motion";
import "./components.css";

interface ToolbarButtonProps extends Omit<HTMLMotionProps<"button">, "children"> {
  icon: LucideIcon;
  label: string;
}

export function ToolbarButton({ icon: Icon, label, className, disabled, ...props }: ToolbarButtonProps) {
  return (
    <motion.button
      className={["toolbar-button", className].filter(Boolean).join(" ")}
      type="button"
      aria-label={label}
      title={label}
      disabled={disabled}
      whileHover={disabled ? undefined : { y: -1 }}
      whileTap={disabled ? undefined : { scale: 0.96 }}
      transition={motionTokens.springPress}
      {...props}
    >
      <Icon aria-hidden="true" size={17} />
    </motion.button>
  );
}
