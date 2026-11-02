import { extendTheme, type ThemeConfig } from "@chakra-ui/react";

export const LS_THEME_MODE = "nexbox-theme-mode";
export type ThemeMode = "light" | "dark" | "system";

/** 同步读取持久化的主题模式：light/dark/system，无记录时默认跟随系统 */
export function readInitialThemeMode(): ThemeMode {
  try {
    const m = localStorage.getItem(LS_THEME_MODE);
    if (m === "light" || m === "dark" || m === "system") return m;
    // 迁移：老版本只有 chakra-ui-color-mode，尊重既有显式选择
    const legacy = localStorage.getItem("chakra-ui-color-mode");
    if (legacy === "light" || legacy === "dark") return legacy;
  } catch {
    // localStorage 不可用时返回默认跟随系统
  }
  return "system";
}

/** 系统是否偏好深色 */
export function systemPrefersDark(): boolean {
  try {
    return window.matchMedia("(prefers-color-scheme: dark)").matches;
  } catch {
    return false;
  }
}

/** 根据持久化主题模式的解析浅/深，用作首帧初始 colorMode、避免闪烁 */
export function readInitialScheme(): "light" | "dark" {
  const m = readInitialThemeMode();
  if (m === "light") return "light";
  if (m === "dark") return "dark";
  return systemPrefersDark() ? "dark" : "light";
}

const config: ThemeConfig = {
  initialColorMode: readInitialScheme(),
  useSystemColorMode: false,
};

const controlSurface={bg:'var(--framescope-control-bg)',color:'var(--framescope-control-fg)',border:'1px solid',borderColor:'var(--framescope-control-border)',backdropFilter:'var(--framescope-surface-filter)',_placeholder:{color:'var(--framescope-surface-muted)',opacity:1},_hover:{bg:'var(--framescope-control-bg)'},_disabled:{opacity:.45,cursor:'not-allowed'}};
const panelSurface={bg:'var(--framescope-panel-bg)',color:'var(--framescope-surface-fg)',border:'1px solid',borderColor:'var(--framescope-control-border)',backdropFilter:'var(--framescope-surface-filter)',boxShadow:'0 18px 60px rgba(0,0,0,.18)'};

const theme = extendTheme({
  config,
  fonts: {
    heading: 'var(--app-font-family, "Sinter-Regular"), Inter, system-ui, sans-serif',
    body: 'var(--app-font-family, "Sinter-Regular"), Inter, system-ui, sans-serif',
  },
  colors: {
    brand: {
      50: "#eef2ff",
      100: "#e0e7ff",
      200: "#c7d2fe",
      300: "#a5b4fc",
      400: "#818cf8",
      500: "#6366f1",
      600: "#4f46e5",
      700: "#4338ca",
      800: "#3730a3",
      900: "#312e81",
    },
  },
  styles: {
    global: (props: any) => ({
      body: {
        bg: props.colorMode === 'dark' ? '#000000' : '#f7fafc',
        color: 'var(--framescope-surface-fg)',
      },
    }),
  },
  components: {
    Text: {baseStyle:{color:'var(--framescope-surface-fg)'}},
    Heading: {baseStyle:{color:'var(--framescope-surface-fg)'}},
    FormLabel: {baseStyle:{color:'var(--framescope-surface-fg)'}},
    Button: {
      baseStyle: {
        borderRadius: "lg",
        fontWeight: "medium",
        _focusVisible:{outline:'2px solid var(--framescope-primary)',outlineOffset:'2px',boxShadow:'none'},
      },
      variants:{
        solid:(props:{colorScheme?:string})=>props.colorScheme==='red'?{...controlSurface,bg:'red.600',color:'white',borderColor:'red.600',_hover:{bg:'red.700'}}:props.colorScheme&&props.colorScheme!=='gray'?{...controlSurface,bg:'var(--framescope-primary)',color:'var(--framescope-primary-fg)',borderColor:'var(--framescope-primary)',_hover:{bg:'var(--framescope-primary-hover)'}}:controlSurface,
        primary:{...controlSurface,bg:'var(--framescope-primary)',color:'var(--framescope-primary-fg)',borderColor:'var(--framescope-primary)',_hover:{bg:'var(--framescope-primary-hover)'}},
        outline:(props:{colorScheme?:string})=>({...controlSurface,bg:'transparent',...(props.colorScheme&&props.colorScheme!=='gray'?{
          color:props.colorScheme==='red'?'red.500':'var(--framescope-primary)',
          borderColor:props.colorScheme==='red'?'red.500':'var(--framescope-primary)',
        }:{})}),
        ghost:(props:{colorScheme?:string})=>({...controlSurface,bg:'transparent',borderColor:'transparent',...(props.colorScheme==='red'?{color:'red.500'}:{})}),
      },
    },
    Input:{variants:{outline:{field:{...controlSurface,_focusVisible:{borderColor:'var(--framescope-primary)',boxShadow:'0 0 0 1px var(--framescope-primary)'}}}}},
    NumberInput:{variants:{outline:{field:{...controlSurface,_focusVisible:{borderColor:'var(--framescope-primary)',boxShadow:'0 0 0 1px var(--framescope-primary)'}}}}},
    Textarea:{variants:{outline:{...controlSurface,_focusVisible:{borderColor:'var(--framescope-primary)',boxShadow:'0 0 0 1px var(--framescope-primary)'}}}},
    Select:{variants:{outline:{field:{...controlSurface,'& option':{bg:'var(--framescope-panel-bg)',color:'var(--framescope-surface-fg)'}}}}},
    Modal:{baseStyle:{overlay:{bg:'rgba(0,0,0,.18)'},dialog:{...panelSurface,borderRadius:'xl'},closeButton:{color:'var(--framescope-control-fg)',borderRadius:'full',_hover:{bg:'var(--framescope-control-hover)'}}}},
    Drawer:{baseStyle:{overlay:{bg:'rgba(0,0,0,.18)'},dialog:{...panelSurface,borderStartRadius:'xl'},closeButton:{color:'var(--framescope-control-fg)',borderRadius:'full',_hover:{bg:'var(--framescope-control-hover)'}}}},
    Menu:{baseStyle:{list:{...panelSurface,borderRadius:'xl'},item:{bg:'transparent',_focus:{bg:'var(--framescope-control-hover)'}}}},
    Popover:{baseStyle:{content:{...panelSurface,borderRadius:'xl'}}},
    Card: {
      baseStyle: {
        container: {
          borderRadius: "xl",
          bg: undefined,
          borderColor: undefined,
          borderWidth: undefined,
        },
      },
    },
    Tooltip: {
      baseStyle: {
        // 由 BackgroundProvider 通过 CSS 变量驱动：跟随液态玻璃开关与深浅主题
        bg: "var(--framescope-panel-bg)",
        color: "var(--framescope-surface-fg)",
        // 覆盖默认 css var，使箭头颜色与胶囊背景一致
        "--tooltip-bg": "var(--framescope-panel-bg)",
        "--tooltip-fg": "var(--framescope-surface-fg)",
        "--popper-arrow-bg": "var(--framescope-panel-bg)",
        px: "8px",
        py: "2px",
        borderRadius: "full", // 胶囊全圆角
        fontWeight: "medium",
        fontSize: "xs",
        backdropFilter: "var(--framescope-surface-filter)",
        WebkitBackdropFilter: "var(--framescope-surface-filter)",
        border: "1px solid",
        borderColor: "var(--framescope-control-border)",
        maxW: "xs",
        zIndex: "tooltip",
      } as any,
      // 全局兜底：点一下提示框即关闭、关闭零延迟，避免部分情况下悬浮提示常驻不消失
      defaultProps: {
        closeOnMouseDown: true,
        closeDelay: 0,
        openDelay: 150,
      } as any,
    },
  },
});

export default theme;
