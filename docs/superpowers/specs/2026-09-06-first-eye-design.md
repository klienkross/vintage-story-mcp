# First Eye Design

Milestone 0 establishes one client-only observation path: external localhost HTTP → main-thread snapshot → immutable JSON player state. Scope is exactly `GET /v1/state`; MCP and all action/gameplay features are excluded.

Transport is isolated in `AgentHttpServer`. It depends only on `IAgentStateProvider`, not Vintage Story types. `AgentStateSnapshotProvider` is the thread boundary: it sends the read through an `IMainThreadDispatcher`. The Vintage Story implementation uses the documented thread-safe `IEventAPI.EnqueueMainThreadTask`. `AgentStateReader` is only responsible for translating `ICoreClientAPI` state into protocol DTOs.

The server binds only `127.0.0.1:42420`. A missing world/player/entity maps to HTTP 503 with `player_unavailable`; no zero-vector placeholder is emitted. Mod lifecycle owns the listener and disposes it from `ModSystem.Dispose()`.
