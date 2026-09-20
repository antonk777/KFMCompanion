# Changelog

## v1.3.0

- After `KillingFloor.exe` exits, do not start the Steam helper if there is nothing left to send.
- Drop successfully flushed stats from the buffer so a later empty exit does not write to Steam again.

## v1.2.0

- Write stats and achievements to Steam as soon as they arrive over TCP, instead of waiting until Killing Floor exits.
- Keep a final Steam flush after `KillingFloor.exe` closes, so the last updates are not lost.
- If a Steam write is already in progress, send the latest max values as soon as it finishes.
- If a live write fails, retry on the next incoming update and again after the game exits.
- Publish the download as `KFM-Companion-v1.2.0.exe`.

## v1.1.0

- Rename the app to KFM Companion and trigger the download site rebuild after each GitHub Release.
