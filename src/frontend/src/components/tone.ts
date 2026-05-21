import type { Tone } from "../types";

export function toneToClass(tone: Tone) {
  return `tone-${tone}`;
}
