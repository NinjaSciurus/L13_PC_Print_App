# L13 / DP-L13 protocol notes

Reverse-engineered against a **Luck Jingle L13** (`DP-L13`, firmware **V3.08**),
cross-checked with the atctwo DP-L13 teardown and the 0xMH `fichero-printer` SDK
notes (see the acknowledgements in the README). Everything here is confirmed on
hardware unless noted.

## Transport

- **Classic Bluetooth RFCOMM** (Serial Port Profile), **channel 1**.
- SPP service class UUID `00001101-0000-1000-8000-00805F9B34FB`.
- After connecting, the socket is a raw byte stream; commands are sent verbatim.
- The device also exposes BLE UART services and a USB printer interface, but this
  project uses RFCOMM only.

## Hardware / geometry

- Print head: **96 dots wide = 12 bytes/row**, **203 DPI** (8 dots/mm) ≈ 12 mm.
- Stock labels are 14 × 30 mm, but **30 mm is the pitch** (body + inter-label gap).
  The printable **body is ~28 mm ≈ 224 dots**.
- The 96-dot head sits ~1 mm inside each 14 mm side edge (fixed hardware).

## Info / status queries

| Command       | Meaning                | Response |
|---------------|------------------------|----------|
| `10 FF 20 F0` | Model                  | ASCII, e.g. `DP-L13` |
| `10 FF 20 F1` | Firmware               | ASCII, e.g. `V3.08` |
| `10 FF 20 F2` | Serial                 | ASCII |
| `10 FF 50 F1` | Battery                | `[status, percent]`, e.g. `00 50` = 80 % |
| `10 FF 40`    | Status                 | 1-byte bitmask (below) |
| `10 FF 11`    | Density / print params | 3 bytes, e.g. `01 0A 01` |
| `10 FF 13`    | Auto-shutdown minutes  | e.g. `14` = 20 min |

`10 FF 20 F3` does **not** respond (it is not a MAC query).

### Status bitmask (`10 FF 40`)

`0x00` = ready. Bits: `0x01` printing · `0x02` cover open · `0x04` out of paper ·
`0x08` low battery · `0x20` charging · `0x40` overheated.

## Config

| Command          | Meaning            | Parameter |
|------------------|--------------------|-----------|
| `10 FF 10 00 nn` | Set density        | 0 light / 1 medium / 2 thick |
| `10 FF 12 HH LL` | Set shutdown time  | big-endian minutes |
| `10 FF 84 nn`    | Set paper type     | 0 gap/label / 1 black-mark / 2 continuous |

Note: `10 FF 84 00` (gap mode) makes **no difference** to the seek on V3.08 and
adds ~2 s latency, so it is off by default.

## Print sequence

```
10 FF 40                     query status (expect 00)
10 FF F1 03                  enable print mode (L13 / Lujiang class)
00 x12                       wake-up (12 NUL bytes)
1D 76 30 00 xL xH yL yH …    GS v 0 raster band + data   (repeat, ≤ 24 rows/band)
0C                           form feed: seek to next label border
1B 4A nn                     tear feed (nn dots; ~6 mm on stock labels)
10 FF F1 45                  stop print job
```

- **Enable/stop are device-class specific.** L13/Lujiang uses `10 FF F1 03` / `10 FF F1 45`.
  (The AiYin D11s/D12 use `10 FF FE 01` / `10 FF FE 45`.) Wrong pair = data accepted
  but nothing prints.
- Split the raster into **bands of ≤ 24 rows**; a single oversized `GS v 0` can be dropped.
- Consecutive bands print contiguously (each advances the paper by its own rows).

### Raster format (`GS v 0`)

`1D 76 30 mm xL xH yL yH <data>`, where `mm` = 0 (normal), `xL xH` = width in **bytes**
little-endian (`0C 00` = 12 = 96 px), `yL yH` = height in **rows** little-endian.
Each byte is 8 pixels, **MSB = leftmost dot**, `1` = black.

## Label-gap detection

- The IR sensor is a **paper-present** sensor only (`10 FF 40` bit `0x04`); there is
  no calibrate-before-print handshake. If a label is mis-positioned when you start,
  it prints wherever the paper happens to be.
- **`0C` (and `1D 0C`) seek to the next label border** using the sensor. Confirmed
  by mis-aligning a label and watching it land exactly on the border (returns `OK`
  = `4F 4B`). `10 0C` is a no-op on V3.08.
- The seek stops with the next border **under the head**. The tear edge is a few mm
  downstream, so a small extra feed (`1B 4A nn`, ~6 mm here) advances it to tearable.
- The firmware **retracts the paper a little before each print** (a visible
  pull-back) to re-seat the label at the print line, so the device *can* reverse-feed,
  even though the sibling SDK's reverse-feed command (`1F 11 11 nn`) drew no response
  on V3.08 and this project never drives retraction itself. Combined with the
  post-print border seek (which re-references the actual gap each cycle), this is why
  **seek + a 6 mm tear feed** prints cleanly on stock labels without a creeping offset:
  the tear feed advances the finished label to tearable, and the next cycle's seek +
  retract re-seat the following label. This matches the vendor iOS app.

## Orientation

The head prints one 96-dot row at a time as paper feeds. Text is rendered upright,
then rotated 270° so its height fills the 96-dot head and the word runs along the
label length. Empirically (from a known test pattern) row 0 = the leading/exit edge
and dot 0 = the far side; rotating the packed bitmap back 90° CW reproduces the
physical label held with the first-out edge on the right, which is how the preview
is drawn.
