# Smart-X Gateway — Part 1: Sensor Data Ingestion & Telemetry

A hybrid IoT ingestion API and dashboard for the Smart-X ecosystem (hydroponic
farms, utility trackers, and smart grid installations). This submission
covers **Part 1** of the PoE: the ingestion API and the client-facing
configuration dashboard. Real-Time Command Stream (Part 2) and Network
Topology & Mesh Routing (final PoE) are stubbed out as locked pillars on the
landing page.

## Architecture

**Web-First**: ASP.NET Core Minimal API backend + Blazor WebAssembly frontend,
sharing one class library, all on .NET 10.

```
Smart-X/
├── SmartX.slnx                    solution file
├── docker-compose.yml
└── src/
    ├── SmartX.Shared/             class library shared by API and Client
    │   ├── Enums/                 SensorCategory, TelemetryDataType, DeploymentNodeKind, ValidationSeverity
    │   ├── Generics/              TelemetryPacket<T>
    │   ├── Aggregation/           MeterReading (overloaded operators)
    │   ├── Models/                SensorRegistration, SensorAttachment, DeploymentNode
    │   ├── Validation/            DeploymentTreeValidator (recursive)
    │   └── Dto/                   API request/response contracts
    ├── SmartX.Api/                ASP.NET Core Minimal API
    │   ├── Endpoints/             Sensor, Telemetry, Deployment endpoint groups
    │   ├── Services/              In-memory repositories + telemetry pipeline
    │   └── App_Data/uploads/      uploaded sensor attachments
    └── SmartX.Client/             Blazor WebAssembly dashboard
        ├── Pages/                 Home, Sensors, Telemetry, DeploymentTree
        └── Services/               SmartXApiClient (typed HttpClient wrapper)
```

Data is held in memory for this simulation (no database) — the brief's focus
for Part 1 is the ingestion pipeline and dashboard, not persistence.

## Required C# concepts — where to find them

| Requirement | Location |
|---|---|
| **Generics** | [`TelemetryPacket<T>`](src/SmartX.Shared/Generics/TelemetryPacket.cs) — a single `struct`-constrained generic class handles float/int/bool telemetry uniformly without boxing. |
| **Operator overloading** | [`MeterReading`](src/SmartX.Shared/Aggregation/MeterReading.cs) — overloads `+`, `-`, `==`, `!=`, `>`, `<`, `>=`, `<=` so power readings aggregate/compare directly (`Meter3 = Meter1 + Meter2`), used for real in [`TelemetryStore.IngestInt`](src/SmartX.Api/Services/TelemetryStore.cs) to drive severity. |
| **Advanced arrays/lists** | Jagged `float[][]` batch ingestion in [`TelemetryStore.IngestFloatBatch`](src/SmartX.Api/Services/TelemetryStore.cs) (sequential per-device sample batches → `List<TelemetryPacket<float>>`), and a rectangular `double[,]` zone-load matrix backing the grid heatmap. |
| **Recursion** | [`DeploymentTreeValidator`](src/SmartX.Shared/Validation/DeploymentTreeValidator.cs) — walks Facility → Zone → Sub-Zone → Device trees of unknown depth. |

## Dynamic engagement feature

The **Telemetry** page's *Live Telemetry Pulse* is more than a progress bar:
a severity-coded, animated event feed; a rectangular zone-load heatmap; and
an SVG load gauge, all updating together as readings are ingested (manually,
in a mock-seeded batch, or via a "live simulation" polling loop).

## Running locally (no Docker)

**Prerequisites**: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Terminal 1 — API (http://localhost:5190)
cd src/SmartX.Api
dotnet run --urls http://localhost:5190

# Terminal 2 — Client dashboard (http://localhost:5122)
cd src/SmartX.Client
dotnet run --urls http://localhost:5122
```

Open **http://localhost:5122**. The client reads the API's base URL from
[`wwwroot/appsettings.json`](src/SmartX.Client/wwwroot/appsettings.json)
(`ApiBaseUrl`), which already points at `http://localhost:5190`.

Or restore/build/run the whole solution from the repo root:

```bash
dotnet restore SmartX.slnx
dotnet build SmartX.slnx
```

## Running with Docker

```bash
docker compose up --build
```

- API: http://localhost:5190
- Dashboard: http://localhost:8080

The client is a static Blazor WASM build served by nginx; the API runs on
Kestrel in its own container. See
[`src/SmartX.Api/Dockerfile`](src/SmartX.Api/Dockerfile) and
[`src/SmartX.Client/Dockerfile`](src/SmartX.Client/Dockerfile).

## Using the app

1. **Sensors** — register a device (MAC address, Facility/Zone/Sub-Zone,
   Node ID, category), then attach a config file, deployment photo, or log
   to it from the device roster table.
2. **Telemetry** — seed mock readings or start the live simulation to watch
   the pulse feed, heatmap, and gauge; or push a single manual reading for a
   chosen device.
3. **Deployment Tree** — load the valid sample tree, or the deliberately
   broken one, and validate it to see the recursive validator's findings
   (blank names, invalid/duplicate MAC addresses, misplaced node kinds,
   devices with stray children).

## API reference

With the API running, OpenAPI JSON is available at
`http://localhost:5190/openapi/v1.json` in development.

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/sensors` | List registered sensors |
| POST | `/api/sensors` | Register a sensor |
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

## Notes

- CORS is configured for the client's dev origins (`http://localhost:5122`,
  `https://localhost:7163`) via `ClientOrigins` in `appsettings.json` /
  environment variables — override it if you run the client elsewhere.
- Uploaded attachments are stored under `src/SmartX.Api/App_Data/uploads/`
  (git-ignored) and served back through the API.

## References

Several design decisions in the source are cited in-text (e.g. `(OWASP
Foundation, n.d.)`) against the sources below.

Espressif Systems (n.d.) *ESP32 Wi-Fi & Bluetooth SoC*, viewed 13 September 2026, <https://www.espressif.com/en/products/socs/esp32>.

Internet Engineering Task Force (2014) *RFC 7252: The Constrained Application Protocol (CoAP)*, viewed 13 September 2026, <https://datatracker.ietf.org/doc/html/rfc7252>.

Microsoft (2025) *Boxing and Unboxing (C# Programming Guide)*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing>.

Microsoft (2026) *Generic Types and Methods (C#)*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/generics>.

Microsoft (n.d.) *Minimal APIs Quick Reference*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis>.

Microsoft (2025) *ASP.NET Core Blazor Hosting Models*, Microsoft Learn, viewed 13 September 2026, <https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models>.

OASIS (2019) *MQTT Version 5.0*, OASIS Standard, viewed 13 September 2026, <https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html>.

OWASP Foundation (n.d.) *File Upload Cheat Sheet*, OWASP Cheat Sheet Series, viewed 13 September 2026, <https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html>.
