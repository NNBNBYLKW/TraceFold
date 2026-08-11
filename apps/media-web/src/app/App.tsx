import { NavLink, Outlet } from "react-router-dom";


const destinations = [
  { to: "/inbox", label: "待入库", marker: "01" },
  { to: "/library", label: "文件库", marker: "02" },
  { to: "/settings", label: "设置", marker: "03" },
] as const;

export function App() {
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
        <div className="scan-indicator" aria-label="自动扫描状态">
          <span className="status-lamp" aria-hidden="true" />
          <span>自动扫描状态将在连接后显示</span>
        </div>
      </header>
      <main className="app-content">
        <Outlet />
      </main>
    </div>
  );
}
