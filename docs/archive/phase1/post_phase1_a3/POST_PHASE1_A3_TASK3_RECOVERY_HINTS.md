# Post-Phase-1 A3 Task 3 Recovery Hints

## Web

- If the UI says `TraceFold API is unavailable`, check `/api/healthz` first and then `VITE_API_BASE_URL`.
- If the UI says `TraceFold API returned an invalid response`, the API process is reachable but not returning a trustworthy payload. Check the API process or logs.
- If AI derivation is `not generated`, the formal record is still available. This is not a data-loss signal.
- If AI derivation `failed`, the formal record is still available. Retry derivation only if needed.

## Desktop

- `Service status: unavailable` means Desktop cannot reach the shared API. Check `/api/healthz` and `TRACEFOLD_DESKTOP_API_BASE_URL`.
- `Service status: invalid response` means the API answered but Desktop could not trust the payload. Check the API process.
- `Workbench URL could not be opened` means the shell could not open the shared Web target. Check `TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL` and confirm the Web dev server is running.
- These shell-side failures do not imply formal records are damaged.

## General Rule

When a failure is entry-side:

- first decide whether it is an API reachability problem, a Web target problem, or a config problem
- do not assume formal facts are damaged unless the failure explicitly says a successful write did not happen
- use the shortest shared checks first:
  - `/api/healthz`
  - relevant `.env`
  - Web dev server reachability
