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
Cualquiera → "topic".DispatchNet(data) → Servidor → Broadcast a todos → listeners[topic] invocan handlers
```

- Construido **encima de Broadcasts** de PurrNet
- Topics son **strings** dinámicos (como `"chat/message"`, `"lobby/ready"`)
- Los handlers se registran con `[MethodPurrFlux("topic")]` (auto) o `"topic".StoreNet(handler, true)` (manual)
- **Cualquier MonoPurrFlux puede escuchar cualquier topic** — no importa en qué GameObject esté

---

## Diferencias Fundamentales

| Aspecto | PurrNet RPCs | PurrNet Broadcasts | PurrFlux |
|---|---|---|---|
| **Acoplamiento** | Fuerte: el caller debe tener referencia al script que tiene el RPC | Medio: necesita conocer el struct de datos | Débil: solo necesita conocer el string del topic |
| **Requiere NetworkIdentity** | Sí (excepto Static RPC) | No | Sí (`MonoPurrFlux` hereda `NetworkBehaviour`) |
| **Dirección** | Client→Server, Server→Clients, Server→Client | Client→Server, Server→Clients | Cualquiera→Todos (broadcast implícito) |
| **Serialización** | Codegen automático en compilación | Manual (`IPackedAuto`) | Automática vía `Packer<T>` en runtime |
| **Routing** | Directo al método específico | Por tipo de struct | Por string topic |
| **Ownership** | Configurable (`RequireOwnership`) | No aplica | No tiene concepto de ownership |
| **Awaitable** | Sí (`Task<T>`) | No | No |
| **RPCInfo (sender)** | Sí | Sí (`PlayerID sender`) | No nativo |
| **Performance** | Óptimo (codegen, sin reflection en runtime) | Óptimo | Reflection en suscripción + diccionario lookup |
| **Compatibilidad UniFlux** | Ninguna | Ninguna | Total (comparte lifecycle con `[MethodFlux]`) |

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
"player/request-add".DispatchNet(userId);
```

**Escenario ideal**: Sistemas de chat, notificaciones globales, eventos de UI que cualquier módulo puede escuchar sin dependencias directas.

```csharp
// En cualquier parte del juego (ni siquiera necesita ser NetworkBehaviour)
"notification/achievement".DispatchNet("First Blood!");

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
private void OnScoreChanged(int newScore) => UpdateScoreboard(newScore);

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

    // Evento de RED (llega de cualquier cliente via PurrFlux)
    [MethodPurrFlux("battle/phase-changed")]
    private void OnPhaseChanged(int phase) => UpdatePhaseIndicator(phase);
}
```

No necesitas dos sistemas mentales diferentes. Mismo paradigma de atributo + string key.

**Escenario ideal**: Módulos de UI o managers que reaccionan tanto a eventos locales como de red.

### 5. Topics como Strings = Extensibilidad sin Recompilación

**Qué es**: Los topics son strings puros. Puedes construirlos dinámicamente.

```csharp
// Enviar un evento específico a un "canal" por equipo
$"team/{teamId}/message".DispatchNet("Avancen!");

// Escuchar solo los mensajes de tu equipo
$"team/{myTeamId}/message".StoreNet<string>(OnTeamMessage, true);
```

Con RPCs, el routing es estático (definido por el método y el script). No puedes crear "canales" dinámicos.

**Escenario ideal**: Sistemas de chat con canales, notificaciones por grupo/zona, telemetría filtrada por criterio.

### 6. No Requiere Ownership ni Identity Directa del Publisher

**Qué es**: PurrFlux envía mensajes a través de `NetworkManager.SendToServer`/`SendToAll`, que no valida ownership.

Con `[ServerRpc]`, PurrNet puede validar que solo el **owner** del NetworkIdentity llame al RPC (a menos que uses `RequireOwnership = false`). Esto es seguridad, pero también es una limitación cuando quieres que **cualquier componente** de cualquier jugador publique.

Con PurrFlux, `"topic".DispatchNet(data)` funciona desde cualquier contexto conectado a la red.

**Escenario ideal**: Eventos donde el emisor no es relevante (telemetría, logs de red, sistemas de votación donde cualquier jugador vota).

---

## Cuándo NO usar PurrFlux (usar PurrNet nativo)

| Escenario | Por qué PurrNet nativo es mejor |
|---|---|
| **Request-Response** (cliente pide, servidor responde) | `[ServerRpc]` con Awaitable RPC (`Task<T>`) permite `await` del resultado. PurrFlux no tiene mecanismo de respuesta. |
| **Necesitas saber quién envió** | `RPCInfo info = default` te da `info.sender` gratis. PurrFlux no lo proporciona. |
| **Server authority estricta** | `[ServerRpc(RequireOwnership = true)]` + Network Rules garantizan que solo el owner puede invocar. PurrFlux no tiene validación de ownership. |
| **Comunicación a UN cliente específico** | `[TargetRpc]` envía solo al `PlayerID` target. PurrFlux siempre hace broadcast a todos. |
| **Rendimiento crítico** (miles de mensajes/seg) | PurrNet usa codegen IL = cero reflection en runtime. PurrFlux usa diccionarios + reflection en suscripción. |
| **Datos complejos con delta sync** | SyncVar, SyncList, SyncDictionary de PurrNet manejan esto nativamente. |
| **Genéricos tipados** | Generic RPC de PurrNet mantiene type safety en compilación. PurrFlux pierde type safety al usar strings. |

---

## Caso de Estudio: RequestAddPlayer (el circuito actual)

### Con PurrFlux
```csharp
// User.cs — publica sin saber quién escucha
NetworkService.Messaging.RequestAddPlayer(userId);
// → internamente: "RequestAddPlayer".DispatchNet(userId);

// NetworkMessagingModule.cs — escucha por topic
[MethodPurrFlux(nameof(RequestAddPlayer))]
private void RequestAddPlayer(ulong userId)
{
    if (!isServer) return;  // Guard MANUAL
    NetworkService.Player.TryAddPlayer(userId);
}
```

**Problema**: PurrFlux hace broadcast a TODOS los clientes. Cada cliente recibe `RequestAddPlayer`, y solo el servidor actúa. Los demás clientes ejecutan `if (!isServer) return;` innecesariamente — es waste de bandwidth y procesamiento.

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

**Ventaja**: El mensaje viaja Client→Server directamente. Ningún otro cliente lo recibe. Más eficiente, más seguro.

**Veredicto para este caso**: PurrNet nativo es mejor. Es un request unidireccional Client→Server que no necesita broadcast.

---

## Cuándo SÍ Brilla PurrFlux (Escenarios Recomendados)

### 1. Chat Global
```csharp
// Desde cualquier parte
$"chat/{channel}".DispatchNet(new ChatMessage(senderName, text));

// Cualquier UI de chat escucha
[MethodPurrFlux("chat/global")]
private void OnGlobalChat(ChatMessage msg) => AppendMessage(msg);
```

### 2. Sistema de Eventos del Juego (Game Events)
```csharp
// Servidor notifica a todos sobre eventos del mundo
"world/weather-changed".DispatchNet("rain");
"world/boss-spawned".DispatchNet(bossId);
"world/zone-unlocked".DispatchNet(zoneIndex);

// Múltiples módulos independientes reaccionan
// WeatherVFX, MusicManager, MinimapUI, QuestTracker...
```

### 3. Debug/Telemetría en Red
```csharp
// Cualquier cliente puede emitir telemetría
"debug/performance".DispatchNet(new PerfData(fps, ping, memory));

// Un módulo de monitoring en el host captura todo
[MethodPurrFlux("debug/performance")]
private void OnPerfReport(PerfData data) { /* log */ }
```

### 4. Sistema de Votación / Encuestas
```csharp
// Cualquier jugador vota
"vote/kick".DispatchNet(targetPlayerId);

// El módulo de votación en todos los clientes actualiza el conteo visual
[MethodPurrFlux("vote/kick")]
private void OnVoteReceived(ulong targetId) => UpdateVoteCount(targetId);
```

### 5. Sincronización de Estado del Lobby (complementario al matchmaking)
```csharp
"lobby/player-ready".DispatchNet(playerId);
"lobby/settings-changed".DispatchNet(newSettings);
"lobby/countdown-started".DispatchNet();
```

---

## Resumen: Guía de Decisión Rápida

```
¿El mensaje necesita ir SOLO al servidor?
  → Sí → [ServerRpc]

¿El servidor necesita enviar a UN cliente específico?
  → Sí → [TargetRpc]

¿Necesitas esperar una respuesta (request-response)?
  → Sí → Awaitable [ServerRpc] con Task<T>

¿Es un evento global donde TODOS deben enterarse?
  → ¿Los listeners son siempre los mismos scripts?
    → Sí → [ObserversRpc]
    → No (múltiples módulos independientes) → PurrFlux ✓

¿Quieres suscripción dinámica a "canales" por string?
  → PurrFlux ✓

¿Necesitas mezclar eventos locales + red en el mismo script?
  → PurrFlux ✓ (UniFlux + PurrFlux)

¿Performance es crítico (muchos mensajes por segundo)?
  → PurrNet nativo (codegen, zero reflection)
```

---

## Conclusión

PurrFlux **no compite** con PurrNet — lo **complementa** en un nicho específico: **eventos globales desacoplados donde el publisher no conoce a los subscribers**. Es el equivalente de red de un event bus local.

Para comunicación **estructurada** (client→server, server→client, request-response), PurrNet nativo es superior en seguridad, performance y ergonomía.

La combinación ideal es usar **PurrNet nativo para la lógica de juego** y **PurrFlux para eventos observacionales** (notificaciones, logging, UI reactiva, sistemas que solo "escuchan").
