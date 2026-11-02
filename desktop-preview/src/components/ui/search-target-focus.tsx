import { useEffect, useRef } from "react";
import { useLocation } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useThemeColor } from "@/contexts/theme-color-context";
import { searchIndex } from "@/config/search-index";

/** Locate only indexed UI. Never click a control or change a setting. */
export function SearchTargetFocus() {
  const location = useLocation();
  const { t } = useTranslation();
  const { getActiveColor } = useThemeColor();
  const color = useRef(getActiveColor());
  color.current = getActiveColor();
  useEffect(() => {
    const id = new URLSearchParams(location.search).get("focus");
    const item = searchIndex.find(entry => entry.id === id && entry.targetId);
    if (!item || item.path.split(/[?#]/, 1)[0] !== location.pathname) return;
    let restore: (() => void) | undefined;
    let located = false;
    const observer = new MutationObserver(locate);
    const normalize = (text: string) => text.normalize("NFKC").trim().replace(/\s+/g, " ");
    function locate() {
      if (located) return;
      const root = document.getElementById(item.targetId!);
      if (!root || !root.getClientRects().length) return;
      const labels = [item.nameKey, item.fallbackNameKey].filter(Boolean).map(key => normalize(String(t(key!))));
      const nodes = Array.from(root.querySelectorAll<HTMLElement>("label,p,button,h1,h2,h3,h4,h5,h6"));
      let label: HTMLElement | undefined;
      for (const text of labels) {
        label = nodes.find(node => normalize(node.textContent ?? "") === text
          || normalize(Array.from(node.childNodes).filter(child => child.nodeType === Node.TEXT_NODE).map(child => child.textContent).join("")) === text);
        if (label) break;
      }
      // Conditional controls may be hidden until their owning feature is
      // enabled. Reveal their existing group, without enabling it for the user.
      const target = label?.parentElement ?? root;
      located = true;
      observer.disconnect();
      const previousTabIndex = target.getAttribute("tabindex");
      target.setAttribute("tabindex", "-1");
      target.dataset.searchHighlight = item.id;
      target.scrollIntoView({ block: "center", behavior: "auto" });
      target.focus({ preventScroll: true });
      const animation = target.animate([
        { outline: "2px solid transparent", outlineOffset: "4px" },
        { outline: `2px solid ${color.current}`, outlineOffset: "4px", offset: .2 },
        { outline: "2px solid transparent", outlineOffset: "4px" },
      ], { duration: 1600 });
      restore = () => {
        animation.cancel();
        delete target.dataset.searchHighlight;
        if (previousTabIndex === null) target.removeAttribute("tabindex");
        else target.setAttribute("tabindex", previousTabIndex);
      };
    }
    observer.observe(document.querySelector("[data-page-content]") ?? document.body,
      { childList: true, subtree: true, attributes: true, attributeFilter: ["style", "hidden"] });
    const frame = requestAnimationFrame(locate);
    const timeout = window.setTimeout(() => observer.disconnect(), 5000);
    return () => { cancelAnimationFrame(frame); clearTimeout(timeout); observer.disconnect(); restore?.(); };
  }, [location.key, location.pathname, location.search, t]);
  return null;
}
