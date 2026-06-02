import type { ComponentPropsWithoutRef } from "react";
import type { LucideIcon } from "lucide-react";
import "./components.css";

interface ToolbarButtonProps extends Omit<ComponentPropsWithoutRef<"button">, "children"> {
  icon: LucideIcon;
  label: string;
}

export function ToolbarButton({ icon: Icon, label, className, disabled, ...props }: ToolbarButtonProps) {
  return (
    <button
      className={["toolbar-button", className].filter(Boolean).join(" ")}
      type="button"
      aria-label={label}
      title={label}
      disabled={disabled}
      {...props}
    >
      <Icon aria-hidden="true" size={17} />
    </button>
  );
}
