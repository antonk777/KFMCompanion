# KFM Companion — stats protocol (for agents)

This is the client contract for **Killing Floor** (Steam AppId **1250**).
The game process talks to **KFM Companion** over local TCP. The companion writes each incoming stat/achievement to Steam **as it arrives** while `KillingFloor.exe` is running, then flushes once more after the process exits.

Do **not** use WebSockets. Do **not** send JSON. Do **not** send INI files.

## Connection

| | |
|---|---|
| Host | `127.0.0.1` only |
| Port | `27250` |
| Transport | TCP |
| Encoding | UTF-8 or ASCII |
| Framing | **one command = one line**, terminated by `\n` (`\r\n` is OK) |
| When | Connect after the companion is running (it starts TCP and waits). The game may be launched from Steam or via the companion. |

If connect fails, the companion is not running. Retry a few times; do not block the game forever.

The game **may** keep using its own `steam_api` during play. TCP events are an extra buffer. The companion reads current Steam values and applies a **monotone merge** (never lowers a stat, never locks an already unlocked achievement).

## Command format

```
COMMAND [id] [value]
```

- Command names are case-insensitive (`ACHIEVEMENT` = `achievement`).
- Fields are split on whitespace.
- `id` is the Steamworks **API name** (not the display title). If an id contains spaces, that is allowed; the last token is always the numeric flag/value.
- Send `STORE` after a batch if you want; it does **not** write to Steam by itself. `STAT_*` / `ACHIEVEMENT` already trigger a Steam flush when they arrive. A final flush also runs when the game process exits.

## Commands

### Unlock an achievement

```
ACHIEVEMENT <api_name> 1
```

Example:

```
ACHIEVEMENT ACH_WIN 1
```

- `1` = unlock (written to Steam when this line arrives).
- `0` = ignored on purpose. The companion will **not** lock/clear achievements.
- If Steam already has it unlocked, flush is a no-op.

### Integer stat

```
STAT_INT <api_name> <integer>
```

Example:

```
STAT_INT kills 42
```

Send the **current absolute total** the mutator knows for that id (not a session delta). During the session the companion keeps the **maximum** value seen.

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
| `QUIT` | `OK` | Does **not** exit the companion and does **not** trigger Steam flush. Steam writes happen on each `STAT_*` / `ACHIEVEMENT` and after game exit. |

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
- Each `STAT_*` / `ACHIEVEMENT` line is flushed to Steam when it arrives. You do not throttle Steam yourself.

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

The companion does not add deltas. It stores max(incoming).

## What the companion writes to Steam

While `KillingFloor.exe` is running, each incoming `STAT_*` / `ACHIEVEMENT` triggers a flush (`RequestUserStats`, monotone merge, `StoreStats` only if something changed). If another flush is already running, the latest buffer is written as soon as that one finishes. The buffer is kept (max values stay) so later flushes skip values Steam already has.

If a live flush fails, retry on the next incoming update, and always try again after the game exits.

After the game closes:

1. Stop listening.
2. Wait ~2s so Steam releases the game session.
3. Same merge / `StoreStats` as above.
4. No Windows toast if nothing was sent (0 ach, 0 stats). Toast only if the **final** flush fails.

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
