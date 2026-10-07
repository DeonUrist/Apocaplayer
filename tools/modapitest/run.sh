#!/bin/sh
# Offline test of ModAPI (CharPlan.cs): every weapon kind x every situation + NPC timelines, the poses built from the FBX clips and drawn.
# Needs mono/mcs, python3 + numpy + pillow, bpy (pip install bpy==5.1.2 on Python 3.13) and the clips as npz (../locotest/extract.py).
#   sh run.sh <npz clips dir> <XBot.fbx> <out dir>
set -e
D=$(dirname "$0"); C=${1:?npz clips}; X=${2:?XBot.fbx}; O=${3:-out}; mkdir -p "$O"
CLIPS="$C" python3 "$D/clipinfo.py" "$D/bundle.txt" > "$D/clipinfo.txt"
mcs -out:"$D/modapitest.exe" "$D/../../LocoPlan.cs" "$D/../../CharPlan.cs" "$D/../../AimLift.cs" "$D/ModApiTest.cs"
mono "$D/modapitest.exe" "$D/clipinfo.txt" "$O/frames.json"
CLIPS="$C" python3 "$D/pose_check.py" "$O/frames.json" "$O"
python3 "$D/render_frames.py" "$O/poses.pkl" "$X" "$O/npc.png" "NPC,Pistol+aim crouch,Rifle+aim crouch idle,reload,pump"
