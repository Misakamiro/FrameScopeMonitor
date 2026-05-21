import { motion } from "framer-motion";
import { Activity, ShieldCheck } from "lucide-react";
import { navigationItems } from "../data/mockPreview";
import { motionTokens } from "../theme/motion";
import type { BridgeEnvironment } from "../bridge/contract";
import type { AppPage } from "../types";
import "./layout.css";

interface SidebarNavProps {
  activePage: AppPage;
  bridgeEnvironment: BridgeEnvironment;
  onNavigate: (page: AppPage) => void;
}

export function SidebarNav({ activePage, bridgeEnvironment, onNavigate }: SidebarNavProps) {
  return (
    <aside className="sidebar" aria-label="FrameScope navigation">
      <div className="brand-lockup">
        <div className="brand-lockup__mark">
          <Activity aria-hidden="true" size={22} />
        </div>
        <div>
          <strong>FrameScope</strong>
          <span>Monitor Web UI</span>
        </div>
      </div>
      <nav className="sidebar__nav">
        {navigationItems.map((item) => {
          const Icon = item.icon;
          const active = activePage === item.id;
          return (
            <motion.button
              key={item.id}
              type="button"
              data-smoke-nav={item.id}
              className={["nav-item", active ? "nav-item--active" : ""].join(" ")}
              onClick={() => onNavigate(item.id)}
              layout
              whileHover={{ x: 2 }}
              whileTap={{ scale: 0.985 }}
              transition={motionTokens.springPress}
            >
              {active ? <motion.span className="nav-item__active-bg" layoutId="activeNav" /> : null}
              <Icon aria-hidden="true" size={18} />
              <span>
                <strong>{item.label}</strong>
                <small>{item.description}</small>
              </span>
            </motion.button>
          );
        })}
      </nav>
      <div className="sidebar__status">
        <ShieldCheck aria-hidden="true" size={18} />
        <div>
          <strong>{bridgeEnvironment === "webview2" ? "本机功能可用" : "界面预览中"}</strong>
          <span>{bridgeEnvironment === "webview2" ? "操作会交给宿主应用执行" : "不会读取真实系统数据"}</span>
        </div>
      </div>
    </aside>
  );
}
