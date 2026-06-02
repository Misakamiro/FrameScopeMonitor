import type { ComponentPropsWithoutRef, ReactNode } from "react";
import type { LucideIcon } from "lucide-react";
import "./components.css";

type ButtonVariant = "primary" | "secondary" | "tonal" | "danger" | "ghost";

interface ButtonProps extends Omit<ComponentPropsWithoutRef<"button">, "children"> {
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
    <button
      className={["fs-button", `fs-button--${variant}`, className]
        .filter(Boolean)
        .join(" ")}
      disabled={disabled}
      {...props}
    >
      {Icon ? <Icon aria-hidden="true" size={16} strokeWidth={2.2} /> : null}
      <span>{children}</span>
    </button>
  );
}
