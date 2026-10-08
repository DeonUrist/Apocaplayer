# Integrated climbing verification

Build Apocaplayer, then run:

```powershell
dotnet build tools/ClimbingVerification/NativeVerifier.csproj -c Release
./tools/ClimbingVerification/Run.ps1
```

The verifier uses an isolated runtime with native world/save FSMs blocked and
only Apocaplayer plus its test plugin installed. It exercises actual Humanoid
animation on the game's Mixamo avatar and native Unity physics. Synthetic input
is local to the test process; control rebinds are not saved to PlayerPrefs.

Latest run: **132 checks passed**, including climbing, weapon stow/restoration,
head-follow view, cancellation, geometry, forward checks on raised car bodies,
wall-contact grounding, jump fallback and same-frame input retries, custom keys,
game rebinds, vehicle hints, avoiding double light toggles, and settings order.
The additional pressure cases verify full climbs with shallow wall penetration,
while rejecting deeper penetration, motion farther into the wall and ceilings.

Full report: `latest-report.txt`.
