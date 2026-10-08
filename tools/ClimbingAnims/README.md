# Climbing animation source project

Use Unity **2020.3.49f1**, matching Apocalypter. Open this folder and select
**Apocaplayer → Build climbing bundle**. The builder creates two Humanoid clips
and copies `apocaplayer_climbing.bundle` into Apocaplayer/Models.

The UE source skeleton is mapped to a Humanoid Avatar; the game retargets the
clips onto its Mixamo avatar. Horizontal root origin is measured from the body
instead of retaining the authored ledge offset. Root travel is extracted;
Apocaplayer moves the physical player separately. Runtime playback uses 1.5x
speed and ends at 65% of each clip, skipping the stationary tail.

Sources are the neutral standing GAP waist mantle (2 s) and high climb (3.333 s).
See `../../CLIMBING-ANIMATION-NOTICES.md` for the confirmed Fab licence and
redistribution restrictions. Ship the built bundle rather than source FBX files.
