<p align="center"><img src="assets/icon_256.png" width="128" alt="True Flasher icon"></p>

<h1 align="center">True Flasher</h1>

<p align="center"><b>Find the real capacity of fake USB flash drives, quickly and accurately, then make them safe to use.</b><br><i>Made by Adromedary</i><br><br>Windows (desktop app + console) &nbsp;·&nbsp; Linux (terminal app, x64 &amp; ARM64)</p>

That "1 TB" flash drive for $10 usually holds 8–64 GB of real flash. The controller is reprogrammed to report a fake size, and once the real flash is full it silently **overwrites your older files** or throws new data away. Your files look fine until you open them.

True Flasher measures how much data the drive really keeps. It writes self-identifying data, re-reads it, and counts what survived. You don't have to fill the whole fake 1 TB, so it finishes in time proportional to the **real** size.

```
========================== TRUE FLASHER REPORT ==========================
Drive          : Disk 1  Generic Flash Disk  (USB)
Claimed size   : 1,000.20 GB (931.51 GiB)
Verdict        : FAKE - the drive does NOT hold what it claims
Real capacity  : 31.42 GB (29.26 GiB)  = 3.1 % of the claim
Behaviour      : Recycling: when its real flash is full it silently throws away older data to make room.
Safe size      : 28,733 MB  (partition size to use)
==========================================================================
```
*(illustrative output)*

---

## Why not H2testw / ValiDrive?

| | H2testw | ValiDrive | **True Flasher** |
|---|---|---|---|
| Catches drives that recycle / overwrite old data | ✅ | ⚠️ spot checks only | ✅ |
| Time on a fake "1 TB" drive at 15 MB/s | ~19 h write + read | minutes | **≈ real size ÷ speed** (32 GB ≈ 45 min) |
| Measures the real size (not just "fake: yes") | ✅ | approximate | ✅ counts every surviving page |
| Tells you *how* it fakes | ❌ | ❌ | ✅ wraparound / recycling / discard / bad flash |
| Fixes the drive at its real size | ❌ | ❌ | ✅ one click: partition, format and label it "USB 28GB" |

H2testw is accurate but has to fill the whole claimed size before it checks anything. True Flasher stops writing **as soon as older data starts disappearing**, then verifies everything it wrote.

## How it works

Every 4 KiB page True Flasher writes is stamped with a magic tag, a random per-run ID, **its own address**, a header check and 4,064 bytes of pseudo-random data derived from all of that. When a page is read back, True Flasher can tell exactly what happened to it:

- **intact**: the right data at the right address
- **moved**: data from a *different* address landed here (wraparound / aliasing)
- **blank**: reads back as all 00 or all FF (data was discarded)
- **stale**: data from an earlier run (the new write was dropped)
- **corrupt / unreadable**: damaged flash or I/O errors

### Stage 1: Quick probe (~1 minute)
Stamps the first 64 MiB, then single stamped pages across the entire claimed size. These cover a power-of-two grid plus both sides of every power-of-two boundary, written **top-down**. If high addresses mirror back onto low ones, the later low write overwrites them and the high probe reads back foreign data. That reveals the wraparound period directly. If data above some point just vanishes, it binary-searches the edge to 1 MiB. Most cheap fakes are caught here within a minute.

### Stage 2: Fill with spot checks
Writes the drive sequentially from the start. At checkpoints (16, 32, 64 … 512 MiB, then every 1 GiB) it re-reads a sample of **everything written so far**:
- the oldest pages (recycling fakes evict those first)
- the first pages of the early chunks
- both sides of every power-of-two boundary
- 1,024 random pages (4,096 once trouble appears)

The newest 16 MiB is excluded so the drive's RAM cache can't fake a pass.

Once older data goes missing, True Flasher confirms with one more interval: **does writing more data still increase the amount of intact data?**
- A fake that's out of real flash loses about as much old data as it takes in, so net capacity stops growing. Writing stops.
- A genuine drive with a bad spot keeps growing. It continues and reports **FAULTY**, not FAKE.

### Stage 3: Full read-back
Reads back everything written and counts every intact page. **That count is the real capacity**, measured rather than estimated.

### Accuracy
- Windows caching is bypassed (`FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH` on the raw `\\.\PhysicalDriveN`), so every read comes from the drive.
- Wraparound, FIFO-recycling, random-recycling, silent-discard and write-error fakes are all measured correctly in the included simulations, within 1.5% of the true size (see *Tests*).
- On recycling fakes the figure is slightly **conservative**: a few MB of the real flash still hold stage 1 probe data and are counted as unavailable. The safe partition size adds a further 4% margin (10% when only an estimate is available).

## Usage

1. Download or build True Flasher (see *Building*).
2. Plug in the drive and **copy off anything you need**. The test erases it.
3. Double-click **`TrueFlasher.exe`** and accept the administrator prompt (raw disk access needs it).
4. Pick the drive, press **Start test**, and **drag the slider** to confirm the erase.
5. Watch the three stages: **Quick probe → Fill & spot-check → Verify**. There's a progress bar, live speed, and the result of the last spot check. **Stop stage** ends the current stage early; whatever was written is still verified.
6. The verdict card shows **GENUINE**, **FAKE**, **FAULTY** or **PARTIAL**, with the real capacity and how the drive fakes it.

A full log is saved to your Desktop (`TrueFlasher_<date>_disk<N>.log`).

### Fix drive: make a fake drive usable at its true size
A fake controller only loses data once you store more than its real flash can hold. **Fix drive** creates a partition that ends *inside* the real flash, formats it (FAT32 up to 32 GB, exFAT above) and **labels it with its real size**, e.g. `USB 28GB`. From then on every file lands in working storage and the drive can't lose data.

- The size is suggested from the test (real capacity minus a 4% safety margin). You can change it and the label.
- Already tested earlier? Select the drive, press **Fix drive** and enter the safe size from the old report. No need to re-test.
- What software *can't* change: the size the controller firmware reports. Disk Management will still show a "1 TB" disk with ~970 GB unallocated. Leave that space alone. Explorer only shows your partition.

## Linux

```
./build.sh                      # → bin/linux-x64/trueflasher   (one self-contained file, no .NET needed to run it)
./build.sh arm64                # → bin/linux-arm64/trueflasher (Raspberry Pi 4/5 and other 64-bit ARM)
sudo bin/linux-x64/trueflasher
```

Same engine and same verdicts as on Windows, in a terminal interface:

- **Drive picker** with ↑/↓ and Enter. Only USB and SD/MMC drives can be chosen; the disk holding `/`, `/boot`, `/home`, `/usr`, `/var` or active swap is always protected, including through LVM, LUKS and RAID layers.
- **One-key confirm** (`y`), then a live panel: stage markers, progress bar, speed, last spot check, elapsed time. **Q** or **Ctrl+C** stops the current stage.
- The drive is unmounted for you and opened with `O_EXCL | O_DIRECT | O_SYNC`, so nothing else can use it and the page cache can't fake a pass.
- **Fix it now?** After a FAKE verdict, one key partitions the drive to its real size (`sfdisk`), formats it (`mkfs.vfat` / `mkfs.exfat`) and labels it, e.g. `USB 28GB`.
- The log is saved in the current folder and handed back to your user when run through `sudo`.

```
sudo trueflasher [options]

  --device /dev/sdX   test this drive (skips the picker; still asks to confirm)
  --quick             stage 1 only (~1 minute)
  --no-probe          skip stage 1
  --check-mb N        spot-check interval during the fill, in MB (default 1024)
  --list              list drives and exit
  --fix-mb N          don't test: partition the drive to N MB (its real size from an earlier test)
  --label TEXT        volume label for the fix (default "USB <size>GB")
  --any-bus           also allow non-USB drives and loop devices (system disk stays protected)
  --no-color          plain output (also honours NO_COLOR)
```
Building needs the .NET 8 SDK (`sudo apt install dotnet-sdk-8.0`). Without it, `build.sh` falls back to Mono (`mono-mcs`), and the result then needs `mono` to run. The fix step needs `sfdisk` (util-linux/fdisk), plus `dosfstools` or `exfatprogs`; if one is missing, True Flasher prints the exact commands instead.

**Verified on real Linux block devices.** Besides the simulations, the Linux build was run against loop devices through the actual kernel I/O path:

| Device | Result |
|---|---|
| Genuine 512 MiB and 6 GiB loop devices | ✅ GENUINE, every byte verified |
| Fake: claims 2 GiB, only 300 MiB of real storage behind it (kernel returns `ENOSPC` past that) | ✅ FAKE, measured 0.31 GB, exactly the backing size |
| Fix on that fake | ✅ 284 MB FAT32 partition labelled `USB 297MB`; random data written over the whole partition read back byte-identical |

## Windows console version
`TrueFlasherCLI.exe` does the same from a Windows terminal, for scripting or logs:
```
TrueFlasherCLI.exe [--disk N] [--quick] [--no-probe] [--check-mb N] [--list]
TrueFlasherCLI.exe --disk N --fix-mb SIZE [--label TEXT]

  --disk N       preselect physical disk N (you still have to type ERASE)
  --quick        stage 1 only (~1 minute). Recycling fakes can pass this; use the full test.
  --no-probe     skip stage 1
  --check-mb N   spot-check interval during the fill, in MB (default 1024)
  --list         list disks and exit
  --fix-mb N     don't test: partition the drive to N MB (its real size from an earlier test)
  --label TEXT   volume label for --fix-mb (default "USB <size>GB")
```
Press **Q** or **Ctrl+C** to stop the current stage. Exit codes: `0` genuine / no problem found, `3` fake, `1` error, `2` bad arguments.

## Building on Windows

No Visual Studio needed. Windows already includes the .NET Framework 4 C# compiler.

```
build.bat          → bin\TrueFlasher.exe     (window)
                     bin\TrueFlasherCLI.exe  (console)
build.bat test     → also builds and runs the simulated fake-drive tests
```
The source is plain C# 5 with no dependencies, so it also builds with Visual Studio, Mono (`mcs`) or `dotnet` (as a .NET Framework 4.x console app).

## Tests

`tests/SimTests.cs` runs the real engine against simulated drives, scaled down to a claimed 4 GiB so they finish in seconds:

| Simulated drive | Expected | Result |
|---|---|---|
| Genuine 512 MiB | GENUINE | ✅ exact |
| Wraparound, real 256 MiB (power of two) | FAKE | ✅ exact |
| Wraparound, real 300 MiB (odd size) | FAKE | ✅ exact |
| Wraparound, real 37 MiB (tiny) | FAKE | ✅ exact |
| Recycling, oldest data evicted (FIFO), real 200 MiB | FAKE | ✅ exact |
| Recycling, **random** data evicted, real 200 MiB | FAKE | ✅ within 1.0% |
| Recycling, random eviction, real 700 MiB | FAKE | ✅ within 0.4% |
| Writes above 333 MiB silently discarded | FAKE | ✅ exact |
| Write errors above 250 MiB | FAKE | ✅ within 0.8% |
| Genuine 768 MiB with a 3 MiB bad spot | FAULTY (not fake) | ✅ |
| Genuine drive, user stops early | PARTIAL | ✅ |

## Safety

- **The test erases the selected drive.**
- Only USB / SD / MMC disks are listed as testable. The disk that holds Windows is detected and refused.
- You must confirm with a slide-to-erase control (in the console version, by typing `ERASE`).
- Volumes on the target drive are locked and dismounted for the test, so Windows doesn't fight the raw writes.
- **Fix drive** re-checks that the disk number still belongs to the same drive (same size, still USB) before running diskpart.

## Project layout

```
src/
  Engine.cs       the three-stage test (portable, no Windows code)
  Pattern.cs      self-identifying 4 KiB page stamps
  Report.cs       verdict, real size, behaviour, safe size
  DriveFixer.cs   partition + format + label at the real size (diskpart)
  WinDisk.cs      Windows: raw unbuffered disk I/O, USB discovery, volume locking, system-disk protection
  Gui.cs          Windows: the window (WinForms, dark theme, slide-to-confirm)
  Program.cs      Windows: the console version
  Device.cs       shared interfaces
  app.ico, logo.png, app.manifest
  linux/
    LinuxDisk.cs  Linux: O_DIRECT block I/O, sysfs discovery, system-disk protection (LVM/LUKS aware)
    LinuxFixer.cs Linux: sfdisk + mkfs at the real size
    LinuxMain.cs  Linux: terminal UI
TrueFlasher.Linux.csproj   Linux build (self-contained single file)
build.bat / build.sh       Windows / Linux build scripts
tests/SimTests.cs          simulated fake drives (+ SimTests.csproj for .NET)
assets/           icon artwork + make_icon.py (regenerates app.ico)
```

## Limitations

- Windows and Linux. A macOS port would need only a new disk layer, since the engine is portable.
- A full test of a **genuine** 1 TB drive still means writing 1 TB. Stop it with Q whenever you're satisfied; the report says how much was verified.
- A controller that has already locked itself read-only (common after a fake has been overfilled) cannot be tested or repaired by any normal tool. True Flasher detects this and tells you.

## License

MIT © 2026 **Adromedary**. See [LICENSE](LICENSE).
