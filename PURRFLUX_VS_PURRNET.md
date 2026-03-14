# PurrFlux vs PurrNet Nativo — Análisis Profundo

## Resumen Ejecutivo

**PurrNet** ofrece RPCs (ServerRpc, ObserversRpc, TargetRpc) y Broadcasts como herramientas nativas para comunicación en red. Estas son **suficientes para el 90% de los casos**.

**PurrFlux** es una capa construida *sobre* PurrNet que añade un **sistema de eventos por topics** (pub/sub) combinado con **UniFlux**. No reemplaza a PurrNet — lo extiende con un paradigma diferente.

La pregunta no es "cuál es mejor", sino **cuándo cada uno brilla**.

---

## Cómo funciona cada uno

### PurrNet Nativo (RPCs)

```
Cliente llama método → [ServerRpc] → Se ejecuta SOLO en servidor
Servidor llama método → [ObserversRpc] → Se ejecuta en TODOS los clientes
Servidor llama método → [TargetRpc(target)] → Se ejecuta en UN cliente específico
```

- **Requiere** heredar de `NetworkBehaviour` o `NetworkIdentity`
- **Cada RPC está atado a una instancia de red específica** (un GameObject con NetworkIdentity)
- **Ownership** controlable: `RequireOwnership = true/false`
- **PurrNet hace codegen** en compilación: genera IL para serialización, routing de RPCs, etc.
- **Tipos avanzados**: Generic RPC, Static RPC, Awaitable RPC (con `Task<T>`)

### PurrNet Nativo (Broadcasts)

```
Cliente → NetworkManager.SendToServer(data) → Servidor recibe
Servidor → NetworkManager.SendToAll(data) → Todos los clientes reciben
```

- **NO requiere** NetworkIdentity
- Usa structs `IPackedAuto` como mensajes
- Subscribe/Unsubscribe manual vía `NetworkManager.Subscribe<T>()` / `Unsubscribe<T>()`
- Es la **low-level API** de mensajería de PurrNet

### PurrFlux (Topic-based Pub/Sub)

```
Cualquiera → "topic".DispatchNet(data, serverOnly) → PurrNet → Server ─┬─ serverOnly=true  → Invoke on server
                                                                        └─ serverOnly=false → Broadcast → All Clients
                                                                                                              │
                                                                                               listeners[topic](PlayerID sender, data)
```

- Construido **encima de Broadcasts** de PurrNet
- Topics son **strings** dinámicos (como `"chat/message"`, `"lobby/ready"`)
- **Sender info incluida**: todos los handlers reciben `PlayerID sender` internamente
- **Server-only routing**: con `serverOnly: true`, el mensaje solo se procesa en el servidor sin rebroadcast
- Los handlers se registran con `[MethodPurrFlux("topic")]` (auto) o `"topic".StoreNet(handler, true)` (manual)
- **Cuatro formas de handler**: `Action`, `Action<T>`, `Action<PlayerID>`, `Action<PlayerID, T>`
- **Cualquier MonoPurrFlux puede escuchar cualquier topic** — no importa en qué GameObject esté

---

## Diferencias Fundamentales

| Aspecto | PurrNet RPCs | PurrNet Broadcasts | PurrFlux |
|---|---|---|---|
| **Acoplamiento** | Fuerte: el caller debe tener referencia al script que tiene el RPC | Medio: necesita conocer el struct de datos | Débil: solo necesita conocer el string del topic |
| **Requiere NetworkIdentity** | Sí (excepto Static RPC) | No | Sí (`MonoPurrFlux` hereda `NetworkBehaviour`) |
| **Dirección** | Client→Server, Server→Clients, Server→Client | Client→Server, Server→Clients | Broadcast a todos **o** solo al servidor (`serverOnly`) |
| **Sender info** | Sí (`RPCInfo info = default`) | Sí (`PlayerID sender`) | Sí (`PlayerID sender` en handlers) |
| **Serialización** | Codegen automático en compilación | Manual (`IPackedAuto`) | Automática vía `Packer<T>` en runtime |
| **Routing** | Directo al método específico | Por tipo de struct | Por string topic |
| **Ownership** | Configurable (`RequireOwnership`) | No aplica | No tiene concepto de ownership |
| **Awaitable** | Sí (`Task<T>`) | No | No |
| **Server-only** | Implícito con `[ServerRpc]` | Manual (solo `SendToServer`) | Sí (`serverOnly: true`) |
| **Performance** | Óptimo (codegen, sin reflection en runtime) | Óptimo | Reflection en suscripción + diccionario lookup |
| **Compatibilidad UniFlux** | Ninguna | Ninguna | Total (comparte lifecycle con `[MethodFlux]`) |
| **Channel configurable** | Sí | Sí | Sí (`Channel` param en `DispatchNet`) |

---

## Ventajas Específicas de PurrFlux

### 1. Desacoplamiento Total por Topics

**Qué es**: Publishers y subscribers no necesitan conocerse. Solo comparten un string.

**PurrNet RPC** requiere que el caller invoque un método concreto en un script concreto:
```csharp
// El caller NECESITA referencia a NetworkMessagingModule
networkMessagingModule.ServerRpc_RequestAddPlayer(userId);
```

**PurrFlux** solo necesita el topic:
```csharp
// El caller NO necesita saber quién escucha
"player/request-add".DispatchNet(userId, serverOnly: true);
```

**Escenario ideal**: Sistemas de chat, notificaciones globales, eventos de UI que cualquier módulo puede escuchar sin dependencias directas.

```csharp
// En cualquier parte del juego
"notification/achievement".DispatchNet("First Blood!", serverOnly: false);

// En un módulo de UI completamente separado
[MethodPurrFlux("notification/achievement")]
private void OnAchievement(string name) => ShowAchievementPopup(name);

// En un módulo de audio completamente separado
[MethodPurrFlux("notification/achievement")]
private void OnAchievement(string name) => PlayAchievementSound();
```

### 2. Múltiples Listeners para un Mismo Evento

**Qué es**: Muchos objetos diferentes pueden suscribirse al mismo topic simultáneamente.

Con RPCs, un `[ObserversRpc]` ejecuta el método en **todas las instancias de red de ese mismo script**. Pero si quieres que *otros scripts diferentes* reaccionen, necesitas cadenas de eventos explícitas.

Con PurrFlux, **cualquier MonoPurrFlux en cualquier GameObject** puede escuchar el mismo topic:

```csharp
// ScoreboardUI.cs
[MethodPurrFlux("game/score-changed")]
private void OnScoreChanged(PlayerID sender, int newScore) => UpdateScoreboard(sender, newScore);

// SoundManager.cs  
[MethodPurrFlux("game/score-changed")]
private void OnScoreChanged(int newScore) => PlayScoreSound();

// ParticleEffects.cs
[MethodPurrFlux("game/score-changed")]  
private void OnScoreChanged(int newScore) => SpawnConfetti();
```

**Escenario ideal**: Eventos globales que múltiples sistemas deben observar (cambios de estado del juego, resultados de combate, transiciones de fase).

### 3. Suscripción Dinámica en Runtime

**Qué es**: Puedes agregar y quitar listeners dinámicamente con `StoreNet`.

```csharp
// Un objeto que solo escucha cuando está en una zona específica
private void OnTriggerEnter(Collider other)
{
    "zone/ambient-event".StoreNet<string>(OnAmbientEvent, true);
}

private void OnTriggerExit(Collider other)
{
    "zone/ambient-event".StoreNet<string>(OnAmbientEvent, false);
}
```

RPCs no permiten esto — un `[ServerRpc]` siempre está disponible si el objeto existe en red.

**Escenario ideal**: Sistemas contextuales donde solo algunos objetos deben reaccionar dependiendo del estado del juego.

### 4. Integración Nativa con UniFlux (Ecosistema Local + Red)

**Qué es**: En un mismo `MonoPurrFlux` puedes mezclar eventos locales (`[MethodFlux]`) y eventos de red (`[MethodPurrFlux]`) con el mismo patrón y lifecycle.

```csharp
public class BattleUI : MonoPurrFlux
{
    // Evento LOCAL (solo esta máquina, via UniFlux)
    [MethodFlux("ui/refresh")]
    private void OnRefreshUI() => RefreshAllPanels();

    // Evento de RED (llega de cualquier cliente via PurrFlux, con sender info)
    [MethodPurrFlux("battle/phase-changed")]
    private void OnPhaseChanged(PlayerID sender, int phase) => UpdatePhaseIndicator(phase);
}
```

No necesitas dos sistemas mentales diferentes. Mismo paradigma de atributo + string key.

**Escenario ideal**: Módulos de UI o managers que reaccionan tanto a eventos locales como de red.

### 5. Topics como Strings = Extensibilidad sin Recompilación

**Qué es**: Los topics son strings puros. Puedes construirlos dinámicamente.

```csharp
// Enviar un evento específico a un "canal" por equipo
$"team/{teamId}/message".DispatchNet("Avancen!", serverOnly: false);

// Escuchar solo los mensajes de tu equipo
$"team/{myTeamId}/message".StoreNet<string>(OnTeamMessage, true);
```

Con RPCs, el routing es estático (definido por el método y el script). No puedes crear "canales" dinámicos.

**Escenario ideal**: Sistemas de chat con canales, notificaciones por grupo/zona, telemetría filtrada por criterio.

### 6. Server-Only Messaging

**Qué es**: Con `serverOnly: true`, el mensaje viaja Client→Server y **no** se rebroadcastea a los demás clientes. Los listeners solo se invocan en el servidor.

```csharp
// Cliente envía un request solo al servidor
"server/request-spawn".DispatchNet(spawnData, serverOnly: true);

// Solo el servidor ejecuta este handler
[MethodPurrFlux("server/request-spawn")]
private void OnSpawnRequest(PlayerID sender, SpawnData data)
{
    if (!isServer) return;
    SpawnEntity(sender, data);
}
```

Esto cubre parte del caso de uso de `[ServerRpc]` cuando no necesitas ownership validation ni Awaitable responses.

**Escenario ideal**: Requests unidireccionales Client→Server donde no importa el ownership y no se necesita respuesta tipada.

---

## Cuándo NO usar PurrFlux (usar PurrNet nativo)

| Escenario | Por qué PurrNet nativo es mejor |
|---|---|
| **Request-Response** (cliente pide, servidor responde) | `[ServerRpc]` con Awaitable RPC (`Task<T>`) permite `await` del resultado. PurrFlux no tiene mecanismo de respuesta. |
| **Server authority estricta con ownership** | `[ServerRpc(RequireOwnership = true)]` + Network Rules garantizan que solo el owner puede invocar. PurrFlux no tiene validación de ownership. |
| **Comunicación a UN cliente específico** | `[TargetRpc]` envía solo al `PlayerID` target. PurrFlux envía a todos (`serverOnly: false`) o solo al servidor (`serverOnly: true`), no a un cliente específico. |
| **Rendimiento crítico** (miles de mensajes/seg) | PurrNet usa codegen IL = cero reflection en runtime. PurrFlux usa diccionarios + reflection en suscripción. |
| **Datos complejos con delta sync** | SyncVar, SyncList, SyncDictionary de PurrNet manejan esto nativamente. |
| **Genéricos tipados** | Generic RPC de PurrNet mantiene type safety en compilación. PurrFlux pierde type safety al usar strings. |

---

## Caso de Estudio: RequestAddPlayer (el circuito actual)

### Con PurrFlux (serverOnly: true)
```csharp
// User.cs — publica sin saber quién escucha
"RequestAddPlayer".DispatchNet(userId, serverOnly: true);

// NetworkMessagingModule.cs — escucha por topic
[MethodPurrFlux(nameof(RequestAddPlayer))]
private void RequestAddPlayer(PlayerID sender, ulong userId)
{
    if (!isServer) return;
    NetworkService.Player.TryAddPlayer(userId);
}
```

Con `serverOnly: true`, el mensaje viaja Client→Server directamente y **no** se rebroadcastea. El guard `if (!isServer)` es redundante pero seguro. Además, `sender` identifica quién hizo la solicitud.

### Con PurrNet [ServerRpc]
```csharp
// User.cs
NetworkService.Messaging.RequestAddPlayerRpc(userId);
// → internamente llama: ServerRpc_RequestAddPlayer(userId);

// NetworkMessagingModule.cs
[ServerRpc(RequireOwnership = false)]
private void ServerRpc_RequestAddPlayer(ulong userId, RPCInfo info = default)
{
    // Se ejecuta SOLO en servidor. Punto.
    // Además info.sender te dice quién lo pidió.
    NetworkService.Player.TryAddPlayer(userId);
}
```

**Ventaja de PurrNet nativo**: Codegen garantiza que el método SOLO se ejecuta en servidor. No hay broadcast innecesario a otros clientes. Ownership validation integrada si se necesita.

**Ventaja de PurrFlux**: Desacoplamiento total — `User.cs` no necesita referencia a `NetworkMessagingModule`. Con `serverOnly: true` el bandwidth es equivalente (no hay broadcast a otros clientes).

**Veredicto para este caso**: PurrNet nativo sigue siendo preferible por las garantías en compilación (codegen). PurrFlux con `serverOnly: true` es una alternativa viable si el desacoplamiento es prioritario.

---

## Cuándo SÍ Brilla PurrFlux (Escenarios Recomendados)

### 1. Chat Global
```csharp
// Desde cualquier parte
$"chat/{channel}".DispatchNet(new ChatMessage(senderName, text), serverOnly: false);

// Cualquier UI de chat escucha (con sender info)
[MethodPurrFlux("chat/global")]
private void OnGlobalChat(PlayerID sender, ChatMessage msg) => AppendMessage(sender, msg);
```

### 2. Sistema de Eventos del Juego (Game Events)
```csharp
// Servidor notifica a todos sobre eventos del mundo
"world/weather-changed".DispatchNet("rain", serverOnly: false);
"world/boss-spawned".DispatchNet(bossId, serverOnly: false);
"world/zone-unlocked".DispatchNet(zoneIndex, serverOnly: false);

// Múltiples módulos independientes reaccionan
// WeatherVFX, MusicManager, MinimapUI, QuestTracker...
```

### 3. Debug/Telemetría en Red
```csharp
// Cualquier cliente puede emitir telemetría (solo al servidor)
"debug/performance".DispatchNet(new PerfData(fps, ping, memory), serverOnly: true);

// Un módulo de monitoring en el host captura todo (con sender info)
[MethodPurrFlux("debug/performance")]
private void OnPerfReport(PlayerID sender, PerfData data) { /* log per-player perf */ }
```

### 4. Sistema de Votación / Encuestas
```csharp
// Cualquier jugador vota (solo al servidor para validar)
"vote/kick".DispatchNet(targetPlayerId, serverOnly: true);

// El servidor valida y rebroadcastea el resultado
[MethodPurrFlux("vote/kick")]
private void OnVoteReceived(PlayerID sender, ulong targetId)
{
    if (!isServer) return;
    RegisterVote(sender, targetId);
    // Rebroadcast resultado validado
    "vote/kick-result".DispatchNet(new VoteResult(targetId, currentCount), serverOnly: false);
}
```

### 5. Sincronización de Estado del Lobby (complementario al matchmaking)
```csharp
"lobby/player-ready".DispatchNet(playerId, serverOnly: false);
"lobby/settings-changed".DispatchNet(newSettings, serverOnly: false);
"lobby/countdown-started".DispatchNet(serverOnly: false);
```

### 6. Client→Server Requests Desacoplados
```csharp
// Cliente solicita algo al servidor sin necesitar referencia al handler
"server/craft-item".DispatchNet(itemId, serverOnly: true);

// Servidor procesa con sender info
[MethodPurrFlux("server/craft-item")]
private void OnCraftRequest(PlayerID sender, string itemId)
{
    if (!isServer) return;
    ProcessCraft(sender, itemId);
}
```

---

## Resumen: Guía de Decisión Rápida

```
¿El mensaje necesita ir SOLO al servidor con ownership validation?
  → Sí → [ServerRpc]

¿El mensaje necesita ir SOLO al servidor sin ownership validation?
  → ¿Necesitas respuesta (request-response)?
    → Sí → Awaitable [ServerRpc] con Task<T>
    → No → [ServerRpc(RequireOwnership=false)] O PurrFlux con serverOnly: true ✓

¿El servidor necesita enviar a UN cliente específico?
  → Sí → [TargetRpc]

¿Es un evento global donde TODOS deben enterarse?
  → ¿Los listeners son siempre los mismos scripts?
    → Sí → [ObserversRpc]
    → No (múltiples módulos independientes) → PurrFlux con serverOnly: false ✓

¿Quieres suscripción dinámica a "canales" por string?
  → PurrFlux ✓

¿Necesitas mezclar eventos locales + red en el mismo script?
  → PurrFlux ✓ (UniFlux + PurrFlux)

¿Necesitas saber quién envió el mensaje?
  → PurrNet nativo: RPCInfo info = default
  → PurrFlux: Action<PlayerID> o Action<PlayerID, T> ✓ (ambos lo soportan)

¿Performance es crítico (muchos mensajes por segundo)?
  → PurrNet nativo (codegen, zero reflection)
```

---

## Conclusión

PurrFlux **no compite** con PurrNet — lo **complementa** en un nicho específico: **eventos globales desacoplados donde el publisher no conoce a los subscribers**. Es el equivalente de red de un event bus local.

Con la adición de `serverOnly` y `PlayerID sender`, PurrFlux cubre más casos de uso que antes: mensajes Client→Server sin broadcast innecesario, e identificación del emisor sin necesidad de RPCInfo.

Para comunicación **estructurada** (ownership validation, targeted delivery a un cliente específico, request-response con `Task<T>`), PurrNet nativo es superior en seguridad, performance y ergonomía.

La combinación ideal es usar **PurrNet nativo para la lógica de juego con authority** y **PurrFlux para eventos observacionales y requests desacoplados** (notificaciones, logging, UI reactiva, sistemas que escuchan, telemetría, chat).
