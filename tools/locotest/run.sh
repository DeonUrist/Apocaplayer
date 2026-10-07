#!/bin/sh
# Offline check of the locomotion (needs mono/mcs, python3 + numpy/matplotlib/pillow, and bpy: pip install bpy==5.1.2 on Python 3.13).
#   sh run.sh <UnityAnims/Assets folder> <out dir>
# 1. the FBX clips -> clips/*.npz (once; delete clips/ after changing an FBX)   2. LocoPlan.cs for every weapon x position -> plan.json
# 3. poses over one stride, measurements -> out/report.txt (PASS/FAIL per case)   4. mesh renders on X Bot -> out/loco_<weapon>.png
set -e
A=${1:?UnityAnims/Assets folder}; O=${2:-out}; D=$(dirname "$0")
python3 "$D/extract.py" "$A/Mixamo" "$D/clips" >/dev/null
ls "$D/clips" | sed 's/.npz$//' > "$D/clips.txt"; printf "WalkStrafeRight\nRightTurn\nThrowRight\n" >> "$D/clips.txt"
mcs -out:"$D/locotest.exe" "$D/../../LocoPlan.cs" "$D/LocoTest.cs"
mono "$D/locotest.exe" "$D/clips.txt" > "$D/plan.json"
python3 "$D/check.py" "$D/plan.json" "$O"
python3 "$D/render.py" "$O/poses.pkl" "$A/Avatar/XBot.fbx" "$O"
