# KFM Launcher — stats protocol (for agents)

This is the client contract for **Killing Floor** (Steam AppId **1250**).
The game process talks to **KFM Launcher** over local TCP. The launcher buffers events while the game is running, then writes to Steam **only after `KillingFloor.exe` exits**.

Do **not** use WebSockets. Do **not** send JSON. Do **not** send INI files.

## Connection

| | |
|---|---|
| Host | `127.0.0.1` only |
| Port | `27250` |
| Transport | TCP |
| Encoding | UTF-8 or ASCII |
| Framing | **one command = one line**, terminated by `\n` (`\r\n` is OK) |
| When | Connect after the launcher is running (it starts TCP, then launches the game) |

If connect fails, the launcher is not running. Retry a few times; do not block the game forever.

The game **may** keep using its own `steam_api` during play. TCP events are an extra buffer. After exit the launcher reads current Steam values and applies a **monotone merge** (never lowers a stat, never locks an already unlocked achievement).

## Command format

```
COMMAND [id] [value]
```

- Command names are case-insensitive (`ACHIEVEMENT` = `achievement`).
- Fields are split on whitespace.
- `id` is the Steamworks **API name** (not the display title). If an id contains spaces, that is allowed; the last token is always the numeric flag/value.
- Send `STORE` after a batch if you want; it does **not** write to Steam immediately. It only acknowledges. Flush to Steam happens when the game process exits.

## Commands

### Unlock an achievement

```
ACHIEVEMENT <api_name> 1
```

Example:

```
ACHIEVEMENT ACH_WIN 1
```

- `1` = unlock (buffered until game exit).
- `0` = ignored on purpose. The launcher will **not** lock/clear achievements.
- If Steam already has it unlocked, flush is a no-op.

### Integer stat

```
STAT_INT <api_name> <integer>
```

Example:

```
STAT_INT kills 42
```

During the session the launcher keeps the **maximum** value seen for that id.

On flush: write to Steam only if `buffered > current Steam value`.

### Float stat

```
STAT_FLOAT <api_name> <float>
```

Example:

```
STAT_FLOAT playtime 12.5
```

Same max / monotone rules as int. Use `.` as decimal separator (invariant).

### Optional housekeeping

```
STORE
PING
QUIT
```

| Command | Reply | Meaning |
|---|---|---|
| `STORE` | `OK BUFFERED` | Session dirty flag only. **Not** Steam `StoreStats`. |
| `PING` | `PONG` | Health check. |
| `QUIT` | `OK` | Does **not** exit the launcher and does **not** trigger Steam flush. Flush is tied to `KillingFloor` process exit. |

## Replies (one line)

```
OK
OK BUFFERED
PONG
ERR <message>
```

Treat any line starting with `ERR` as failure of that command. Keep sending later events; one bad line must not kill the socket.

## Unreal Engine 2.5 notes

Use `TcpLink` (or a native socket) to `127.0.0.1:27250`.

- Send ASCII lines ending with `Chr(10)` (`\n`).
- Read replies the same way (one line).
- Do **not** implement HTTP or a WebSocket handshake.
- Prefer sending events when they happen (achievement unlocked, stat increased). Also OK to dump a snapshot on map change / disconnect / game end, as long as values are **absolute totals** (or at least non-decreasing), not deltas.

Good:

```
STAT_INT kills 10
STAT_INT kills 15
ACHIEVEMENT ACH_SOME_NAME 1
```

Bad:

```
STAT_INT kills +1
```

The launcher does not add deltas. It stores max(incoming).

## What the launcher does after the game closes

1. Stop listening.
2. Wait ~2s so Steam releases the game session.
3. `Initialize(1250)`, `RequestUserStats`.
4. For each buffered achievement: unlock only if Steam still has it locked.
5. For each buffered stat: `SetStat` only if new value is **greater**.
6. `StoreStats` only if something actually changed.
7. No Windows toast if nothing was sent (0 ach, 0 stats).

## IDs

`api_name` **must** match Killing Floor Steamworks names for AppId 1250 (the API Name in Steamworks / `UserGameStatsSchema`, not the English title).

If you do not know the names, look them up from KF’s Steam stats schema or from SAM-style UserGameStatsSchema for app 1250. Sending a made-up id is buffered but will be skipped on flush when Steam `GetAchievement` / `GetStatValue` fails.

## Minimal client algorithm

1. Open TCP `127.0.0.1:27250`.
2. Optionally `PING` and expect `PONG`.
3. On unlock: `ACHIEVEMENT <id> 1\n`
4. On stat change: `STAT_INT <id> <absolute>\n` or `STAT_FLOAT <id> <absolute>\n`
5. Keep the socket open for the session, or reconnect if it drops.
6. Exit the game normally. Do not depend on `QUIT` for Steam upload.

## Copy-paste examples

```
PING
ACHIEVEMENT ACH_WIN 1
STAT_INT kills 100
STAT_FLOAT playtime 3600.0
STORE
```
