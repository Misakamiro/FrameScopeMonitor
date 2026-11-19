import { Box, HStack, Text, Badge } from "@chakra-ui/react";
import { LuChevronDown, LuCheck } from "react-icons/lu";
import { useState, useRef, useEffect, useLayoutEffect, useId } from "react";
import { createPortal } from "react-dom";
import { LiquidGlassCard } from "./liquid-glass-card";
import { useThemeColor } from "@/contexts/theme-color-context";
import { useGlassTextColor } from "@/hooks/use-adaptive-text-color";
import { useSurfacePresence } from '../ui/surface-motion';

interface SelectOption {
  value: string;
  label: string;
  description?: string;
  badge?: string;
  badgeColorScheme?: string;
  disabled?: boolean;
}
interface CustomSelectProps {
  value: string;
  onChange: (value: string) => void;
  options: SelectOption[];
  width?: string;
  placeholder?: string;
  direction?: "up" | "down";
  ariaLabel?: string;
  id?: string;
  disabled?: boolean;
  solidMenu?: boolean;
}

export function CustomSelect({ value, onChange, options, width = "140px", placeholder,
  direction = "down", ariaLabel, id, disabled = false, solidMenu = false }: CustomSelectProps) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const [opensUp, setOpensUp] = useState(direction === 'up');
  const presence = useSurfacePresence<HTMLDivElement>(open, 'menu', opensUp ? -1 : 1);
  const [position, setPosition] = useState<{ top: number; left: number; width: number; maxHeight: number } | null>(null);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLDivElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const listId = useId();
  const textColor = useGlassTextColor("gray.700", "#ffffff");
  const { getActiveColor, getHoverColor, getBorderColor } = useThemeColor();
  const selected = options.find(option => option.value === value);
  const close = (restore = false) => { setOpen(false); if (restore) trigger.current?.focus(); };
  const choose = (index: number) => { if (!options[index] || options[index].disabled) return; onChange(options[index].value); close(true); };

  useLayoutEffect(() => {
    if (!open) return;
    const place = () => {
      const rect = trigger.current?.getBoundingClientRect();
      if (!rect) return;
      const below = window.innerHeight - rect.bottom - 12, above = rect.top - 12;
      const up = direction === "up" ? above >= 140 || above >= below : below < 140 && above > below;
      setOpensUp(up);
      const maxHeight = Math.max(40, Math.min(280, up ? above : below));
      const node = menu.current;
      const actualHeight = Math.min(maxHeight, node?.firstElementChild?.scrollHeight || maxHeight);
      const menuWidth = Math.min(rect.width, window.innerWidth - 16);
      setPosition({ top: up ? rect.top - actualHeight - 4 : rect.bottom + 4,
        left: Math.max(8, Math.min(rect.left, window.innerWidth - menuWidth - 8)),
        width: menuWidth, maxHeight });
    };
    place();
    const frame = requestAnimationFrame(place);
    window.addEventListener("resize", place);
    window.addEventListener("scroll", place, true);
    return () => { cancelAnimationFrame(frame); window.removeEventListener("resize", place); window.removeEventListener("scroll", place, true); };
  }, [open, direction, options.length]);
  useEffect(() => {
    if (!open) return;
    const outside = (event: MouseEvent) => {
      const target = event.target as Node;
      if (!root.current?.contains(target) && !menu.current?.contains(target)) setOpen(false);
    };
    document.addEventListener("mousedown", outside);
    return () => document.removeEventListener("mousedown", outside);
  }, [open]);
  useEffect(() => {
    if (open) menu.current?.querySelector<HTMLElement>('[data-index="' + active + '"]')?.scrollIntoView({ block: "nearest" });
  }, [open, active]);
  useEffect(() => { if (disabled) setOpen(false); }, [disabled]);
  const show = () => { setActive(Math.max(0, options.findIndex(option => option.value === value))); setOpen(true); };

  return <Box ref={root} w={width} minW={0}>
    <LiquidGlassCard ref={trigger} id={id} px={3} py={1.5} role="combobox" tabIndex={disabled ? -1 : 0}
      aria-label={ariaLabel} aria-expanded={open} aria-controls={open ? listId : undefined} aria-haspopup="listbox"
      aria-activedescendant={open ? listId + '-' + active : undefined} aria-disabled={disabled}
      cursor={disabled ? "not-allowed" : "pointer"} opacity={disabled ? 0.55 : 1}
      onClick={() => { if (!disabled) { if (open) close(); else show(); } }}
      onKeyDown={event => {
        if (disabled) return;
        if (event.key === "Escape" && open) { event.preventDefault(); event.stopPropagation(); close(true); }
        else if (event.key === "Tab") close();
        else if (["ArrowDown", "ArrowUp", "Home", "End", "Enter", " "].includes(event.key)) {
          event.preventDefault();
          if (!open) { show(); return; }
          if (event.key === "Enter" || event.key === " ") choose(active);
          else setActive(index => {
            const enabled=options.map((o,i)=>o.disabled?-1:i).filter(i=>i>=0);
            if(event.key==='Home')return enabled[0]??0;if(event.key==='End')return enabled.at(-1)??0;
            return event.key==='ArrowDown'?(enabled.find(i=>i>index)??index):([...enabled].reverse().find(i=>i<index)??index);
          });
        }
      }}>
      <HStack justify="space-between"><Text fontSize="sm" color={textColor} noOfLines={1} minW={0} title={selected?.label}>
        {selected?.label || placeholder || value}</Text><LuChevronDown size={14} color={textColor}
          style={{ flexShrink: 0, transform: open ? "rotate(180deg)" : undefined }} /></HStack>
    </LiquidGlassCard>
    {presence.present && position && createPortal(<Box ref={menu} position="fixed" top={position.top} left={position.left} width={position.width}
      zIndex={99999} pointerEvents={open?'auto':'none'} inert={!open}>
      <Box ref={presence.ref} id={listId} role="listbox" aria-label={ariaLabel} data-motion-owned=""
      data-glass-surface="panel" maxH={position.maxHeight} overflowY="auto" overflowX="hidden" bg={solidMenu ? 'var(--framescope-popup-bg)' : 'var(--framescope-panel-bg)'}
      backdropFilter="var(--framescope-surface-filter)" border="1px solid" borderColor={getBorderColor()}
      borderRadius="lg" boxShadow="2xl" zIndex={99999} onMouseDown={event => event.preventDefault()}>
      {options.map((option, index) => <Box key={option.value} id={listId + '-' + index} data-index={index}
        role="option" aria-selected={option.value === value} aria-disabled={option.disabled||undefined} px={3} py={2} cursor={option.disabled?'not-allowed':'pointer'} opacity={option.disabled?.5:1}
        bg={index === active ? getHoverColor() : "transparent"} color={solidMenu ? 'var(--framescope-control-fg)' : option.value === value ? getActiveColor() : "var(--framescope-surface-fg)"}
        onMouseMove={() => {if(!option.disabled)setActive(index);}} onClick={() => choose(index)}>
        <HStack justify="space-between" minW={0}><Text fontSize="sm" overflowWrap="anywhere">{option.label}</Text>
          {option.badge && <Badge colorScheme={option.badgeColorScheme ?? "purple"}>{option.badge}</Badge>}
          {option.value === value && <LuCheck size={14} style={{ flexShrink: 0 }} />}</HStack>
        {option.description && <Text fontSize="xs" color="var(--framescope-surface-muted)" overflowWrap="anywhere">{option.description}</Text>}
      </Box>)}
      </Box>
    </Box>, root.current?.closest('.chakra-modal__content-container') || document.body)}
  </Box>;
}
