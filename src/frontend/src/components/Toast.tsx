import { Info } from "lucide-react";
import "./components.css";

interface ToastProps {
  title: string;
  detail: string;
}

export function Toast({ title, detail }: ToastProps) {
  return (
    <aside className="toast-preview" aria-label="静态状态提示">
      <Info aria-hidden="true" size={17} />
      <div>
        <strong>{title}</strong>
        <span>{detail}</span>
      </div>
    </aside>
  );
}
