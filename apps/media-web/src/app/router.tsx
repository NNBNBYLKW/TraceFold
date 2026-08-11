import { Navigate, createBrowserRouter } from "react-router-dom";

import { App } from "./App";
import { InboxPage } from "../inbox/InboxPage";
import { LibraryPage } from "../library/LibraryPage";
import { SettingsPage } from "../settings/SettingsPage";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <App />,
    children: [
      { index: true, element: <Navigate replace to="/inbox" /> },
      { path: "inbox", element: <InboxPage /> },
      { path: "library", element: <LibraryPage /> },
      { path: "settings", element: <SettingsPage /> },
    ],
  },
]);
