import type { LucideIcon } from "lucide-react";
import "./components.css";

interface EmptyStateProps {
  icon: LucideIcon;
  title: string;
  description: string;
  actionLabel?: string;
  onAction?: () => void;
}

export function EmptyState({ icon: Icon, title, description, actionLabel, onAction }: EmptyStateProps) {
  return (
    <div className="empty-state">
      <div className="empty-state__icon">
        <Icon aria-hidden="true" size={22} />
      </div>
      <div>
        <h3>{title}</h3>
        <p>{description}</p>
      </div>
      {actionLabel ? (
        onAction ? (
          <button className="empty-state__action" type="button" onClick={onAction}>
            {actionLabel}
          </button>
        ) : (
          <span className="empty-state__note">{actionLabel}</span>
        )
      ) : null}
    </div>
  );
}
