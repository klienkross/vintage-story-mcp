# vintage-story-mcp

**Project status: experimental.** Milestone 0 is an observation bridge only; there is no MCP server or game control yet.

```text
Vintage Story client mod
→ localhost Agent Bridge API
→ ordinary external clients
```

Current endpoint: `GET /v1/state` on `http://127.0.0.1:42420`.

## Quick start on Windows

Prerequisites: Vintage Story and .NET 10 SDK.

Clone the current Milestone 0 branch, build it, run its tests, and install the built mod into the normal user Mods directory:

```powershell
git clone -b task/first-eye https://github.com/klienkross/vintage-story-mcp.git
cd vintage-story-mcp
powershell -ExecutionPolicy Bypass -File .\scripts\dev.ps1 -Install
```

The script looks for the Vintage Story installation in this order:

1. `-VintageStoryPath` supplied explicitly;
2. the `VINTAGE_STORY` environment variable;
3. `%APPDATA%\Vintagestory`.

If auto-detection fails:

```powershell
.\scripts\dev.ps1 -VintageStoryPath "C:\path\to\Vintagestory" -Install
```

After the script finishes, start Vintage Story and query the bridge:

```powershell
python client\probe.py state
```

Use `-SkipTests` if you only want to build/install the mod. Without `-Install`, the script builds but does not copy anything into the user Mods directory.

## Development setup

The project follows the official Vintage Story SDK-style mod pattern: the game assembly is referenced from `$(VINTAGE_STORY)/VintagestoryAPI.dll`; proprietary Vintage Story binaries are not committed.

1. Install Vintage Story and the .NET SDK required by your installed Vintage Story version. The current official `anegostudios/vsmodtemplate` targets `net10.0`, which this milestone follows.
2. Set environment variable `VINTAGE_STORY` to the Vintage Story install directory. Do not write a machine-specific absolute path into the repository.
3. Build the mod:

   ```bash
   dotnet build mod/VintageStoryAgent.csproj -c Debug
   ```

4. Output is under `mod/bin/Debug/Mods/vintagestoryagent/`. Point Vintage Story at `mod/bin/Debug/Mods` using `--addModPath`, or copy the `vintagestoryagent` directory into the game's user Mods directory.
5. Start the client and confirm the log contains `Vintage Story Agent observation bridge listening on http://127.0.0.1:42420/`.

## Tests

Transport/protocol tests deliberately do not reference Vintage Story assemblies:

```bash
dotnet run --project tests/VintageStoryAgent.Tests/VintageStoryAgent.Tests.csproj
python -m unittest discover -s client -p 'test_*.py'
```

## MANUAL SMOKE

1. Build/install the client mod and launch Vintage Story.
2. Before entering a world, run `python client/probe.py state`; expect a non-success process exit and JSON `{"ok": false, "code": "player_unavailable"}` from HTTP 503.
3. Enter a world and stand at a recognizable coordinate.
4. Run `python client/probe.py state`; expect HTTP 200 JSON with real `position`, `yaw`, and `pitch`.
5. Aim at empty space; expect `selection.block` to be `null`.
6. Aim at a known block; expect its integer block position and asset code such as `game:granite`.
7. Exit/disable the mod, then rerun the probe; the connection should be refused/unavailable because `ModSystem.Dispose()` shuts down the listener.

Protocol details are in `docs/protocol.md`.
