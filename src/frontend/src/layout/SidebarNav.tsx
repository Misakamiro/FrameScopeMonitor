import { Activity, ShieldCheck } from "lucide-react";
import { navigationItems } from "../data/mockPreview";
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
    <aside className="sidebar" aria-label="FrameScope 导航">
      <div className="brand-lockup">
        <div className="brand-lockup__mark">
          <Activity aria-hidden="true" size={22} />
        </div>
        <div>
          <strong>FrameScope</strong>
          <span>性能监控</span>
        </div>
      </div>
      <nav className="sidebar__nav">
        {navigationItems.map((item) => {
          const Icon = item.icon;
          const active = activePage === item.id;
          return (
            <button
              key={item.id}
              type="button"
              data-smoke-nav={item.id}
              className={["nav-item", active ? "nav-item--active" : ""].join(" ")}
              data-compact-label={item.label}
              title={item.label}
              aria-label={`${item.label}：${item.description}`}
              onClick={() => onNavigate(item.id)}
            >
              <span className="nav-item__rail" aria-hidden="true" />
              <Icon aria-hidden="true" size={18} />
              <span>
                <strong>{item.label}</strong>
                <small>{item.description}</small>
              </span>
            </button>
          );
        })}
      </nav>
      <div className="sidebar__status">
        <span className="sidebar__status-dot" aria-hidden="true" />
        <ShieldCheck aria-hidden="true" size={18} />
        <div>
          <strong>{bridgeEnvironment === "webview2" ? "本机功能可用" : "界面预览中"}</strong>
          <span>{bridgeEnvironment === "webview2" ? "操作由本机程序执行" : "不会读取真实系统数据"}</span>
        </div>
      </div>
    </aside>
  );
}
