# Post-Phase-1 A3 Task 2 Recovery Paths

## API Is Not Started

Signal:

- `http://127.0.0.1:8000/api/healthz` does not respond
- Web shows service-unavailable style failures
- Desktop shows `Service status: unavailable`

Shortest recovery path:

1. Start the API with the recommended command
2. Recheck `http://127.0.0.1:8000/api/healthz`
3. Refresh Web or restart Desktop only after API health is back

## API Base URL Is Wrong

Signal:

- Web starts, but requests fail immediately
- Desktop starts, but always reports `Service status: unavailable`

Shortest recovery path:

1. Check `apps/web/.env` and `apps/desktop/.env`
2. Confirm both point at the intended shared API address
3. Restart Web or Desktop after correcting the config

## Web Starts But Backend Is Unreachable

Signal:

- `npm run dev` works
- Workbench renders, but shared data requests fail

Shortest recovery path:

1. Check API health first
2. Confirm `VITE_API_BASE_URL`
3. Restart Web after fixing API reachability or config

## Desktop Starts But Cannot Open Workbench

Signal:

- Desktop shell starts
- startup output shows `Workbench state: error`
- Desktop reports `Workbench URL could not be opened.` or URL config issues

Shortest recovery path:

1. Check `TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL`
2. Confirm the Web dev server is reachable on that URL
3. Restart Desktop after fixing the Web URL or Web startup

## Desktop Shows Service Unavailable

Signal:

- startup output shows `Service status: unavailable`
- Desktop notification says service unavailable

Shortest recovery path:

1. Check API health
2. If API is healthy, check `TRACEFOLD_DESKTOP_API_BASE_URL`
3. Restart Desktop after fixing the API or config

Important distinction:

- Workbench URL failure and API status failure are related but not identical

## `.env` Is Missing Or Wrong

Signal:

- startup raises validation or URL errors
- Web or Desktop points to the wrong local service

Shortest recovery path:

1. Copy from the relevant `.env.example`
2. Fill only the minimum local values first
3. Retry the formal startup command for that entry

## Acceptance Runner Passes Or Fails

What it means:

- passing means the checked validation stages passed
- failing means at least one validation stage failed

What it does not mean:

- it is not the daily startup path
- it does not replace running API, Web, and Desktop normally

Shortest recovery path:

1. Read the stage name in the runner output
2. Fix that stage as a validation issue
3. Do not treat runner success as a substitute for normal day-to-day startup
