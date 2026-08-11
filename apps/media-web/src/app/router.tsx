import { Navigate, createBrowserRouter } from "react-router-dom";

import { App } from "./App";
import { InboxPage } from "../inbox/InboxPage";


function PlaceholderPage({ title, index }: { title: string; index: string }) {
  return (
    <section className="placeholder-page" aria-labelledby={`page-${index}`}>
      <span className="section-index" aria-hidden="true">{index}</span>
      <div>
        <p className="eyebrow">TRACEFOLD / MEDIA</p>
        <h1 id={`page-${index}`}>{title}</h1>
        <p>界面模块正在接入本地媒体 API。</p>
      </div>
    </section>
  );
}

export const router = createBrowserRouter([
  {
    path: "/",
    element: <App />,
    children: [
      { index: true, element: <Navigate replace to="/inbox" /> },
      { path: "inbox", element: <InboxPage /> },
      { path: "library", element: <PlaceholderPage title="文件库" index="02" /> },
      { path: "settings", element: <PlaceholderPage title="设置" index="03" /> },
    ],
  },
]);
