import type { ReactNode } from "react";
import type { Tone } from "../types";
import { toneToClass } from "./tone";
import "./components.css";

interface StatusPillProps {
  tone?: Tone;
  children: ReactNode;
}

export function StatusPill({ tone = "neutral", children }: StatusPillProps) {
  return <span className={["status-pill", toneToClass(tone)].join(" ")}>{children}</span>;
}
