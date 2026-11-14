# Automated build (GitHub Actions)

`.github/workflows/build.yml` builds the mod on every push to `main`, on every pull request, and
on demand (Actions tab, "Build", "Run workflow"). A finished run has one artifact, named
`Resurrected_VRMod-<version>-<commit>`. Downloading it gives a zip in Thunderstore layout that can
be installed with the mod manager's "Import local mod".

No copy of the game is involved. The build uses:

| What | Where it comes from |
|------|---------------------|
| C# compiler | .NET SDK on the build machine (`dotnet build VRMod.sln -c Release`) |
| Game assemblies (stripped, reference only) | `RiskOfRain2.GameLibs` on the BepInEx NuGet feed |
| Hook definitions | `MMHOOK.RoR2` on NuGet |
| Unity engine modules, BepInEx | `UnityEngine.Modules`, `BepInEx.Core` on NuGet |
| XR / input libraries the mod ships | the published Thunderstore package of this mod |
| bHaptics and Shockwave SDKs (compile time only) | embedded files inside DrBibop's original `VRPatcher.dll` |
| Post-processing and tracked pose driver (game side) | compiled for reference from Unity's package registry |
| Decalicious (game side) | a small reference stub, `stubs/Decalicious/Decalicious.cs` |

`fetch-refs.sh` downloads and prepares everything in the last four rows into `refs/` (ignored by
git). `package.sh` then takes the published package, puts the newly built `VRMod.dll`,
`VRPatcher.dll` and `VRAPI.dll` in place of the published ones, and copies in this repository's
`manifest.json`, `README.md`, `CHANGELOG.md` and `icon.png`. Every other file in the artifact is
byte for byte the one from the published package.

## Things to change now and then

- **Game update:** set `GameLibsVersion` and `MMHookVersion` in `Directory.Build.props` to the
  versions that match the new game build.
- **New published release with changed XR libraries:** nothing to do, the latest published
  package is used. To pin one, set `TS_VERSION` in the workflow's "Fetch references" step.
- **The mod starts using another member of a game-side assembly that has only a stub:** add the
  member to the stub source.

## Building on your own machine

With the .NET SDK (8 or newer) and a bash shell (Git Bash on Windows works):

```
bash build-tools/ci/fetch-refs.sh
dotnet build VRMod.sln -c Release
bash build-tools/ci/package.sh
```

The package is then in `dist/package/`.

## How this differs from the hand-driven build

The `*.ps1` / `ubuild.py` scripts one folder up compile against a real copy of the game's
`Managed` folder. This build compiles against stripped reference assemblies instead, and targets
.NET Standard 2.1 (as DrBibop's original project files did). The resulting DLLs are not
byte-identical to a hand-driven build, so test an artifact in the game before publishing it.
