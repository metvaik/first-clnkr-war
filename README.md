# The First C.L.N.K.R. War

Phase 1 prototype of an original real-time strategy game. Two factions, TDO and C.L.N.K.R., on a whitebox test map. You switch player slots and command both sides yourself.

This pass is a test of the simulation, controls, movement, damage, sight and hauler economy. Models are coloured boxes and capsules. Code is GPLv3 (see `LICENSE`).

The design's "waiting on testing" and "waiting on prototype" numbers are the tunable defaults in `data/`.

## Engine

Developed and compile-checked against **Godot 4.7.2 .NET** (stable, 18 August 2026) and the **.NET 8** target (`Godot.NET.Sdk` 4.7.2).

Install the matching editor before opening the project:

- Windows: [Godot 4.7.2 .NET x86_64](https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_win64.zip) (the .NET editor, which includes C# support)
- .NET SDK 8 or newer: https://dotnet.microsoft.com/download

A newer Godot 4.x editor will usually ask to upgrade the project. Stay on 4.7.2 unless you also change `Godot.NET.Sdk` in `FirstClnkrWar.csproj` to that editor's version. Godot 4.8 was still in beta when this prototype was written.

If the GPU cannot use Vulkan, switch the renderer in Project Settings to **Compatibility** (OpenGL 3).

## Open and run

1. Install the .NET SDK and the Godot 4.7.2 .NET editor.
2. Open `project.godot` in that editor (Project → Import, or double-click the file).
3. Let the editor build the C# solution, or press the **Build** button. The first build restores NuGet packages (`Godot.NET.Sdk`, YamlDotNet).
4. Press **F5**. The main scene is `scenes/test_map.tscn`.

From a terminal, the simulation and its tests do not need the editor:

```bash
dotnet test
```

`dotnet build FirstClnkrWar.sln` builds the Godot game project, the headless sim library, and the tests. If the Godot editor rewrites the solution and drops those projects, `dotnet test tests/FirstClnkrWar.Tests.csproj` still runs the suite.

## Controls

Bindings live in `data/controls.yaml`. Final hotkeys are intentionally not locked. Control groups stay on keys **0–9**.

| Input | Action |
| --- | --- |
| Left click | Select a unit or building you own |
| Left drag | Box-select your mobile units |
| Shift + click / drag | Add to the selection |
| Right click ground | Move |
| Right click enemy | Attack |
| Hold **Z**, right click | Attack-move |
| **H** | Stop (haulers go back to auto-routing) |
| **Ctrl + 0–9** | Save a control group |
| **0–9** | Recall. Double-tap centres the camera |
| **Tab** or the HUD dropdown | Switch the player slot you command |
| **Home** | Centre on the active slot's base |
| **N** | Place a refinery (select a Technician or Constructobot, then left click) |
| **M** | Place a mining site / resource collector the same way |
| Right click a Computer Centre | Hacker or Agent captures it, then infiltrates |
| **Space** | Pause the simulation (camera still moves) |
| **W A S D** or arrows | Pan |
| **Q / E** | Rotate |
| **R / F** | Tilt |
| Mouse wheel | Zoom |
| Middle mouse drag | Pan |
| Alt + middle mouse drag | Orbit |
| Screen edges | Pan, when `edge_scroll` is true |

Arrow keys pan as well as WASD. The HUD shows credits for both players, the storage cap, the tick counter and FPS.

TDO is blue, C.L.N.K.R. is orange. Switching slots also switches fog of war and recentres on that base.

## How the match is laid out

The map is 128×128 small tiles. Terrain is one heightmap mesh: corner vertices are shared, so slopes meet and there are no stepped cliffs. A river with two land bridges splits the map. Rock and water are still impassable to ground units. Each side starts with a refinery (TDO Refinery / C.L.N.K.R. Reclamation Plant), a collector on an ore field, one free hauler, and a handful of units. Rare-earth pockets sit near each base and beside the bridges. Gold terrain is ore, purple is rare earth.

Haulers path to a dock point on the footprint edge of the nearest collector, then to their home refinery, and only then load or unload. Unload is 100 credits per second. Rare earth pays 2×. Income stops when that player's refinery storage cap is full (10,000 per refinery).

The rendered tile matches the sim tile: 1 small tile is 512 world units and 2 on-screen units. A unit's on-screen pace is its design speed times `sim.speed_scale` (currently 0.5). `combat.fire_rate_scale` (currently 0.75) stretches weapon cooldowns and salvo gaps by `1 / scale`.

Each side also starts with a Technician or Constructobot and a Hacker or Agent. Two neutral Computer Centres sit between the bases. A hacker docked at one captures it in 20 seconds, infiltrates for 15 seconds, then has a 45 second window. During that window Scramble Nav fires about every 20 seconds: one enemy unit is forced off its orders for 3 seconds. Progress is a blue bar on the building and a line in the HUD. Building placement deducts the design cost; structure build seconds are a placeholder (`build_s` on the building, or `construction.default_build_s`) because the design tables do not list them. Computer Centre hit points are a placeholder too.

The Mobile SAM only shoots air. A Gunship and a Bird of Prey are on the map so that can be tested. Send the gunship over the other base, or attack-move both groups toward a bridge.

## Editing unit data

Author files are YAML:

| File | Contents |
| --- | --- |
| `data/rules.yaml` | Tick rate, sight cap, economy rates, starting credits, separation |
| `data/factions.yaml` | Names and whitebox colours |
| `data/units/tdo.yaml` | TDO units |
| `data/units/clnkr.yaml` | C.L.N.K.R. units |
| `data/buildings.yaml` | Refinery and collector |
| `data/controls.yaml` | Camera and command keys (not simulated) |

Speeds are small tiles per second. Range and sight are small tiles. The sim stores them as fixed-point thousandths (`1.72` → `1720`) and converts speed with `tiles/s × 512 / tick_hz` world units per tick. At 20 Hz the hauler moves 44.032 world units per tick. The design note rounds that illustration to 44; the sim keeps the exact quotient.

Restart the scene after editing. The loader checks data on startup.

A unit that is not a building and not `transformable: true` must have speed greater than 0. Otherwise loading fails with:

```text
make it a building or transformable
```

`transform_s` defaults to 3 seconds (`schema.transform_s_default` in `data/rules.yaml`).

`cooldown_s`, `salvo_interval_s`, `projectile_speed`, the mining rate and the load rate are prototype defaults. The design tables do not publish a rate of fire yet. `weapon_slots` copies the table's weapon-count column. Only damage figures that are actually published are fired. Where those two disagree (Mobile SAM lists 4 slots and one damage value; Scout Drone and Mk I Walker list 2 slots and one damage channel; Gunship and Bird of Prey list 3 slots and two damage channels), the extra slots are not invented.

Sight defaults to the normal class, 12 small tiles, except the Mobile SAM's `sight_by_target.air: 20`. That lets it spot aircraft out to 20 tiles without revealing ground at that range. Per-unit sight classes are still waiting on testing.

There is a `TODO` in the loader for compiling YAML to a cached binary catalog. Phase 1 reads YAML directly.

## Layout

The simulation does not reference Godot, so it can run headless later.

- `src/Sim/` — tick, fixed-point math, commands, data load, map, A* queue, combat, fog, haulers
- `presentation/` — camera, whitebox meshes, fog overlay, selection, HUD
- `tests/` — fixed-point math, the speed-0 schema rule, roster numbers, pathing, a repeated command log (same state hash), combat, SAM air sight, hauler income

Commands are the only way to give orders. Each command is stamped with the tick it should run. The same log produces the same hash. Rendering interpolates between the last two ticks and can run at the display refresh. Camera state, selection and control groups stay on the client.

1 small tile is 512 world units in the sim and 2 metres on screen.

## Stubbed

Not in this prototype: AI, networking, campaign, Doomsday, squads, veterancy, transports (the APC and Warhorse move but do not carry), mines, stealth, fuel, selling, refinery upgrades, healing, building capture, audio, menus, and final art. Hackers capture Computer Centres and cast Scramble Nav only; the other hack-site perks and the earned-ability list are not implemented. Constructors place a refinery or a collector; they do not build the rest of the roster. Population is loaded (300) and not enforced. Player firepower, armour and speed levels exist (default 0, each level ±20%) and are not exposed in the HUD. Ground units do not block each other's paths. A group move spreads them onto a formation of nearby cells, and a separation push keeps bodies from stacking. Health bars shrink from the left as HP falls.

## Known issues

- Click positions are quantized to fixed-point before they enter the command queue, but the raycast itself uses the GPU/client float camera. Two machines can still disagree on a click until picking is deterministic. Replaying a recorded command log is deterministic; the tests cover that.
- Fog is refreshed every 6 ticks (0.3 s at 20 Hz), so sight lags slightly.
- Attack-move holds still while a target is in range, then continues. A plain move order does not stop to shoot.
- Haulers ignore the economy while they have a move or attack order. Stop sends them back to work.
- Soft separation can nudge a unit off a path cell. It will repath.
- Damage uses `weapon × scale / 256` with integer truncation, minimum 1. Ten damage at +1 firepower is 11, not 12.
- The map is built in code (`PrototypeScenario`), not loaded from a `.wgmap` package.
- YAML files may show an "import" warning in the Godot filesystem dock. They are read as text at runtime. The warning is harmless.

## Verification

`dotnet test` (28 tests) and `dotnet build` of the Godot project succeed on .NET SDK 8.0.427.

Godot 4.7.2 .NET was also launched headless (`--headless --quit-after 90`) against this project. The test scene loaded, printed `Phase 1 ready` with 26 entities, and ran 90 frames without a script error. Headless mode uses the dummy renderer. Camera controls, picking, fog colours and the whitebox meshes still need a look in the editor window on your machine.

## Licence

Code is [GNU GPL v3](LICENSE). Contributions are under the same licence. This prototype has no art, audio or map packages. The design calls for CC BY-NC on those later, and for every asset to be original or CC0. Names, stats and data in this repository belong to this project.
