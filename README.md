<p align="center">
  <img src="assets/vkorobku-icon.png" width="140" alt="vKOROBKU" />
</p>

<h1 align="center">vKOROBKU</h1>

<p align="center">
  Game compression with built-in Windows tools to save disk space.
</p>

<p align="center">
  <b>English</b> | <a href="README.ru.md">Русский</a>
</p>

<p align="center">
  <a href="#download">Download</a> ·
  <a href="#features">Features</a> ·
  <a href="#development">Development</a> ·
  <a href="#license">License</a>
</p>

<p align="center">
  <a href="https://github.com/damnpotato430-eng/vkorobku/actions/workflows/build.yml"><img src="https://github.com/damnpotato430-eng/vkorobku/actions/workflows/build.yml/badge.svg" alt="Build status" /></a>
  <a href="https://github.com/damnpotato430-eng/vkorobku/releases"><img src="https://img.shields.io/github/v/release/damnpotato430-eng/vkorobku?include_prereleases&style=for-the-badge&logo=github&label=release&color=8BFF4D" alt="Latest release" /></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=for-the-badge&logo=windows" alt="Windows 10/11" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0-555555?style=for-the-badge" alt="GPL-3.0" /></a>
</p>

A Windows application that estimates and transparently compresses installed games with the XPRESS and LZX algorithms. Games keep working as before — only the way their files are stored on NTFS changes.

<p align="center">
  <img src="assets/screenshot-main.png" width="820" alt="vKOROBKU main window" />
</p>

> Preview version: the core works and has been verified on real game libraries; field testing continues. Start with games you can restore through Steam file verification.

## Download

### [Download the latest vKOROBKU release for Windows x64](https://github.com/damnpotato430-eng/vkorobku/releases)

Download the `vKOROBKU-v<version>-win-x64.zip` archive, extract it completely and run `vKOROBKU.exe`. Keep `vKOROBKU.Worker.exe` next to it.

Releases are self-contained: no separate .NET Runtime installation is required. They are built automatically from tags by GitHub Actions.

## Target platform

- Windows 10/11 x64
- NTFS for XPRESS/LZX operations

## Features

**Analysis and compression**

- automatic discovery of Steam, Epic Games Store and GOG games, plus Ubisoft Connect and EA App (experimental — not yet verified on live installations); manual folder adding with game detection;
- preliminary estimate on a safe sample (512 MB – 2 GB) with a forecast for XPRESS4K/8K/16K and LZX and a read-speed benchmark that bypasses the system cache;
- automatic algorithm choice balancing savings against read speed: the app never sacrifices loading times for a couple hundred megabytes;
- compression, full decompression and cancellation; an interrupted operation resumes where it stopped;
- a "finish" button for updated games — compresses only the new files with the same algorithm;
- skipping files that are known to be incompressible (media, archives — 41 types, configurable): faster compression and a more accurate forecast.

**Monitoring and safety**

- watching compressed games: on startup the app checks whether games "decompressed themselves" after updates and shows how much space can be reclaimed;
- DirectStorage detection: compression is discouraged and blocked for such games (available in expert mode after explicit confirmation);
- recognition of games compressed earlier or by third-party tools, via WOF/NTFS;
- administrator rights are requested only for the compression operation itself — the UI always runs unelevated;
- new-version notification (no auto-install).

**Interface**

- the "pick a game → Optimize" one-button flow; an optional expert mode with manual algorithm and analysis-precision selection;
- colored card statuses, savings per drive and in total;
- Steam covers, including for non-Steam games matched by name — no setup or API keys; an operations journal and a settings window.

## Development

Requires the .NET 10 SDK with the Windows Desktop workload.

```powershell
dotnet restore
dotnet build vKOROBKU.sln
dotnet run --project src/vKOROBKU.App
```

The project is verified to build with .NET SDK 10.0.302.

Details: [MVP specification](docs/MVP.md) and [architecture](docs/ARCHITECTURE.md) (in Russian).

## License

GNU General Public License v3.0. See [LICENSE](LICENSE).
