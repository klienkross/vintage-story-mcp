# Agent Bridge Protocol — Milestone 0

Development endpoint: `http://127.0.0.1:42420`

The listener is intentionally bound to IPv4 loopback only. Milestone 0 has no authentication and must not be exposed on `0.0.0.0`, `::`, or a LAN interface.

## `GET /v1/state`

Success: HTTP `200`.

```json
{
  "ok": true,
  "player": {
    "position": { "x": 123.4, "y": 82.0, "z": -45.2 },
    "yaw": 1.23,
    "pitch": -0.18
  },
  "selection": { "block": null }
}
```

When a block is selected, `selection.block` is:

```json
{
  "position": { "x": 124, "y": 82, "z": -46 },
  "code": "game:granite"
}
```

If the client has not entered a world or the controlled player entity is unavailable: HTTP `503`.

```json
{ "ok": false, "code": "player_unavailable" }
```

Transport errors are stable JSON responses: unknown route is HTTP `404` / `not_found`; unsupported method is HTTP `405` / `method_not_allowed`; unexpected request execution failure is HTTP `500` / `execution_error`.

## Threading

The HTTP accept/request path never reads Vintage Story objects directly. `AgentStateSnapshotProvider` dispatches `AgentStateReader.ReadState()` through `IEventAPI.EnqueueMainThreadTask`, waits for the immutable protocol DTO, then returns that DTO to the HTTP thread for serialization.
