export const DEFAULT_NAV_ORDER = ["/", "/hardware", "/targets", "/reports", "/overlay-panel", "/settings"];

export function normalizeNavOrder(value: unknown): string[] {
  let parsed: unknown = value;
  if (typeof value === "string") {
    try {
      parsed = JSON.parse(value);
    } catch {
      return [...DEFAULT_NAV_ORDER];
    }
  }
  const saved = Array.isArray(parsed)
    ? parsed.filter((path): path is string => typeof path === "string" && DEFAULT_NAV_ORDER.includes(path))
    : [];
  return [...new Set([...saved, ...DEFAULT_NAV_ORDER])];
}
