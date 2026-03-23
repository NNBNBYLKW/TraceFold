# Post-Phase-1 A3 Task 3 Failure Signals

## Classification Table

| Signal | Meaning | Typical surface | Recommended wording | Shortest recovery |
| --- | --- | --- | --- | --- |
| API unavailable | Shared API cannot be reached | Web, Desktop | `TraceFold API is unavailable.` | Check `/api/healthz`, then verify API base URL config |
| API invalid response | API responded, but the payload is not trustworthy | Web, Desktop | `TraceFold API returned an invalid response.` | Check API health, then inspect the API process or logs |
| API request failed | Request reached the API, but the request failed | Web | `Request failed with status ...` or API message | Check API health first, then inspect the requested route or filters |
| Desktop service unavailable | Desktop cannot reach the shared API | Desktop | `Service status: unavailable` | Check `/api/healthz`, then `TRACEFOLD_DESKTOP_API_BASE_URL` |
| Desktop invalid response | Desktop can reach the API but cannot trust the status payload | Desktop | `Service status: invalid response` | Check API health and API process |
| Workbench URL could not be opened | Desktop shell could not open the shared Web target | Desktop | `Workbench URL could not be opened.` | Check `TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL` and the Web dev server |
| Workbench URL not configured | Desktop shell config is incomplete | Desktop | `Workbench URL is not configured.` | Check `apps/desktop/.env` |
| Not generated | Derived content does not exist yet | Web | `AI derivation has not been generated ...` | Continue using the formal record; generate later if needed |
| Failed | A derivation or entry-side action failed | Web | `AI derivation failed ...` | Retry if appropriate; formal record remains available |
| Empty but healthy | Query succeeded but returned no items | Web | `No ... found.` | Adjust filters or continue elsewhere |
| Not configured | Required local config is missing or invalid | Desktop, startup path | explicit config error | Copy from `.env.example` and retry |

## Signal Principles

- `unavailable` is not the same thing as `empty`
- `invalid response` is not the same thing as `unavailable`
- `not generated` is not the same thing as `failed`
- Workbench URL failure is not the same thing as API health failure
- entry-side failures should not imply formal facts are corrupted
