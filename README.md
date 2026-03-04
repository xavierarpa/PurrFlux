# PurrFlux

Networked event system built on top of **PurrNet** and **UniFlux**. Lets you publish and subscribe to topics across the network using the same string-key pattern as UniFlux, but messages are automatically serialized, sent through PurrNet, and dispatched to all connected clients.

## Requirements

- **Unity 2022.3+**
- **PurrNet** (networking layer)
- **UniFlux** (local event bus / attribute dispatching)

## Architecture

```
Publisher ──DispatchNet──► PurrFluxUtils ──PurrNet──► Server ──Broadcast──► All Clients
                                                                              │
                                                                listeners dict / Deserialize
                                                                              │
                                                                        ◄── Handlers
```

- **Client publishes** → message is sent to server via PurrNet.
- **Server receives** → rebroadcasts to all clients.
- **Client receives** → looks up topic in listeners, deserializes payload, invokes handlers.

## Quick Start

### 1. Manual subscription (`StoreNet`)

Subscribe and unsubscribe to network topics directly, similar to UniFlux's `Store`.

```csharp
using PurrFlux;
using UnityEngine;

public class ChatListener : MonoBehaviour
{
    private void OnEnable()
    {
        // Typed topic (Action<T>)
        "chat/message".StoreNet<string>(OnChatMessage, true);

        // Parameterless topic (Action)
        "chat/ping".StoreNet(OnPing, true);
    }

    private void OnDisable()
    {
        "chat/message".StoreNet<string>(OnChatMessage, false);
        "chat/ping".StoreNet(OnPing, false);
    }

    private void OnChatMessage(string message)
    {
        Debug.Log($"Chat: {message}");
    }

    private void OnPing()
    {
        Debug.Log("Ping received!");
    }
}
```

### 2. Attribute-based subscription (`MonoPurrFlux` + `[MethodPurrFlux]`)

Inherit from `MonoPurrFlux` to get automatic subscription lifecycle. Mark handler methods with `[MethodPurrFlux("topic")]` — they are discovered by reflection and subscribed on `OnEnable` / unsubscribed on `OnDisable`.

```csharp
using PurrFlux;
using UnityEngine;

public class LobbyHandler : MonoPurrFlux
{
    [MethodPurrFlux("lobby/join")]
    private void OnPlayerJoined(string playerName)
    {
        Debug.Log($"{playerName} joined the lobby");
    }

    [MethodPurrFlux("lobby/ready")]
    private void OnAllReady()
    {
        Debug.Log("All players ready!");
    }
}
```

> `MonoPurrFlux` inherits from `NetworkBehaviour` (PurrNet), so the component must be on a networked object.

### 3. Publishing messages

```csharp
// Send a typed message
"chat/message".DispatchNet("Hello everyone!");

// Send a parameterless event
"lobby/ready".DispatchNet();
```

### 4. Mixing with UniFlux attributes

`MonoPurrFlux` calls both `Utils.SubscribeAttributes` (UniFlux) and `PurrFluxUtils.SubscribeAttributes`, so you can use standard UniFlux attributes (`[MethodFlux]`, `[StateFlux]`) alongside `[MethodPurrFlux]` on the same class:

```csharp
using PurrFlux;
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

    // Networked PurrFlux event
    [MethodPurrFlux("game/score")]
    private void OnScoreUpdate(int newScore)
    {
        Debug.Log($"Score updated: {newScore} (from network)");
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
    private void OnPlayerUpdate(string name)
    {
        playerName.value = name;
    }
}
```

## API Reference

### Extension Methods on `string`

| Method | Description |
|--------|-------------|
| `"topic".DispatchNet<T>(T data)` | Sends a typed message to all clients via the server |
| `"topic".DispatchNet()` | Sends a parameterless event |
| `"topic".StoreNet<T>(Action<T>, bool)` | Subscribe (`true`) or unsubscribe (`false`) a typed handler |
| `"topic".StoreNet(Action, bool)` | Subscribe/unsubscribe a parameterless handler |

### Classes

| Type | Description |
|------|-------------|
| `MonoPurrFlux` | Base class (`NetworkBehaviour`) with automatic attribute subscription lifecycle |
| `MethodPurrFluxAttribute` | Marks a method for automatic networked subscription via topic key |
| `PurrFluxUtils` | Static utility class that manages listeners, serialization, and network transport |

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
