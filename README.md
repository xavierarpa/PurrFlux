# PurrFlux

Networked event system built on top of **PurrNet** and **UniFlux**. Lets you publish and subscribe to topics across the network using the same string-key pattern as UniFlux, but messages are automatically serialized, sent through PurrNet, and dispatched to connected clients.

## Requirements

- **Unity 2022.3+**
- **PurrNet** (networking layer)
- **UniFlux** (local event bus / attribute dispatching)

## Architecture

```
Publisher ──DispatchNet──► PurrFluxUtils ──PurrNet──► Server ──┬── serverOnly=true  → Invoke on server only
                                                               └── serverOnly=false → Broadcast to all clients
                                                                                          │
                                                                                 listeners dict / Deserialize
                                                                                          │
                                                                                    ◄── Handlers(PlayerID sender, T data)
```

- **Client publishes** → message is sent to server via PurrNet Broadcasts.
- **Server receives** → if `serverOnly`, invokes listeners on the server; otherwise rebroadcasts to all clients.
- **Receivers** → look up topic in listeners, deserialize payload, invoke handlers with `PlayerID sender` info.

## Quick Start

### 1. Manual subscription (`StoreNet`)

Subscribe and unsubscribe to network topics directly, similar to UniFlux's `Store`. Four handler signatures are supported:

```csharp
using PurrFlux;
using PurrNet;
using UnityEngine;

public class ChatListener : MonoBehaviour
{
    private void OnEnable()
    {
        // 1) Action — no payload, no sender
        "chat/ping".StoreNet(OnPing, true);

        // 2) Action<T> — typed payload, no sender
        "chat/message".StoreNet<string>(OnChatMessage, true);

        // 3) Action<PlayerID> — sender only, no payload
        "chat/ping".StoreNet<PlayerID>(OnPingFrom, true);

        // 4) Action<PlayerID, T> — sender + typed payload
        "chat/message".StoreNet<string>(OnChatMessageFrom, true);
    }

    private void OnDisable()
    {
        "chat/ping".StoreNet(OnPing, false);
        "chat/message".StoreNet<string>(OnChatMessage, false);
        "chat/ping".StoreNet<PlayerID>(OnPingFrom, false);
        "chat/message".StoreNet<string>(OnChatMessageFrom, false);
    }

    private void OnPing()
    {
        Debug.Log("Ping received!");
    }

    private void OnChatMessage(string message)
    {
        Debug.Log($"Chat: {message}");
    }

    private void OnPingFrom(PlayerID sender)
    {
        Debug.Log($"Ping from {sender}");
    }

    private void OnChatMessageFrom(PlayerID sender, string message)
    {
        Debug.Log($"Chat from {sender}: {message}");
    }
}
```

### 2. Attribute-based subscription (`MonoPurrFlux` + `[MethodPurrFlux]`)

Inherit from `MonoPurrFlux` to get automatic subscription lifecycle. Mark handler methods with `[MethodPurrFlux("topic")]` — they are discovered by reflection and subscribed on `OnEnable` / unsubscribed on `OnDisable`.

All four handler signatures work with the attribute:

```csharp
using PurrFlux;
using PurrNet;
using UnityEngine;

public class LobbyHandler : MonoPurrFlux
{
    // Action — no params
    [MethodPurrFlux("lobby/ready")]
    private void OnAllReady()
    {
        Debug.Log("All players ready!");
    }

    // Action<T> — typed payload
    [MethodPurrFlux("lobby/join")]
    private void OnPlayerJoined(string playerName)
    {
        Debug.Log($"{playerName} joined the lobby");
    }

    // Action<PlayerID> — sender only
    [MethodPurrFlux("lobby/leave")]
    private void OnPlayerLeft(PlayerID sender)
    {
        Debug.Log($"Player {sender} left");
    }

    // Action<PlayerID, T> — sender + payload
    [MethodPurrFlux("lobby/chat")]
    private void OnLobbyChat(PlayerID sender, string message)
    {
        Debug.Log($"[{sender}]: {message}");
    }
}
```

> `MonoPurrFlux` inherits from `NetworkBehaviour` (PurrNet), so the component must be on a networked object.

### 3. Publishing messages

```csharp
// Broadcast a typed message to all clients (serverOnly: false)
"chat/message".DispatchNet("Hello everyone!", serverOnly: false);

// Send a typed message to the server only (serverOnly: true)
"server/command".DispatchNet("restart", serverOnly: true);

// Broadcast a parameterless event
"lobby/ready".DispatchNet(serverOnly: false);

// Send parameterless event to the server only
"server/ping".DispatchNet(serverOnly: true);

// Optionally specify the delivery channel (defaults to ReliableOrdered)
"game/input".DispatchNet(inputData, serverOnly: true, Channel.UnreliableSequenced);
```

### 4. Mixing with UniFlux attributes

`MonoPurrFlux` calls both `Utils.SubscribeAttributes` (UniFlux) and `PurrFluxUtils.SubscribeAttributes`, so you can use standard UniFlux attributes (`[MethodFlux]`, `[StateFlux]`) alongside `[MethodPurrFlux]` on the same class:

```csharp
using PurrFlux;
using PurrNet;
using UniFlux;
using UnityEngine;

public class HybridHandler : MonoPurrFlux
{
    // Local-only UniFlux event
    [MethodFlux("ui/refresh")]
    private void OnUIRefresh()
    {
        Debug.Log("UI refreshed (local)");
    }

    // Networked PurrFlux event (payload only)
    [MethodPurrFlux("game/score")]
    private void OnScoreUpdate(int newScore)
    {
        Debug.Log($"Score updated: {newScore} (from network)");
    }

    // Networked PurrFlux event (sender + payload)
    [MethodPurrFlux("game/kill")]
    private void OnKill(PlayerID sender, string victimName)
    {
        Debug.Log($"{sender} eliminated {victimName}");
    }
}
```

### 5. Manual subscription with `OnFlux` override

Override `OnFlux` for manual subscriptions alongside PurrNet features like `SyncVar`:

```csharp
using PurrFlux;
using PurrNet;
using UnityEngine;

public class PlayerState : MonoPurrFlux
{
    public SyncVar<string> playerName;

    protected override void OnFlux(in bool condition)
    {
        base.OnFlux(condition);

        if (condition)
        {
            playerName.onChangedWithOld += OnNameChanged;
        }
        else
        {
            playerName.onChangedWithOld -= OnNameChanged;
        }
    }

    private void OnNameChanged(string oldValue, string newValue)
    {
        Debug.Log($"Name changed: {oldValue} → {newValue}");
    }

    [MethodPurrFlux("player/update")]
    private void OnPlayerUpdate(PlayerID sender, string name)
    {
        Debug.Log($"Player update from {sender}");
        playerName.value = name;
    }
}
```

### 6. Server-only messages

Use `serverOnly: true` to send messages that the server processes without broadcasting to all clients. This is useful for client-to-server requests:

```csharp
// Client sends a request — only the server will invoke listeners
"server/request-spawn".DispatchNet(spawnData, serverOnly: true);

// On the server side, a listener handles the request
[MethodPurrFlux("server/request-spawn")]
private void OnSpawnRequest(PlayerID sender, SpawnData data)
{
    if (!isServer) return;
    SpawnEntity(sender, data);
}
```

## API Reference

### Extension Methods on `string`

| Method | Description |
|--------|-------------|
| `"topic".DispatchNet<T>(T data, bool serverOnly, Channel channel = ReliableOrdered)` | Sends a typed message. If `serverOnly`, only the server processes it; otherwise all clients receive it. |
| `"topic".DispatchNet(bool serverOnly, Channel channel = ReliableOrdered)` | Sends a parameterless event with the same routing logic. |
| `"topic".StoreNet(Action, bool)` | Subscribe/unsubscribe a parameterless handler (no sender, no payload). |
| `"topic".StoreNet(Action<PlayerID>, bool)` | Subscribe/unsubscribe a sender-only handler (no payload). |
| `"topic".StoreNet<T>(Action<T>, bool)` | Subscribe/unsubscribe a typed handler (payload, no sender). |
| `"topic".StoreNet<T>(Action<PlayerID, T>, bool)` | Subscribe/unsubscribe a handler with sender + typed payload. |

### Handler Signatures

PurrFlux supports four handler forms, usable both with `StoreNet` and `[MethodPurrFlux]`:

| Signature | Description |
|-----------|-------------|
| `void Handler()` | No payload, no sender info |
| `void Handler(T data)` | Typed payload, no sender info |
| `void Handler(PlayerID sender)` | Sender info only, no payload |
| `void Handler(PlayerID sender, T data)` | Sender info + typed payload |

### Classes

| Type | Description |
|------|-------------|
| `MonoPurrFlux` | Base class (`NetworkBehaviour`) with automatic attribute subscription lifecycle |
| `MethodPurrFluxAttribute` | Marks a method for automatic networked subscription via topic key |
| `PurrFluxUtils` | Static utility class that manages listeners, serialization, and network transport |
| `PurrMessage` | Internal network message struct (`IPackedAuto`) with `topic`, `payload`, and `serverOnly` fields |

### Lifecycle (`MonoPurrFlux`)

```
OnEnable
  ├── Utils.SubscribeAttributes (UniFlux: MethodFlux, StateFlux)
  ├── PurrFluxUtils.SubscribeAttributes (PurrFlux: MethodPurrFlux)
  └── OnFlux(true)  ← override for manual subscriptions

OnDisable
  ├── Utils.SubscribeAttributes (unsubscribe)
  ├── PurrFluxUtils.SubscribeAttributes (unsubscribe)
  └── OnFlux(false)
```

## Editor Inspector

Components inheriting from `MonoPurrFlux` display a custom inspector that shows:

- **NetworkIdentity** info (overrides, status, observers) from PurrNet
- **PurrFlux Tool** panel listing all attributed methods grouped by type:
  - **MethodFlux** (blue) — local UniFlux events
  - **StateFlux** (gold) — local UniFlux state events
  - **MethodPurrFlux** (pink, with cloud icon) — networked PurrFlux events

Each entry shows the topic key, method signature, an input field (if the method has a parameter), and an Invoke button (active in Play Mode).

## Folder Structure

```
PurrFlux/
  Runtime/
    MonoPurrFlux.cs              ← Base class for networked flux components
    PurrFluxUtils.cs             ← Core static utilities (dispatch, listen, subscribe)
    PurrMessage.cs               ← Network message struct (IPackedAuto)
    MethodPurrFluxAttribute.cs   ← Attribute for marking networked handlers
    PurrFlux.asmdef              ← Assembly definition (references PurrNet, UniFlux)
  Editor/
    PurrFluxEditor.cs            ← Custom inspector for MonoPurrFlux
    PurrFlux.Editor.asmdef       ← Editor assembly definition
```

## License

MIT License - Copyright (c) 2026 Xavier Arpa López Thomas Peter ('xavierarpa')
