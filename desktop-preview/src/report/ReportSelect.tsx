import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useReportPresence } from "./report-motion";
import { Check, ChevronDown, Search, X } from "lucide-react";

export function ReportSelect({
  label,
  choices,
  value,
  disabled,
  onChange,
  searchPlaceholder = '搜索指标…',
}: {
  label: string;
  choices: string[][];
  value: string;
  disabled?: boolean;
  onChange: (value: string) => void;
  searchPlaceholder?: string;
}) {
  const trigger = useRef<HTMLButtonElement>(null);
  const [opened, setOpened] = useState(false);
  const { present, ref: menu } = useReportPresence<HTMLDivElement>(opened);
  const [position, setPosition] = useState<{
      left: number;
      top: number;
      width: number;
      height: number;
    }>(null),
    [query, setQuery] = useState("");
  const items = choices.filter(([, name]) =>
    name.toLocaleLowerCase().includes(query.toLocaleLowerCase()),
  );
  const close = (focus = false) => {
    setOpened(false);
    if (focus) trigger.current?.focus();
  };
  const open = () => {
    const r = trigger.current.getBoundingClientRect(),
      height = Math.min(360, innerHeight - 24),
      width = Math.min(Math.max(r.width, 290), innerWidth - 24);
    setQuery("");
    setOpened(true);
    setPosition({
      left: Math.max(12, Math.min(r.left, innerWidth - width - 12)),
      top:
        r.bottom + height + 8 < innerHeight
          ? r.bottom + 6
          : Math.max(12, r.top - height - 6),
      width,
      height,
    });
  };
  useEffect(() => {
    if (!opened || !present) return;
    const dismiss = (e: PointerEvent) => {
      if (
        !menu.current?.contains(e.target as Node) &&
        !trigger.current?.contains(e.target as Node)
      )
        close();
    };
    const resize = () => close(),
      scroll = (e: Event) => {
        if (!menu.current?.contains(e.target as Node)) close();
      };
    window.addEventListener("pointerdown", dismiss);
    window.addEventListener("resize", resize);
    window.addEventListener("scroll", scroll, true);
    menu.current
      ?.querySelector<HTMLInputElement>("input")
      ?.focus({ preventScroll: true });
    return () => {
      window.removeEventListener("pointerdown", dismiss);
      window.removeEventListener("resize", resize);
      window.removeEventListener("scroll", scroll, true);
    };
  }, [opened, present, menu]);
  return (
    <div className="report-select">
      <button
        ref={trigger}
        className="select-trigger"
        aria-label={label}
        aria-haspopup="listbox"
        aria-expanded={opened}
        disabled={disabled}
        onClick={() => (opened ? close() : open())}
        onKeyDown={(e) => {
          if (e.key === "ArrowDown" || e.key === "ArrowUp") {
            e.preventDefault();
            open();
          }
        }}
      >
        <span>{choices.find(([key]) => key === value)?.[1] || label}</span>
        <ChevronDown size={15} />
      </button>
      {present &&
        position &&
        createPortal(
          <div
            inert={!opened}
            style={{
              position: 'fixed', zIndex: 1000,
              left: position.left,
              top: position.top,
              width: position.width,
            }}
          ><div ref={menu} className="report-menu" data-motion-owned="" style={{position:'relative',maxHeight:position.height}}
            onKeyDown={(e) => {
              const buttons = Array.from(
                  menu.current.querySelectorAll<HTMLButtonElement>(
                    "[role=option]",
                  ),
                ),
                index = buttons.indexOf(
                  document.activeElement as HTMLButtonElement,
                );
              if (e.key === "Escape") {
                e.preventDefault();
                e.stopPropagation();
                close(true);
              } else if (e.key === "Tab") close();
              else if (
                ["ArrowDown", "ArrowUp", "Home", "End"].includes(e.key) &&
                !(
                  e.target instanceof HTMLInputElement &&
                  ["Home", "End"].includes(e.key)
                )
              ) {
                e.preventDefault();
                const next =
                  e.key === "Home"
                    ? 0
                    : e.key === "End"
                      ? buttons.length - 1
                      : (index +
                          (e.key === "ArrowDown" ? 1 : -1) +
                          buttons.length) %
                        buttons.length;
                buttons[next]?.focus();
              }
            }}
          >
            <div className="menu-search">
              <Search size={16} />
              <input
                aria-label={searchPlaceholder}
                placeholder={searchPlaceholder}
                value={query}
                onChange={(e) => setQuery(e.target.value)}
              />
              {query && (
                <button aria-label="清除搜索" onClick={() => setQuery("")}>
                  <X size={14} />
                </button>
              )}
            </div>
            <div role="listbox" aria-label={label} className="menu-options">
              {items.map(([key, name]) => (
                <button
                  key={key}
                  role="option"
                  aria-selected={key === value}
                  onClick={() => {
                    onChange(key);
                    close(true);
                  }}
                >
                  <span>{name}</span>
                  {key === value && <Check size={15} />}
                </button>
              ))}
              {!items.length && (
                <p className="empty-search" role="status">
                  没有匹配的选项
                </p>
              )}
            </div>
          </div></div>,
          document.body,
        )}
    </div>
  );
}
