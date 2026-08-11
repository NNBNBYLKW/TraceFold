import { NavLink, Outlet } from "react-router-dom";

import { useMediaEvents } from "../shared/events/useMediaEvents";


const destinations = [
  { to: "/inbox", label: "待入库", marker: "01" },
  { to: "/library", label: "文件库", marker: "02" },
  { to: "/settings", label: "设置", marker: "03" },
] as const;

export function App() {
  const eventConnection = useMediaEvents();
  const eventStatus = {
    connecting: "正在连接实时更新",
    connected: "实时更新已连接",
    disconnected: "实时更新已断开，可继续手动刷新",
  }[eventConnection];
  return (
    <div className="app-shell">
      <header className="topbar">
        <NavLink className="brand" to="/inbox" aria-label="TraceFold 媒体库首页">
          <span className="brand-mark" aria-hidden="true">TF</span>
          <span>
            <strong>TRACEFOLD</strong>
            <small>本地媒体台账</small>
          </span>
        </NavLink>
        <nav className="product-nav" aria-label="产品导航">
          {destinations.map((destination) => (
            <NavLink
              key={destination.to}
              to={destination.to}
              className={({ isActive }) => isActive ? "nav-link is-active" : "nav-link"}
            >
              <span aria-hidden="true">{destination.marker}</span>
              {destination.label}
            </NavLink>
          ))}
        </nav>
        <div className="scan-indicator" aria-label="实时更新状态">
          <span className={`status-lamp is-${eventConnection}`} aria-hidden="true" />
          <span>{eventStatus}</span>
        </div>
      </header>
      <main className="app-content">
        <Outlet />
      </main>
    </div>
  );
}
