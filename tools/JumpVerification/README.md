# First-person jump verification

Build the mod, then run:

```powershell
dotnet build tools/JumpVerification/NativeVerifier.csproj -c Release
./tools/JumpVerification/Run.ps1 -Visual
./tools/JumpVerification/Run.ps1 -Visual -Character Max
```

The isolated native Unity runtime disables world/save FSMs and uses the game's
actual Mixamo avatar, Apocaplayer mesh and animation bundle. It samples 61 poses
from each of five jump clips, plus Run, RifleRunLow and Kick, with and without a
drawn weapon at four look pitches and two body headings.

Checks verify the forward thigh limit, preserved knee bends and bone lengths,
unchanged third-person/run/kick poses, and disabling the correction. Transition
checks cover 30, 60 and 144 fps and landing release. The final integration check
calls the actual Runner LateUpdate, including pause and view switching.

`latest-report.txt` contains the measurements and assertions. `baseline-report.txt`
records the original uncorrected geometry. Optional rendered before/after images
at 30, 60 and 85 degrees down are written to
`V:/Repos/Apocalypter/_staging/apocaplayer-jump/results`.

Without `-Visual`, the verifier runs without graphics. The test process never
loads or writes a player save.
