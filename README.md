 # Sandwich Delviery

Co-op sandwich delivery game built with Godot + C# and a SpacetimeDB authoritative backend.

Players carry a shared sandwich stack to the endpoint while avoiding impacts and preserving toppings.

This build currently supports exactly 2 players.

## Tech Stack

- Godot 4 (C#)
- .NET 8
- SpacetimeDB (authoritative simulation)
- SpacetimeDB Godot Client SDK (`SpacetimeDB.ClientSDK.Godot`)

## Repository Layout

- `main.tscn`: Main scene.
- `food-eater.csproj`: Godot C# project.
- `food-eater-server/`: SpacetimeDB module source and backend docs.
- `food-eater-server/spacetimedb/Lib.cs`: Core backend simulation and reducers.
- `module_bindings/`: Generated C# bindings from SpacetimeDB schema.
- `Shared/`: Generated/static shared gameplay data (terrain, toppings, loose items).

## Gameplay Controls

- `W A S D` or arrow keys: Move.
- `Right Mouse Button` (hold): Orbit camera.
- `Mouse Wheel`: Zoom camera.
- `Space`: Jump.
- `I + O` (held together): Reset run.

## Prerequisites

Install the following:

- (For Direct game play) Windows users can download the packaged build from `https://nh-cpu.itch.io/sandwich-delivery`, unzip it, and run the `.exe`. This packaged build is Windows-only.
- Godot 4 with C# support.
- .NET SDK 8.x.
- SpacetimeDB CLI (`spacetime`).

## Quick Start (Local Backend)

1. Build/publish backend and regenerate client bindings.

```powershell
cd food-eater-server
spacetime build
spacetime publish food-eater --server local --delete-data always --yes
spacetime generate --lang csharp --out-dir ../module_bindings
```

2. Build the Godot C# project.

```powershell
cd ..
dotnet build food-eater.sln
```

3. Run the game in Godot.

- Open the folder in Godot.
- Use `main.tscn` as the startup scene.
- Press Play.

## Running Against Maincloud

The default `ServerUrl` in `GameSessionController.cs` targets maincloud.

To publish backend changes there:

```powershell
cd food-eater-server
spacetime publish --server maincloud food-eater --delete-data
```

Then regenerate bindings and rebuild if schema/reducers changed.

## Backend Notes

- Simulation runs server-side on a fixed tick (`SimulationTimer`).
- Clients send input intent; backend resolves movement, collisions, topping state, and win/loss conditions.
- Loose obstacle collisions are backed by manual collider profiles for key assets (for example: Firetruck, GarbageTruck, RaceFuture, WheelDefault variants).

## Regeneration Workflow

Regenerate generated code whenever you change backend schema or reducer signatures:

```powershell
cd food-eater-server
spacetime generate --lang csharp --out-dir ../module_bindings
cd ..
dotnet build food-eater.sln
```

## Troubleshooting

- If client connection fails, verify `ServerUrl` and `DatabaseName` exports on `GameSessionController`.
- If gameplay state looks stale, republish backend and regenerate bindings.
- If generated shared data looks outdated, rerun relevant exporter scripts in Godot and rebuild.
