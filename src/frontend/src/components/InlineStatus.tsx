import { AlertCircle, CheckCircle2, Info, Loader2 } from "lucide-react";
import type { Tone } from "../types";
import { toneToClass } from "./tone";
import "./components.css";

interface InlineStatusProps {
  tone: Tone;
  title: string;
  message: string;
  busy?: boolean;
}

export function InlineStatus({ tone, title, message, busy }: InlineStatusProps) {
  const Icon = busy ? Loader2 : tone === "danger" ? AlertCircle : tone === "success" ? CheckCircle2 : Info;
  return (
    <div className={["inline-status", toneToClass(tone)].join(" ")}>
      <Icon aria-hidden="true" size={17} className={busy ? "inline-status__busy" : ""} />
      <div>
        <strong>{title}</strong>
        <span>{message}</span>
      </div>
    </div>
  );
}
