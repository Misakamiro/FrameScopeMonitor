import type { ReactNode } from "react";
import { motion, type HTMLMotionProps } from "framer-motion";
import type { LucideIcon } from "lucide-react";
import { motionTokens } from "../theme/motion";
import "./components.css";

type ButtonVariant = "primary" | "secondary" | "danger" | "ghost";

interface ButtonProps extends Omit<HTMLMotionProps<"button">, "children"> {
  variant?: ButtonVariant;
  icon?: LucideIcon;
  children: ReactNode;
}

export function Button({
  variant = "secondary",
  icon: Icon,
  children,
  className,
  disabled,
  ...props
}: ButtonProps) {
  return (
    <motion.button
      className={["fs-button", `fs-button--${variant}`, className]
        .filter(Boolean)
        .join(" ")}
      disabled={disabled}
      whileHover={disabled ? undefined : { y: -1 }}
      whileTap={disabled ? undefined : { scale: 0.97 }}
      transition={motionTokens.springPress}
      {...props}
    >
      {Icon ? <Icon aria-hidden="true" size={16} strokeWidth={2.2} /> : null}
      <span>{children}</span>
    </motion.button>
  );
}
