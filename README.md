# Smart-X Gateway - Parts 1 & 2

A hybrid IoT gateway API and dashboard for the Smart-X ecosystem (hydroponic
farms, utility trackers and smart grid installations).

- **Part 1 - Sensor Data Ingestion & Telemetry:** device registration, file
  attachments, telemetry ingestion, the live pulse feed and grid heatmap, and
  recursive deployment-tree validation.
- **Part 2 - Real-Time Command Stream & History:** a live device dashboard fed
  by Server-Sent Events, manual override commands with undo, priority alert
  processing, a chronological history log, search and filtering, and a
  recommendation engine ("Suggested Actions").
- **Final PoE - Network Topology & Mesh Routing:** **not implemented yet.** It
  has a navigation entry and a placeholder page only.

> **Simulation notice.** No physical hardware is connected. A built-in device
> simulator in the API plays the part of nine ESP32-based devices (moisture
> sensors, pumps, smart plugs, a relay, a power meter and a controller). Every
> simulated device is labelled as simulated in the UI. Simulated packets go
> through exactly the same validation, queues and processing as packets posted
> to the API from outside.

## Contents

1. [Architecture](#architecture)
2. [Requirements and setup](#requirements-and-setup)
3. [Running the application](#running-the-application)
4. [Configuration](#configuration)
5. [Using the application](#using-the-application)
6. [Part 2 data structures - where and why](#part-2-data-structures---where-and-why)
7. [Real-time updates (Server-Sent Events)](#real-time-updates-server-sent-events)
8. [Recommendation engine](#recommendation-engine)
9. [Testing each feature by hand](#testing-each-feature-by-hand)
10. [API reference](#api-reference)
11. [Automated tests](#automated-tests)
12. [Limitations and assumptions](#limitations-and-assumptions)
13. [Part 1 C# concepts](#part-1-c-concepts)
14. [References](#references)

## Architecture

**Web-first**: an ASP.NET Core Minimal API backend and a Blazor WebAssembly
frontend that share one class library, all on .NET 10. The browser talks to the
API over normal HTTP requests (reads, commands, searches). The API pushes live
updates to the browser over one long-lived Server-Sent Events (SSE) connection.

```
Smart-X/
├── SmartX.slnx                      solution file (API, client, shared library, tests)
├── docker-compose.yml
├── src/
│   ├── SmartX.Shared/               class library used by both API and client
│   │   ├── Enums/                   SensorCategory, DeviceKind, DeviceStatus, CommandAction, ...
│   │   ├── Generics/                TelemetryPacket<T>                         (Part 1)
│   │   ├── Aggregation/             MeterReading (overloaded operators)        (Part 1)
│   │   ├── Models/                  SensorRegistration, DeploymentNode, ...    (Part 1)
│   │   ├── Validation/              DeploymentTreeValidator (recursive)        (Part 1)
│   │   ├── Search/                  DeviceQuery - search/filter rules          (Part 2)
│   │   └── Dto/                     request/response and live-update contracts
│   ├── SmartX.Api/                  ASP.NET Core Minimal API
│   │   ├── Endpoints/               one file per route group
│   │   ├── Services/                Part 1 in-memory repositories and telemetry store
│   │   ├── RealTime/                Part 2: registry, queues, history, errors, commands,
│   │   │                            simulator, SSE broadcaster, recommendation engine
│   │   ├── SmartX.Api.http          ready-made example requests
│   │   └── appsettings.json         Part 2 thresholds and timings ("RealTime" section)
│   └── SmartX.Client/               Blazor WebAssembly dashboard
│       ├── Layout/                  sidebar navigation (three pillars) + live indicator
│       ├── Pages/                   Part 1 pages, Part 2 Stream* pages, Topology placeholder
│       ├── Components/              DeviceCard, SuggestedActionsPanel, ApiErrorBanner, ...
│       └── Services/                SmartXApiClient (HTTP) and LiveDashboardState (SSE)
└── tests/
    └── SmartX.Tests/                xUnit tests for the Part 2 logic
```

### How a telemetry packet flows (Part 2)

```
device / simulator ──POST /api/stream/telemetry──▶ TelemetryProcessor.Enqueue
    validate (MAC format, Dictionary lookup, sensor type, value range)
    classify severity ──▶ Info: Queue<T>      Warning/Critical: PriorityQueue
                                    │
ProcessingWorker (every 300 ms) ───▶ dequeue: priority first (with starvation guard)
    update device state ─ add to HistoryLog (SortedDictionary)
    raise/clear active errors (HashSet) ─ forward to the Part 1 pulse feed and heatmap
    publish to LiveUpdateBroadcaster ──SSE /api/stream/live──▶ dashboard
```

All data is held **in memory** (no database), as in Part 1. Restarting the API
resets devices, history, commands and searches.

## Requirements and setup

| Software | Version | Needed for |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | **10.0** (`dotnet --version` should show 10.x) | building, running and testing |
| A modern browser | Chrome, Edge or Firefox | the dashboard (needs streaming `fetch` support for SSE) |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | optional | running both parts in containers |

Dependencies are NuGet packages that restore automatically. No environment
variables are needed for a local run.

```bash
git clone https://github.com/St10443269/programming7312_part-1.git
cd programming7312_part-1
dotnet restore SmartX.slnx
dotnet build SmartX.slnx
```

## Running the application

The API and the dashboard are two separate processes. Start both.

**Terminal 1 - API** (http://localhost:5190):

```bash
dotnet run --project src/SmartX.Api --urls http://localhost:5190
```

**Terminal 2 - dashboard** (http://localhost:5122):

```bash
dotnet run --project src/SmartX.Client --urls http://localhost:5122
```

Open **http://localhost:5122**. The sidebar should show
**"Live stream connected"** within a second or two, and the Live Dashboard
should start showing readings. If the API is not running, the pages show an
"API problem" banner and the sidebar shows "Reconnecting...". The client keeps
retrying and recovers by itself when the API comes back.

In **Visual Studio**, you can instead right-click the solution, choose
*Configure Startup Projects* -> *Multiple startup projects*, and set both
`SmartX.Api` and `SmartX.Client` to *Start*. Their launch profiles already use
ports 5190 and 5122.

### Running with Docker

Start Docker Desktop first, then:

```bash
docker compose up --build
```

- API: http://localhost:5190
- Dashboard: http://localhost:8080

The client is a static Blazor WebAssembly build served by nginx. The API runs
in its own container. The browser calls the API (and its SSE stream) directly
on port 5190, and `ClientOrigins__0` in `docker-compose.yml` allows the
dashboard's origin through CORS. Stop with `Ctrl+C`, then `docker compose down`.

## Configuration

| Setting | File | Purpose |
|---|---|---|
| `ApiBaseUrl` | `src/SmartX.Client/wwwroot/appsettings.json` | Where the dashboard finds the API (default `http://localhost:5190`). |
| `ClientOrigins` | API `appsettings.json` or env var `ClientOrigins__0` | Browser origins allowed by CORS (defaults to `http://localhost:5122` and `https://localhost:7163`). |
| `RealTime:Thresholds` | `src/SmartX.Api/appsettings.json` | Alert thresholds: moisture critical < 20 %, warning < 30 %; power spike > 1500 W, warning > 1000 W; controller overheat > 40 °C, warning > 35 °C; disconnected after 8 s of silence. |
| `RealTime:Processing` | same | Worker interval (300 ms), batch size (50), starvation guard `MaxConsecutivePriority` (5), history cap (5000 entries). |
| `RealTime:Simulator` | same | Start on launch, tick interval (1000 ms), random dropout chance. |
| `RealTime:Recommendations` | same | Pattern window (5 min), minimum occurrences (2), minimum confidence (0.5), minimum history (3), recurring-fault threshold (3), lookback (60 min). |

Any of these can also be overridden with environment variables, e.g.
`RealTime__Thresholds__MoistureCriticalPct=15`.

## Using the application

The sidebar groups the pages by pillar. Navigation is client-side routing, so
moving between pages never reloads the app. The live SSE connection, the
latest device states, your search filters and the selected history device all
carry over between pages.

| Pillar | Page | What it does |
|---|---|---|
| 1 | **Telemetry** | Part 1 pulse feed, heatmap and gauge. Now also shows the live simulated readings. |
| 1 | **Sensors** | Register a device and attach files. A newly registered device also joins the Part 2 network. |
| 1 | **Deployment Tree** | Recursive deployment-tree validation. |
| 2 | **Live Dashboard** (`/stream`) | Device cards with live readings, status, alerts and ON/OFF buttons; search and filters; active errors; live event stream; top suggestions; simulator on/off. |
| 2 | **Commands & Undo** (`/stream/commands`) | Send commands, see the undo stack, undo, simulate an outage, full command history. |
| 2 | **Queues & Alerts** (`/stream/processing`) | Queue depths, pause/resume, the priority demo burst, the processing order, active errors with duplicate counts, send your own packets. |
| 2 | **History Log** (`/stream/history`) | The chronological log with a chart, filters and two ordering demos. |
| 2 | **Suggested Actions** (`/stream/insights`) | All recommendations with their reasons, how they are generated, and recent searches. |
| 3 | **Topology** (`/topology`) | Placeholder for the final PoE. States clearly that mesh routing is not implemented. |

### Simulated devices

| Device | MAC | Kind | Zone | Reports |
|---|---|---|---|---|
| Moisture Sensor B | `24:6F:28:B1:00:01` | MoistureSensor | Zone 1 | `SoilMoisturePct` (dries fast - triggers alerts soon after start-up) |
| Pump A | `24:6F:28:A1:00:01` | WaterPump | Zone 1 | `FlowLitresPerMin`. When ON, Zone 1 moisture rises. |
| ESP32 Controller Z1 | `24:6F:28:C1:00:01` | Esp32Controller | Zone 1 | `TemperatureC` (occasional overheat) |
| Moisture Sensor D | `24:6F:28:B2:00:01` | MoistureSensor | Zone 2 | `SoilMoisturePct` |
| Pump C | `24:6F:28:A2:00:01` | WaterPump | Zone 2 | `FlowLitresPerMin` (starts ON) |
| Smart Plug 1 / 2 | `24:6F:28:D3:00:01` / `:02` | SmartPlug | Zone 3 | `PowerWatts` (occasional spike; ~0 W when OFF) |
| Relay R1 | `24:6F:28:E3:00:01` | Relay | Zone 3 | `RelayClosed` (0/1) |
| Power Meter M1 | `24:6F:28:F3:00:01` | PowerMeter | Zone 3 | `PowerWatts` (sum of the Zone 3 plugs + base load) |

Readings depend on the real device state, so commands have visible effects:
turn Pump A on and Moisture Sensor B rises, turn a plug off and its draw drops
to ~0 W.

## Part 2 data structures - where and why

| Structure | Where | What it does and why it fits |
|---|---|---|
| **`Queue<T>`** | [`TelemetryProcessor`](src/SmartX.Api/RealTime/TelemetryProcessor.cs) `_normalQueue` | Normal (Info) telemetry. FIFO means readings are processed in the order they arrived, so each device's values are applied in sequence. Enqueue and dequeue are O(1). |
| **`PriorityQueue<TElement, TPriority>`** | [`TelemetryProcessor`](src/SmartX.Api/RealTime/TelemetryProcessor.cs) `_priorityQueue` | Warnings and critical alerts. Priority = `(level, arrival number)`, where level 0 = Critical (power spike > 1500 W, moisture < 20 %, overheat > 40 °C) and 1 = Warning. It always dequeues the smallest, so Critical comes before Warning, and the arrival number keeps equal priorities first-come-first-served. The worker serves this queue before the normal one. |
| *Starvation guard* | `TryDequeueNext` | After `MaxConsecutivePriority` (default 5) alerts in a row, one waiting normal message is processed before alerts continue, so a flood of alerts can never block normal telemetry forever. |
| **`Stack<T>`** | [`CommandService`](src/SmartX.Api/RealTime/CommandService.cs) `_undoStack` | Every accepted command that changed a device is pushed. Undo pops the most recent (LIFO) and restores the device's previous state. Push, Peek and Pop are O(1). |
| **`Dictionary<TKey, TValue>`** | [`DeviceRegistry`](src/SmartX.Api/RealTime/DeviceRegistry.cs) `_devicesByMac` | Registered devices keyed by normalised MAC address. Every incoming packet is matched to its device with an O(1) average hash lookup instead of scanning a list. Unknown MACs return `false` (HTTP 404), never an exception. |
| **`SortedDictionary<TKey, TValue>`** | [`HistoryLog`](src/SmartX.Api/RealTime/HistoryLog.cs) `_entries` | Readings, commands, alerts and recoveries, kept sorted by key on every insert (balanced tree, O(log n)). The key is [`HistoryKey(Timestamp, Sequence)`](src/SmartX.Api/RealTime/HistoryLog.cs): it compares by time, then by arrival sequence. Readings with **identical timestamps are both kept** (different sequence numbers), and late or buffered readings sort into the right place. The log is read front-to-back for timelines and charts without re-sorting. |
| **`HashSet<T>`** | [`ActiveErrorTracker`](src/SmartX.Api/RealTime/ActiveErrorTracker.cs) `_active` | The currently active errors as `(MAC, code)` pairs, e.g. `(Smart Plug 1, POWER_SPIKE)`. `Add` returns `false` for an error that is already active, so repeats are counted as suppressed duplicates instead of raising new alerts. On recovery the entry is `Remove`d, so the set only ever means "active now". Past alerts stay in the history log. |

### Undo - rules and limitations

- Undo reverts the **top** command only if the device is still in the state
  that command left it in. If a newer reading shows the device changed since
  (e.g. a plug switched back on locally), the undo is **refused** and that
  stale entry is discarded. Restoring it would overwrite a newer real change.
- If the device is **disconnected**, undo **fails** and the entry stays on the
  stack so it can be retried when the device reconnects.
- An undo is not pushed onto the stack, so pressing Undo twice reverts the last
  two commands (it does not "redo").
- A command that changes nothing (e.g. turning on a device that is already
  on) is accepted but not pushed, because there is nothing to undo.
- Readings captured **before** a command cannot overwrite the state that
  command set. They were already queued when the switch happened.

## Real-time updates (Server-Sent Events)

- **Why SSE:** updates only flow API -> browser. Commands go the other way as
  normal HTTP POSTs. SSE is plain HTTP, is built into ASP.NET Core 10
  (`TypedResults.ServerSentEvents`), and needs no extra library. The client
  reads it with `HttpClient` and browser response streaming, so SignalR was
  not needed.
- **Server** ([`StreamEndpoints`](src/SmartX.Api/Endpoints/StreamEndpoints.cs),
  [`LiveUpdateBroadcaster`](src/SmartX.Api/RealTime/LiveUpdateBroadcaster.cs)):
  - Each connection gets its own bounded channel that drops its oldest update
    when full, so a slow browser can't hold up the API.
  - Every new connection starts with a full snapshot of devices, errors and
    queues.
  - A heartbeat is sent after 15 s of silence.
  - When the browser disconnects, the request is cancelled and the connection
    is unsubscribed.
- **Client** ([`LiveDashboardState`](src/SmartX.Client/Services/LiveDashboardState.cs)):
  - There is one connection per browser tab, shared by every page.
  - If the stream drops, or nothing arrives for 40 s, it reconnects with a
    backoff of 1, 2, 4 ... up to 10 s, and the snapshot then brings it up to
    date.
  - Incoming updates only mark the data as changed. Pages re-render at most
    every 250 ms, so a burst of readings doesn't cause a burst of renders.

## Recommendation engine

[`RecommendationEngine`](src/SmartX.Api/RealTime/RecommendationEngine.cs)
(`GET /api/insights`) uses only data the app has recorded. Each suggestion
shows the device, the issue, **why** it was generated, the supporting data, and
a suggested action. It is **never executed automatically**. If a suggestion
includes a command, it only runs when the user presses its button.

1. **Behaviour patterns (alert -> response).** For every alert in the last 60
   minutes, the user's commands and searches made **while that alert was
   active** count as responses to it. "While active" means until the device
   recovered, capped at 5 minutes. Pairs of
   (trigger device + error, response device + action) are tallied in a
   Dictionary, and a HashSet per alert stops double counting. A pair becomes a
   pattern when it has:
   - **support ≥ 2:** it was seen at least twice;
   - **confidence ≥ 50 %:** it followed at least half of that trigger's alerts.

   This is the support/confidence idea from association-rule mining. Example:
   *"Moisture Sensor B raised MOISTURE_LOW 3 times; 2 of those times (67 %)
   you switched Pump A ON."* If the trigger is active again right now, the
   suggestion offers that command.
2. **Recurring faults.** At least 3 alerts of the same type for one device in
   the lookback window.
3. **Out-of-range readings.** Each connected device's latest reading checked
   against the thresholds (e.g. low moisture -> turn on the pump in that zone).
4. **Disconnected devices.** Devices with no telemetry within the timeout.

**Score** (used to sort the list): patterns score
`50 + 40 × confidence (+30 if the trigger is active now) + min(count, 10)`.
Recurring faults score `40 + 5 × count`, critical out-of-range readings 45,
disconnected devices 35, and warning-level readings 25.

With fewer than 3 recorded searches and commands, the page says there is not
enough history to detect patterns yet, but still shows the fault-based
insights. Every threshold is configurable under `RealTime:Recommendations`.

## Testing each feature by hand

Every request below is also in
[`src/SmartX.Api/SmartX.Api.http`](src/SmartX.Api/SmartX.Api.http). Open it in
Visual Studio and click *Send request*. The curl examples are for Git Bash.
In PowerShell, use the `.http` file instead.

**Simulate telemetry packets.** The simulator sends one reading per device
every second automatically. To send your own:

```bash
curl -X POST http://localhost:5190/api/stream/telemetry -H "Content-Type: application/json" \
     -d '{"macAddress":"24:6F:28:B1:00:01","sensorType":"SoilMoisturePct","value":45}'
```

Or use **Queues & Alerts -> Send a telemetry packet**.

**Trigger a high-priority alert.** Send moisture `12` (critical drop) or plug
power `1900` (severe spike). The response says
`Accepted into the priority queue (priority 0)`.

**Queue + PriorityQueue.** On **Queues & Alerts**, click *Pause processing*,
then *Inject priority demo burst*, then *Resume processing*. The
*Priority demo result* table shows that the critical alert, sent 10th of 10,
was processed **1st**, the warning **2nd**, and the eight normal readings
after them in the order they were sent. The same is available through the API:
`POST /api/stream/processing/pause`, `POST /api/stream/demo/priority-burst`,
`POST /api/stream/processing/resume`, then `GET /api/stream/processing`.

**Stack and undo.** On **Commands & Undo**:

1. Turn Pump A on, then turn Smart Plug 1 off. The stack shows the plug on top.
2. *Undo last command* restores the plug.
3. Undo again to restore the pump.
4. Undo once more: it is refused with "stack is empty".
5. To see the disconnect rule, send a command, click *Simulate 20s outage*,
   wait about 8 s, and try Undo. It **fails** and the entry stays on the stack.
   Once the device reconnects, Undo works.

**Dictionary.** `GET /api/devices/24-6f-28-a1-00-01` finds Pump A even with a
different case and separator. Unknown MACs get 404 in telemetry and commands.

**SortedDictionary.** On **History Log**, pick a device, then:

- *Send 2 readings with the same timestamp*: both rows appear with the same
  time and consecutive sequence numbers, labelled "same timestamp - kept".
- *Send a late reading*: a reading captured 30 s ago is inserted 30 s back in
  the timeline, not at the end.

**HashSet.** Wait for Moisture Sensor B to drop below 20 % (about 15 s after
start-up), or send moisture `12` twice with *Send twice*. **Queues & Alerts ->
Active errors** shows one `MOISTURE_LOW` entry whose *duplicates suppressed*
count keeps rising. The history log has only one Alert. Turn Pump A on: once
moisture is back above 30 % the error disappears and a Recovery entry is
logged.

**Disconnected devices.** Click *Simulate 20s outage* (Commands page), or
`POST /api/simulator/devices/{mac}/disconnect?seconds=20`. After 8 s the device
shows **Disconnected**, gets a `DISCONNECTED` error, commands to it fail, and it
recovers on its own.

**Search and filter.** On the **Live Dashboard**, type `pump` and press
*Search*, or pick Kind / Category / Status / *Alerts only*. The search runs on
the API (and is recorded for recommendations) and is re-applied live: e.g.
with Status = Disconnected, a device appears in the list the moment it drops
out. Filters are kept when you navigate away and back.

**Recommendations.** Wait for Moisture Sensor B to raise `MOISTURE_LOW`, then
search for `Pump A` and turn Pump A on. When it recovers, turn the pump off.
Repeat once more. On the third drop, **Suggested Actions** shows *"Repeated
response pattern: MOISTURE_LOW on Moisture Sensor B -> switched Pump A ON"*
with a *Turn on Pump A* button. Before any history exists it reports "Not
enough history to detect behaviour patterns yet".

**Error handling.** Stop the API while the dashboard is open: pages show an
"API problem" banner, the sidebar shows *Reconnecting...*, and nothing
crashes. Start the API again and the stream reconnects by itself.

## API reference

Base URL `http://localhost:5190`. Enums are sent as strings
(e.g. `"TurnOn"`). OpenAPI JSON is at `/openapi/v1.json` in Development.

### Part 2

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/devices?search=&kind=&category=&status=&alertsOnly=&record=` | Search and filter devices (`record=false` skips search history) |
| GET | `/api/devices/{mac}` | One device by MAC |
| GET | `/api/devices/searches?take=` | Recent searches |
| POST | `/api/stream/telemetry` | Submit a packet `{ macAddress, sensorType, value, capturedAtUtc? }` -> 202 / 400 / 404 |
| GET | `/api/stream/live` | **SSE** stream of live updates (`text/event-stream`) |
| GET | `/api/stream/processing` | Queue depths and processing order |
| POST | `/api/stream/processing/pause` / `resume` | Pause or resume the processing worker |
| POST | `/api/stream/demo/priority-burst` | Queue 8 normal + 1 warning + 1 critical at once |
| POST | `/api/commands` | Command `{ macAddress, action: "TurnOn"\|"TurnOff" }` -> 200 accepted / 400 rejected / 404 unknown / 503 failed |
| POST | `/api/commands/undo` | Undo the most recent command -> 200 / 409 / 503 |
| GET | `/api/commands?take=` | Command and undo history, newest first |
| GET | `/api/commands/stack` | Undo stack, top first |
| GET | `/api/history?mac=&kind=&minutes=&take=` | Chronological history (oldest first) |
| GET | `/api/errors` | Active errors with duplicate counts |
| GET | `/api/insights` | Suggested actions report |
| GET | `/api/simulator` | Simulator status |
| POST | `/api/simulator/start` / `stop` | Start or stop the simulator |
| POST | `/api/simulator/devices/{mac}/disconnect?seconds=` | Simulate a device outage |

Every packet sensor type: `SoilMoisturePct` (0-100), `PowerWatts` (≥ 0),
`FlowLitresPerMin` (≥ 0), `RelayClosed` (0 or 1), `TemperatureC` (-40 to 125).

### Part 1

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/sensors` | List registered sensors |
| POST | `/api/sensors` | Register a sensor (also joins the Part 2 registry) |
| POST | `/api/sensors/{id}/attachments` | Upload a file attachment (multipart) |
| GET | `/api/sensors/{id}/attachments/{attachmentId}` | Download an attachment |
| POST | `/api/telemetry` | Ingest a single reading |
| POST | `/api/telemetry/batch` | Ingest a jagged per-device sample batch |
| POST | `/api/telemetry/seed` | Generate mock readings for registered sensors |
| GET | `/api/telemetry/feed` | Recent telemetry pulse feed |
| GET | `/api/telemetry/aggregate/{nodeId}` | Aggregated power load for a node |
| GET | `/api/telemetry/grid` | Zone-load heatmap snapshot |
| POST | `/api/deployment/validate` | Recursively validate a deployment tree |
| GET | `/api/deployment/sample` | A ready-made sample deployment tree |

## Automated tests

```bash
dotnet test tests/SmartX.Tests
```

> On Windows, **stop the API first**. A running API locks its DLLs, and the
> test build then fails with "file is being used by another process".

56 xUnit tests run against the real Part 2 services, wired up with a fake
clock (no web server), covering:

- FIFO order, critical-before-normal, priority ties and the starvation guard
  ([`TelemetryProcessorTests`](tests/SmartX.Tests/TelemetryProcessorTests.cs)).
- Malformed packets and unknown devices.
- MAC lookups and duplicate registration
  ([`DeviceRegistryTests`](tests/SmartX.Tests/DeviceRegistryTests.cs)).
- Chronological history, equal timestamps, late packets and the cap
  ([`HistoryLogTests`](tests/SmartX.Tests/HistoryLogTests.cs)).
- Duplicate suppression, recovery and disconnects
  ([`ActiveErrorTrackerTests`](tests/SmartX.Tests/ActiveErrorTrackerTests.cs)).
- Commands, LIFO undo and every undo refusal case
  ([`CommandServiceTests`](tests/SmartX.Tests/CommandServiceTests.cs)).
- Search and filter rules ([`DeviceQueryTests`](tests/SmartX.Tests/DeviceQueryTests.cs)).
- Patterns, thresholds, "not enough data", and the guarantee that nothing is
  auto-executed ([`RecommendationEngineTests`](tests/SmartX.Tests/RecommendationEngineTests.cs)).

The Blazor pages are not covered by automated UI tests. They were checked by
hand in the browser.

## Limitations and assumptions

- **No hardware and no persistence.** All devices are simulated, and all state
  is in memory and lost when the API restarts.
- **Single API instance.** The queues, stack and sets live in one process.
  Running several API instances would need a shared store or message broker.
- **Severity rules are threshold-based** per sensor type. Part 1's
  delta-based severity on the Telemetry page is unchanged and separate.
- **Errors clear only when fully back to normal.** An active error clears on
  an Info-level reading, so moisture must rise above 30 %, not just above
  20 %. This stops alerts flickering around the threshold.
- **Global undo stack.** There is one undo stack shared by all users, and no
  authentication. Anyone with the dashboard can send commands.
- **Searches only count for patterns when specific.** A search contributes to
  pattern detection only if it matched 1-3 devices. A search for everything
  says nothing about a specific device.
- **Final PoE not implemented.** Network topology and mesh routing are a
  placeholder page only.

## Part 1 C# concepts

| Requirement | Location |
|---|---|
| **Generics** | [`TelemetryPacket<T>`](src/SmartX.Shared/Generics/TelemetryPacket.cs): a single `struct`-constrained generic class handles float/int/bool telemetry uniformly without boxing. |
| **Operator overloading** | [`MeterReading`](src/SmartX.Shared/Aggregation/MeterReading.cs): overloads `+`, `-`, `==`, `!=`, `>`, `<`, `>=`, `<=` so power readings aggregate and compare directly (`Meter3 = Meter1 + Meter2`). Used for real in [`TelemetryStore`](src/SmartX.Api/Services/TelemetryStore.cs) to drive severity and the per-node running total. |
| **Advanced arrays/lists** | Jagged `float[][]` batch ingestion in [`TelemetryStore.IngestFloatBatch`](src/SmartX.Api/Services/TelemetryStore.cs), and a rectangular `double[,]` zone-load matrix behind the grid heatmap. |
| **Recursion** | [`DeploymentTreeValidator`](src/SmartX.Shared/Validation/DeploymentTreeValidator.cs): walks Facility → Zone → Sub-Zone → Device trees of unknown depth. |

The Part 1 **Telemetry** page's *Live Telemetry Pulse* (a severity-coded feed,
zone heatmap and SVG gauge) also displays the Part 2 simulated readings,
because processed Part 2 packets are forwarded into the Part 1 store.

## References

In-text citations in the source code (e.g. `(Microsoft, n.d.c)`) refer to the
list below. Where one author has several sources in the same year, they are
lettered a, b, c ... in the order used in this list.

Agrawal, R., Imieliński, T. and Swami, A. (1993) 'Mining association rules between sets of items in large databases', *Proceedings of the 1993 ACM SIGMOD International Conference on Management of Data*, Washington DC, 25-28 May. New York: ACM, pp. 207-216. doi: 10.1145/170035.170072.

Espressif Systems (n.d.) *ESP32 Wi-Fi & Bluetooth SoC*, viewed 13 September 2026, <https://www.espressif.com/en/products/socs/esp32>.

Internet Engineering Task Force (2014) *RFC 7252: The Constrained Application Protocol (CoAP)*, viewed 13 September 2026, <https://datatracker.ietf.org/doc/html/rfc7252>.

Microsoft (2025a) *Boxing and Unboxing (C# Programming Guide)*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing>.

Microsoft (2025b) *ASP.NET Core Blazor Hosting Models*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models>.

Microsoft (2026) *Generic Types and Methods (C#)*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/generics>.

Microsoft (n.d.a) *Minimal APIs Quick Reference*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis>.

Microsoft (n.d.b) *Queue<T> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.queue-1>.

Microsoft (n.d.c) *PriorityQueue<TElement,TPriority> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.priorityqueue-2>.

Microsoft (n.d.d) *Stack<T> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.stack-1>.

Microsoft (n.d.e) *Dictionary<TKey,TValue> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2>.

Microsoft (n.d.f) *SortedDictionary<TKey,TValue> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.sorteddictionary-2>.

Microsoft (n.d.g) *HashSet<T> Class*, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.hashset-1>.

Microsoft (n.d.h) *Channels*, .NET documentation, Microsoft Learn, viewed 19 October 2026, <https://learn.microsoft.com/en-us/dotnet/core/extensions/channels>.

OASIS (2019) *MQTT Version 5.0*, OASIS Standard, viewed 13 September 2026, <https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html>.

OWASP Foundation (n.d.) *File Upload Cheat Sheet*, OWASP Cheat Sheet Series, viewed 13 September 2026, <https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html>.

WHATWG (n.d.) *HTML Living Standard: Server-sent events*, viewed 19 October 2026, <https://html.spec.whatwg.org/multipage/server-sent-events.html>.
