import type { LucideIcon } from "lucide-react";
import "./components.css";

interface EmptyStateProps {
  icon: LucideIcon;
  title: string;
  description: string;
  actionLabel?: string;
}

export function EmptyState({ icon: Icon, title, description, actionLabel }: EmptyStateProps) {
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
        <button className="empty-state__action" type="button" disabled>
          {actionLabel}
        </button>
      ) : null}
    </div>
  );
}
