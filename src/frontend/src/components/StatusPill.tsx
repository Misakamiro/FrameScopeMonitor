import type { ReactNode } from "react";
import type { Tone } from "../types";
import { toneToClass } from "./tone";
import "./components.css";

interface StatusPillProps {
  tone?: Tone;
  className?: string;
  children: ReactNode;
}

export function StatusPill({ tone = "neutral", className, children }: StatusPillProps) {
  return <span className={["status-pill", toneToClass(tone), className].filter(Boolean).join(" ")}>{children}</span>;
}
