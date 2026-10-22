# WPF Launch Simulation

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

.NET 9 / Windows WPF Viewport3D simulation of Korea's **Nuri (KSLV-II) flight 2** (2022-06-21 16:00 KST, Naro Space Center): countdown, liftoff, Max-Q, stage and fairing separations, orbit insertion and satellite separation, rebuilt from public sources for education. No game engine, HelixToolkit or custom shaders.

> Status: early version. Flight model, countdown, telemetry, camera direction, control room and the post-insertion fine-adjustment exercise work end to end; visuals are still being refined.

## What it shows

- **Countdown** from the automatic launch sequence (PLO, T−10 min) to ignition at T−4 s and liftoff.
- **Ascent**: 3-DoF point-mass flight with Earth rotation, gravity, an exponential atmosphere, sea-level/vacuum thrust, staged masses and drag. Events are raised from the dynamics: sonic, Max-Q, stage separations, ignitions, fairing separation, 3rd-stage cutoff.
- **Separated hardware** (stages, fairing halves) falls ballistically; splashdown distances are logged next to the planned values.
- **Telemetry**: mission clock, altitude, inertial/ground speed, Mach, sensed acceleration, dynamic pressure, downrange, flight-path angle, propellant, live perigee/apogee/inclination.
- **Camera direction**: automatic broadcast-style switching, or manual 3rd-person chase (drag/wheel), control room, onboard camera, pad ground camera and full-Earth view.
- **Onboard camera vibration**: strong at liftoff (pad acoustics) and around Max-Q/transonic (buffeting), weak while the upper stages burn in vacuum, none after engine cutoff, with short shocks at separations and ignitions. Amplitudes are illustrative.
- **Sound (synthesised, can be turned off)**: engine roar and exhaust crackle by view and phase (loud near the ground, a low rumble in vacuum, silence after cutoff; heard from the pad/control room it fades with distance), countdown beeps from T−10 s and thuds at separations. No recordings are shipped; clips are generated at first run. Muted when paused or above 5×.
- **Fine adjustment (education)**: after separation the camera and telemetry follow the performance verification satellite. "Auto" schedules a circularisation burn at the next apoapsis; the practice buttons apply prograde/retrograde, normal/antinormal and radial burns (1/5/10 m/s) and show the orbit before (grey) and after (cyan). The 30 m/s Δv budget is an example value, not a satellite specification.
- **Satellite after separation**: the performance verification satellite has body-mounted solar panels (no deployable wings, inferred from released photos). It separates tumbling, is de-spun (magnetorquers, then reaction wheels) to below 0.3°/s, turns its panels to the Sun, and the first beacon is received at King Sejong Station at T+41:36 (published). The sequence follows KSAS 2023 abstracts; phase timings and the tumble rate are illustrative.
- **After the flight**: a closing card summarises what followed (news reports): two-way contact with the Daejeon ground station on 2022-06-22, the four university cubesats ejected on 6/29, 7/1, 7/3 and 7/5, and the end of PVSAT operations in August 2024.
- **Mission timeline**: a bar under the 3D view with buttons for liftoff, Max-Q, separations, orbit insertion and satellite separations; a jump re-runs the deterministic flight to that moment (backward jumps restart it).
- **Explanation cards**: short Korean explanations of liftoff, sonic, Max-Q, staging, fairing, orbit insertion and the satellite/mass-simulator separations (can be turned off).
- **Visuals**: Nuri model with flag and "대한민국" markings, seams, raceways and bell nozzles; detailed launch complex 2 (lattice tower, service arms, deck, flame trench, lightning masts; layout illustrative); soft billboard exhaust cloud; gradient plumes; altitude sky colours, night-side city lights and an atmosphere limb glow. Sky: altitude-dependent gradient strongest near the horizon, a sun aureole, a thin cirrus sheet and fair-weather cumulus out to ~120 km (side view from below, flat tops from above; layout illustrative).
- **Control room**: flight map with ground track and tracking stations (Naro, Jeju, Palau), altitude and speed charts, station elevation.

Pad-area terrain: NASA GIBS Landsat WELD (2000, ~30 m) around Naro, built by `WpfLaunchSimulation/build_assets/prepare_pad_texture.py` (bounds in `Assets/naro-area.json`). The source mosaic has some swath-edge streaks over the sea, and terrain is flat (no elevation). Within 3 km of the pad the same image drives a near-field terrain mesh (land/sea from the colours, procedural canopy and wave detail); its hill relief is illustrative since no elevation data is shipped. Apron concrete and building facades are procedural textures.

## Accuracy

Reference values and their sources are in [docs/research-nuri-flight2.md](docs/research-nuri-flight2.md). Public: engine thrust, specific impulse, mass flow, burn times, liftoff mass, event times/altitudes, target orbit. **Approximated** (not public): stage dry masses, pitch programme and guidance, launch azimuth, tracking-station coordinates, fairing shape.

| Event | Public (actual flight 2) | Simulation |
|---|---|---|
| Stage 1 separation | 123 s / 62 km | 123 s / 52.7 km |
| Fairing separation | 227 s / 202 km | 227 s / 198.6 km |
| Stage 2 separation | 269 s / 258~273 km | 270 s / 279.8 km |
| Stage 3 cutoff | ≈790 s / 700 km | 791 s / 699 km |
| Orbit | 700 km ±5 %, ≈98.2° SSO | 686 × 724 km, 98.24° |
| Max acceleration | design limit 4.3 G | 4.25 G |
| Max-Q | planned ≈64 s / 11 km | 63 s / 8.6 km |
| Stage 1 / fairing / stage 2 splashdown | planned ≈413 / 1,514 / 2,800 km | 314 / 1,180 / 2,429 km |

The first-stage trajectory is about 9 km lower than flown; making it steeper costs too much energy for the approximated masses, so drop distances are also short. Satellite separations use the published times (875 s, 945 s).

`tools/ascent_prototype.py` is the Python prototype (same model as `AscentSimulator`, plus splashdown distances); `tools/tune_ascent.py` searches the approximated parameters. With the published liftoff mass and KARI mass flow no combination fits every public target at once; see section 7.1 of the research document.

## Build

```powershell
dotnet build WpfLaunchSimulation.sln -c Release
dotnet run --project WpfLaunchSimulation -c Release
```

## License

[MIT License](LICENSE) — keep the copyright notice (`Copyright (c) 2026 Song-Sungyoub`) and license text in copies or substantial portions. Earth imagery: NASA Blue Marble. Nuri flight data: public KARI releases and news reports (see the research document).
