# Part 2 - YouTube demonstration script

Target length: about 8-10 minutes. Every step below shows a feature that is
implemented and has been tested. Say clearly near the start that **all devices
are simulated**.

## Before recording

- [ ] Close anything else using ports 5190 / 5122.
- [ ] `dotnet build SmartX.slnx` succeeds.
- [ ] `dotnet test tests/SmartX.Tests` shows 56 passed (run it with the API **stopped**).
- [ ] Browser zoom at 90-100 %, window wide enough to show the dashboard's two columns.
- [ ] Have `src/SmartX.Api/SmartX.Api.http` open in Visual Studio if you want to show raw API calls.

## 1. Start the application (~45 s)

1. Terminal 1: `dotnet run --project src/SmartX.Api --urls http://localhost:5190`
2. Terminal 2: `dotnet run --project src/SmartX.Client --urls http://localhost:5122`
3. Open http://localhost:5122.
4. Point out the orange **"SIMULATED DEVICE NETWORK"** banner and the sidebar's
   **"Live stream connected"**.

> Say: "The API runs a simulator that plays nine ESP32 devices. Their packets go
> through the same validation and queues as real ones would. Updates reach the
> browser over Server-Sent Events."

## 2. Navigation (~45 s)

1. Show the three pillar groups in the sidebar. Open Home and show the cards:
   Part 1 online, Part 2 online, Final PoE **not yet implemented**.
2. Click **Telemetry** (Part 1). The pulse feed and heatmap now show the live
   simulated readings too.
3. Click **Topology**. Read out that mesh routing is a placeholder for the final PoE.
4. Click **Live Dashboard**. Point out that the page didn't reload and the live
   indicator stayed connected.

## 3. Live simulated readings (~1 min)

1. On the **Live Dashboard**, show the device cards updating every second: name,
   MAC, zone, reading, "seen Xs ago", the status pill and the **SIM** badge.
2. Show the summary tiles: Active / With alerts / Disconnected, and the queue depths.
3. Show the **Live event stream** on the right.

## 4. Critical alert priority (~1.5 min)

1. Open **Queues & Alerts**. Explain the two queues and the priority numbers
   (0 = Critical, 1 = Warning).
2. Click **Pause processing**, show the queue depths growing, then click
   **Inject priority demo burst**.
3. Click **Resume processing**. In **Priority demo result**: the critical spike
   was sent **10th of 10** but processed **1st**, the warning 2nd, and the
   normal readings after them in their original order.
4. Mention the starvation guard: after 5 alerts in a row, one normal message is let through.

## 5. Search and filter (~1 min)

1. Back on **Live Dashboard**, type `pump` and press **Search**: 2 of 9 shown.
   Point out the note that the search was recorded.
2. Clear it. Set **Category = Actuator**, then **Alerts only**.
3. Go to another page and come back: the filters are still set.

## 6. Manual command and device state (~1 min)

1. Turn **Pump A** on from its card. Show the green "Command accepted" feedback,
   the card switching to **ON**, and the flow reading going to ~12 L/min.
2. Watch **Moisture Sensor B** (same zone) start rising - the command changed
   the simulated state, not just a label.
3. Optional: on **Commands & Undo**, send *Turn on* to **Moisture Sensor B** to
   show a **rejected** command (sensors are read-only).

## 7. Command stack and undo (~1.5 min)

1. On **Commands & Undo**: turn Smart Plug 1 **off**, then Relay R1 **on**.
   Show the stack with Relay R1 on **TOP**.
2. Click **Undo last command** twice: the relay is undone first, then the plug (LIFO).
3. Click Undo on the empty stack: "Nothing to undo".
4. Turn Relay R1 on, click **Simulate 20s outage**, wait about 8 s until it shows
   Disconnected, and press Undo: it **fails** and the entry **stays** on the
   stack. Once the relay reconnects, Undo succeeds.

## 8. Chronological history (~1 min)

1. Open **History Log** with Moisture Sensor B selected. Show the chart and the
   table in time order.
2. Click **Send 2 readings with the same timestamp**: two highlighted rows with
   the same time, consecutive sequence numbers, labelled "same timestamp - kept".
3. Click **Send a late reading**: the highlighted row appears 30 s back in the
   timeline, not at the end.

## 9. Duplicate error prevention (~45 s)

1. Open **Queues & Alerts -> Active errors**. When Moisture Sensor B is below
   20 %, it shows **one** `MOISTURE_LOW` entry whose **duplicates suppressed**
   count rises every second.
2. Optional: pick Smart Plug 1, value `1900`, click **Send twice**: one
   `POWER_SPIKE` with 1 duplicate suppressed.
3. Explain that HashSet.Add returns false for an error that is already active,
   and the entry is removed when the device recovers.

## 10. Predictive recommendations (~1.5 min)

1. Open **Suggested Actions** early in the recording (or after a restart) to
   show **"Not enough history to detect behaviour patterns yet"**.
2. Build the pattern (best done just before this section):
   - When Moisture Sensor B shows `MOISTURE_LOW`, search `Pump A` and turn Pump A on.
   - When it recovers (> 30 %), turn Pump A off.
   - Repeat once.
3. When B drops a third time, show the **Pattern** suggestion: "MOISTURE_LOW on
   Moisture Sensor B -> switched Pump A ON". Read out its **Why** line (counts
   and the 67 % confidence) and press **Turn on Pump A** to show it only runs
   when you choose.
4. Point out the other types: Recurring fault, Out of range, Offline.

## Wrap-up (~20 s)

Mention the 56 automated tests (`dotnet test`), the README sections on each
data structure, and that the final PoE (topology and mesh routing) is still to come.
