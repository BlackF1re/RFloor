# Project files — schema 1

## Shared `.rfplan` format

A single UTF-8 JSON file contains the floor plan, survey settings, and measurements. Switching between Plan and Survey changes the interface without converting the file.

```json
{
  "format": "rfloor.plan",
  "schema": 1,
  "name": "My apartment",
  "geometry": {
    "rooms": [], "walls": [], "doors": [],
    "decorations": [], "tapes": []
  },
  "points": [],
  "settings": {"grid": 0.1, "iperfHost": "192.168.2.1", "iperfPort": 5201}
}
```

Coordinates and dimensions use metres. Rooms contain `id`, `name`, `x`, `y`, `w`, and `h`; width and height describe the interior. Walls and openings use endpoint objects `a` and `b`, each containing `x` and `y`. Decorations store `kind`, `name`, position, dimensions, and `filled`; kinds include `rect`, `roundRect`, `ellipse`, `line`, `text`, and `router`. Tapes store endpoints. Optional label offsets and visibility are preserved.

Measurement points store `x`, `y`, `name`, `band`, optional `requested` test flags, `measurementState`, and `measurements`. Results may include `rssi` (dBm), `quality` (%), `pings`, `downloadMbps`, `uploadMbps`, `ssid`, `bssid`, and `frequencyMHz`. Missing results remain absent rather than becoming zero. Heatmaps are regenerated; raster caches are excluded.

`settings` stores measurement flags and targets, grid options, and colour thresholds. Application preferences, including language and shortcuts, are stored separately in the platform's local application data directory under `RFloor/preferences.json`.

The file picker accepts `.rfplan` on every platform, including Android document-provider URIs. Older plan settings are normalized on load; saving preserves the unified project structure. Unsupported schema versions and invalid coordinates are rejected.
