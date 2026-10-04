using System;
using UnityEngine;

namespace Apocaplayer
{
    // A blast lance thrown from the driver's seat starts inside her own car (the game spawns it at the first-person throw point and pushes it
    // forward) and blew up on the car's own body / doors / windows. Harmony postfix on PlayMaker's CreateObject.OnEnter: when a blast lance's
    // [Attack] FSM creates its projectile while she sits in a car, the projectile's colliders ignore every collider of that car (and hers),
    // so it flies out and only hits the world.
    internal static class Projectiles
    {
        public static void Patch(HarmonyLib.Harmony h)
        {
            var m = HarmonyLib.AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.CreateObject), "OnEnter");
            if (m == null) { Plugin.Warn("Projectiles: CreateObject.OnEnter not found - lances may hit your own car"); return; }
            h.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(Projectiles), nameof(AfterCreateObject)));
        }

        public static void AfterCreateObject(HutongGames.PlayMaker.Actions.CreateObject __instance)
        {
            try
            {
                if (!Plugin.Enabled.Value || !Game.InCar || __instance == null || __instance.Fsm == null || __instance.Fsm.Name != "Attack") return;
                string owner = __instance.Fsm.GameObjectName ?? "";
                if (!owner.StartsWith("blastlance", StringComparison.OrdinalIgnoreCase)) return;
                var go = __instance.storeObject != null ? __instance.storeObject.Value : null;
                var car = Game.CarRoot;
                if (go == null || car == null) return;
                var mine = go.GetComponentsInChildren<Collider>(true);
                int n = 0;
                foreach (var c in car.GetComponentsInChildren<Collider>(true))
                    foreach (var p in mine) if (c != null && p != null) { Physics.IgnoreCollision(p, c, true); n++; }
                if (Game.Player != null)
                    foreach (var c in Game.Player.GetComponentsInChildren<Collider>(true))
                        foreach (var p in mine) if (c != null && p != null) Physics.IgnoreCollision(p, c, true);
                Plugin.Verbose("Projectiles: " + go.name + " from " + owner + " ignores " + car.name + " (" + n + " collider pairs)");
            }
            catch (Exception e) { Plugin.Verbose("Projectiles: " + e.Message); }
        }
    }
}
