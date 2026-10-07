using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Apocaplayer
{
    // ================================================================================================================================
    // (2.2.0) ModAPI - the player's animations for other mods.
    //
    // Any humanoid (an NPC, a companion, a mannequin ...) can be animated exactly like the player's third-person body: the same clips, the same
    // locomotion (idle, 8 directions x walk / run / sprint, crouch idle, 8 crouched walks, the relaxed set, turns in place, walk-to-stop, jumps),
    // the same hands (rifle / pistol aim and fire, the low-ready rifle, the lowered pistol, reloads incl. one round at a time, ShotgunPump),
    // throws, punches, melee swings and kicks, the weapon in the right hand at the player's tuned per-clip weapon poses, the aim lift.
    // Only the clips the player itself uses are offered (ClipNames); the bundle's other clips (death poses ...) are not part of the API.
    //
    // Use it (reference Apocaplayer.dll, or call it by reflection; BepInDependency("com.denis.apocalypter.apocaplayer", SoftDependency)):
    //     if (ModAPI.Ready) {
    //         var c = ModAPI.Attach(npcAnimator);           // null: not a humanoid / no animation bundle
    //         c.SetWeapon(gunModelTransform);              // moved to the right hand (put back by SetWeapon(null) / Dispose)
    //         // every frame (Update): what it does - the velocity comes from its Rigidbody by itself
    //         c.Crouched = ...; c.Aiming = ...; c.Firing = ...; c.Airborne = ...; c.AimPitch = ...;
    //         // events: c.Reload(seconds, rounds); c.Pump(); c.Jump(); c.Kick(); c.Throw(); c.Strike(seconds); c.Shoot();
    //         c.Suspended = true;                           // the Animator's own controller back for a while (death, a seat ...)
    //         c.Dispose();                                  // done with it (also automatic when the Animator is destroyed)
    //     }
    // The character is animated in LateUpdate by Apocaplayer; set its inputs in Update. All members are safe to call every frame.
    // ================================================================================================================================
    public static partial class ModAPI
    {
        public const int ApiVersion = 1;
        public static string PluginVersion { get { return Plugin.VERSION; } }

        // the animation bundle is there with the player's Mixamo set (what the player's body uses)
        public static bool Ready
        {
            get
            {
                try { return Anims.Loaded && Anims.Get("RifleIdle") != null && (Anims.Get("RifleWalk") != null || Anims.Get("RifleWalkLow") != null); }
                catch (Exception) { return false; }
            }
        }

        // ---------------------------------------------------------------- the player's clips (and only those)
        // every clip the player's body can play (Loco.cs / Body.cs): the legs (LocoPlan's 42 slots with their stand-ins), the hands, the actions,
        // throws and strikes. The bundle's other clips are left out on purpose.
        private static string[] _names;
        private static HashSet<string> _set;
        private static void BuildNames()
        {
            if (_names != null) return;
            var l = CharPlan.PlayerClips(n => Anims.Get(n) != null);
            if (!Anims.Loaded) return;                       // not cached until the bundle is there
            l.Sort(StringComparer.Ordinal);
            _names = l.ToArray();
            _set = new HashSet<string>(l, StringComparer.OrdinalIgnoreCase);
        }
        public static string[] ClipNames() { BuildNames(); return _names != null ? (string[])_names.Clone() : new string[0]; }
        public static bool IsPlayerClip(string name) { BuildNames(); return name != null && _set != null && _set.Contains(name); }
        // the clip itself (null for anything the player doesn't use)
        public static AnimationClip GetClip(string name) { return IsPlayerClip(name) ? Anims.Get(name) : null; }

        // ---------------------------------------------------------------- weapons
        // "akm_trash_model (1)" / "9mm_borz_smg(Clone)" -> "akm_trash" / "borz_smg": the key of the weapon-pose and aim-lift tables
        public static string WeaponKey(string objectName) { return string.IsNullOrEmpty(objectName) ? "" : Props.Norm(objectName); }
        // "None", "Rifle" (rifles, shotguns, crossbow), "Pistol" (pistols, revolver, SMGs), "Melee", "Throw"
        public static string WeaponKind(string weaponKey) { return Props.KindOf(weaponKey).ToString(); }
        public static bool HasWeaponPoses(string weaponKey) { return !string.IsNullOrEmpty(weaponKey) && GunPose.HasAny(weaponKey); }
        // where the weapon model sits under the right hand bone in that clip (the player's table: weapon-poses.txt > built in > the kind's idle)
        public static bool WeaponPose(string weaponKey, string clip, out Vector3 localPosition, out Quaternion localRotation)
        {
            localPosition = Vector3.zero; localRotation = Quaternion.identity;
            if (string.IsNullOrEmpty(weaponKey)) return false;
            var v = GunPose.EffectiveFor(weaponKey, clip, IdleOf(Props.KindOf(weaponKey)), null);
            if (v == null) return false;
            localPosition = new Vector3(v[0], v[1], v[2]) * 0.01f; localRotation = Quaternion.Euler(v[3], v[4], v[5]);
            return true;
        }
        // the aim lift for an aim / fire clip: x = hands lifted (cm), y = head down (degrees), z = hands forward (cm)
        public static Vector3 AimLiftOf(string weaponKey, string clip, bool crouched)
        {
            var v = AimLift.Get(weaponKey ?? "", clip ?? "", crouched);
            return new Vector3(v[0], v[1], v[2]);
        }
        internal static string IdleOf(Props.Kind k) { return k == Props.Kind.Rifle ? "RifleIdle" : k == Props.Kind.Pistol ? "PistolIdle" : "Idle"; }

        // the clip the player reloads this weapon with ("RifleReload" / "PistolReload"), null without one
        public static string ReloadClip(string weaponKey)
        {
            var k = Props.KindOf(weaponKey);
            string c = k == Props.Kind.Rifle ? "RifleReload" : k == Props.Kind.Pistol ? "PistolReload" : null;
            return c != null && Anims.Get(c) != null ? c : null;
        }
        public static float ReloadClipSeconds(string weaponKey) { var c = Anims.Get(ReloadClip(weaponKey)); return c != null ? c.length : 0f; }
        // one round of a one-round-at-a-time reload at the clip's own speed, s (the clip's round part)
        public static float RoundSeconds(string weaponKey)
        {
            string rc = ReloadClip(weaponKey); float a, b;
            var c = Anims.Get(rc);
            return c != null && LocoPlan.RoundSegment(rc, out a, out b) ? c.length * (b - a) : 0f;
        }

        // the player's copy of the gun (WeaponsArm/Parent/<key>) decides: its Reload FSM loops one round at a time ("checkAmmoInStore") / its
        // Attack FSM cocks after a shot ("pump" / "bolt action"). Weapons the player has no copy of: the known ones by name.
        private static readonly Dictionary<string, bool> _perRound = new Dictionary<string, bool>(), _cocks = new Dictionary<string, bool>();
        public static bool ReloadsOneRoundAtATime(string weaponKey) { return FsmFlag(weaponKey, _perRound, "Reload", "checkAmmoInStore"); }
        public static bool CocksAfterShot(string weaponKey) { return FsmFlag(weaponKey, _cocks, "Attack", null); }
        private static bool FsmFlag(string key, Dictionary<string, bool> cache, string fsmName, string state)
        {
            if (string.IsNullOrEmpty(key)) return false;
            bool r;
            if (cache.TryGetValue(key, out r)) return r;
            Transform w = null;
            try
            {
                if (Game.WeaponsParent != null)
                    foreach (var name in new[] { key, key.Replace("_chopped", ""), key.Replace("_scoped", "") })
                        if ((w = Game.WeaponsParent.Find(name)) != null) break;
            }
            catch (Exception) { w = null; }
            if (w == null)
            {
                // not found (yet): by name, not cached (the player's weapons show up once the camera is found)
                string k = key.ToLowerInvariant();
                if (state != null) return k.Contains("revolver") || k.Contains("slamberg") || k.Contains("rochester") || k.Contains("redmark");
                return k.Contains("slamberg") || k.Contains("redmark");
            }
            r = false;
            try
            {
                foreach (var f in w.GetComponents<PlayMakerFSM>())
                {
                    if (f == null || f.FsmName != fsmName) continue;
                    foreach (var st in f.FsmStates)
                        if (st != null && (state != null ? st.Name == state : LocoPlan.IsCocking(st.Name))) { r = true; break; }
                }
            }
            catch (Exception) { }
            cache[key] = r;
            return r;
        }

        // the clips' native ground speeds (the player's settings): a body moved faster than twice these slides its feet
        public static float NativeSpeed(string tier)
        {
            switch ((tier ?? "").ToLowerInvariant())
            {
                case "run": return Plugin.ClipRunSpeed.Value;
                case "sprint": return Plugin.ClipSprintSpeed.Value;
                case "crouch": return Plugin.ClipCrouchSpeed.Value;
                default: return Plugin.ClipWalkSpeed.Value;
            }
        }

        // ---------------------------------------------------------------- characters
        private static readonly List<Character> _chars = new List<Character>();
        public static int Count { get { return _chars.Count; } }

        // starts animating this humanoid Animator with the player's clips (the same object for the same Animator); null when it can't
        public static Character Attach(Animator animator)
        {
            if (animator == null) return null;
            var have = Find(animator);
            if (have != null) return have;
            if (!Ready || animator.avatar == null || !animator.avatar.isHuman) return null;
            try
            {
                var c = new Character(animator);
                if (!c.Valid) return null;
                _chars.Add(c);
                Plugin.Verbose("ModAPI: animating " + animator.gameObject.name + " (" + _chars.Count + " characters)");
                return c;
            }
            catch (Exception e) { Plugin.Log.LogError("ModAPI.Attach " + animator.gameObject.name + ": " + e); return null; }
        }
        public static Character Find(Animator animator)
        {
            if (animator == null) return null;
            for (int i = 0; i < _chars.Count; i++) if (_chars[i].Animator == animator && _chars[i].Valid) return _chars[i];
            return null;
        }
        internal static void Forget(Character c) { _chars.Remove(c); }

        // every LateUpdate (ModApiRunner): animate them all; a destroyed Animator ends its character
        internal static void LateTick()
        {
            for (int i = _chars.Count - 1; i >= 0; i--)
            {
                var c = _chars[i];
                if (c == null || !c.Valid || c.Animator == null) { if (c != null) c.Dispose(); if (i < _chars.Count && _chars[i] == c) _chars.RemoveAt(i); continue; }
                c.Tick();
            }
        }
        internal static void OnSceneLoaded()
        {
            foreach (var c in _chars.ToArray()) if (c != null) c.Dispose();
            _chars.Clear(); _perRound.Clear(); _cocks.Clear();
        }
    }

    // runs the characters after the Animators (DontDestroyOnLoad, on Apocaplayer's runner object)
    internal sealed class ModApiRunner : MonoBehaviour
    {
        private void LateUpdate() { ModAPI.LateTick(); }
    }
}
