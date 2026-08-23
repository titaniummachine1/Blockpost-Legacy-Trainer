# Captures

Raw probe output, archived from `BepInEx/captures/`. One file per game run; the plugin never
overwrites a log (a fixed filename with `FileMode.Create` destroyed one early on).

`.gitignore` ignores `*.log` globally but has an explicit exception for this directory, so these
are tracked deliberately. Keep them — re-running a capture costs a game session, and several of
these took multiple attempts to get right.

## Line formats

```
<ms> tx hdr=0xNN op=0xNN <name> : <fields>     outgoing packet, decoded field by field
<ms> rx len=N <hex>                            inbound buffer
<ms> fw <target> <field> <old> -> <new>        numeric field that changed this frame
<ms> # ...                                     probe note (connection state, errors)
<ms> ==== ... ====                             toggle marker
<ms> ---- MARKER #n ----                       manual F8 marker
```

## Index

| File | Content |
|---|---|
| `2026-08-23_02-fieldwatch-player.log` | early run; fieldwatch crashed on `FindObjectOfType` |
| `2026-08-23_04-inventory-dump.log` | inventory / weapon catalog dump |
| `net-20260823-020010.log` | **5533 fieldwatch lines — the run that identified the reload system** (see `PROTOCOL.md` §6) |
| `net-20260823-021230.log` | 1005 tx, movement + hit reports |
| `net-20260823-025026.log` | 1099 tx |
| `net-20260823-025408.log` | 646 tx |
| `net-20260823-030415.log` | **3487 rx — first capture with real inbound traffic** |
| `net-20260823-032006.log` | 1216 rx, 524 tx |
| `net-20260823-04*.log`, `net-20260823-05*.log` | short/aborted runs, some only a couple of lines |

The short runs are kept on purpose: they are cheap, and an empty capture is itself evidence about
when the probe was or wasn't active.
