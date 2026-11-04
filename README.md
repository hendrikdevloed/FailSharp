# FailSharp

A random engine failure simulator for Microsoft Flight Simulator 2020, built on SimConnect.

## Projects

- `src/FailSharp.Core` — settings model, JSON persistence, and the probability-driven failure scheduler. No SimConnect dependency; fully unit-testable.
- `src/FailSharp` — the app itself: a tray-resident WPF settings window plus two independent SimConnect connections:
  - **[SimConnect.NET](https://github.com/stopbars/SimConnect.NET)** for reading/writing simvars (failure simulation).
  - A small hand-written P/Invoke layer (`NativeSimConnect.cs`, `MenuClient.cs`) against the native `SimConnect.dll` for the in-sim "FailSharp Setup" Add-ons menu entry — SimConnect.NET has no menu API, and the *official* managed `Microsoft.FlightSimulator.SimConnect.dll` can't be used at all from modern .NET: it's a C++/CLI mixed-mode assembly, which CoreCLR (.NET 5+) cannot load under any configuration.
- `tests/FailSharp.Core.Tests` — xUnit tests for the scheduler and settings persistence, run against a fake SimConnect client (no MSFS required).

## Requirements

- .NET 10 SDK
- Microsoft Flight Simulator 2020 (to actually connect; the app runs standalone otherwise)

## Build & test

```
dotnet build
dotnet test
```

## Run

```
dotnet run --project src/FailSharp
```

The app starts minimized to the system tray (icon may be under the taskbar's overflow "^" arrow the first time Windows shows it). Open settings from the tray icon, or — once MSFS is running and connected — from **Add-ons → FailSharp Setup** inside the sim.

Settings (enabled/disabled, failure probability, check interval, eligible engines) are stored at `%APPDATA%\FailSharp\settings.json` and take effect immediately; there's no separate save step.

## Position tab

Puts the aircraft into level flight at an editable airspeed (default 90 kts) near the nearest airport with a usable runway:

- **Overhead** — above the airport reference point (default 2000 ft above field elevation).
- **Downwind** — abeam runway midpoint, 1 NM to the pattern side, flying the reciprocal (default 1000 ft).
- **Base** — 1 NM to the pattern side, at the distance that puts base + final on a 3° glidepath (default 1000 ft).
- **Final** — on the extended centerline at 3° glidepath distance (default 800 ft ≈ 2.5 NM).

The runway is the end with the most headwind from the sim's ambient wind (among ends within 3 kts of the best headwind, the longest wins; in calm wind the longest runway). Left-hand traffic unless "Right-hand pattern" is ticked. Airports and runways come from the SimConnect facility API on the native connection; the move itself uses `Initial Position`.
