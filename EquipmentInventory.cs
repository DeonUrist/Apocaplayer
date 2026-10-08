using System;
using System.Reflection;
using BepInEx.Configuration;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Apocaplayer
{
    // Read-only logical custody: an extra-slot weapon may physically occupy a
    // vanilla holder while that holder's original item is parked elsewhere.
    internal sealed class EquipmentInventory
    {
        public readonly GameObject[] Items = new GameObject[6];
        // (2.2.6) read every frame: the names and the invoke argument made once
        private static readonly string[] SlotNames = { "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5", "Slot 6" };
        private static readonly string[] PocketNames = { "Apocapocket_ExtraSlots/Slot 1", "Apocapocket_ExtraSlots/Slot 2", "Apocapocket_ExtraSlots/Slot 3", "Apocapocket_ExtraSlots/Slot 4", "Apocapocket_ExtraSlots/Slot 5", "Apocapocket_ExtraSlots/Slot 6" };
        private static readonly string[] InvNames = { "Apocainventory_ExtraSlots/Slot 1", "Apocainventory_ExtraSlots/Slot 2", "Apocainventory_ExtraSlots/Slot 3", "Apocainventory_ExtraSlots/Slot 4", "Apocainventory_ExtraSlots/Slot 5", "Apocainventory_ExtraSlots/Slot 6" };
        private static readonly object[] _arg = new object[1];
        private static readonly object[] _boxed = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        private static object[] Arg(int i) { _arg[0] = i < _boxed.Length ? _boxed[i] : i; return _arg; }
        public int Drawn = -1;
        private Transform _camera, _weapons;
        private PlayMakerFSM _weaponFsm;
        private bool _resolved;
        private FieldInfo _instance, _slots, _content, _operation, _save, _loading, _normalised, _enabled, _handOrigin, _handName;
        private PropertyInfo _ready;
        private MethodInfo _isDrawn;
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                foreach (string ns in new[] { "Apocapocket", "Apocainventory" })
                {
                    var runner = asm.GetType(ns + ".Runner", false);
                    if (runner == null) continue;
                    var slots = runner.GetField("Slots", Flags);
                    var content = slots != null && slots.FieldType.IsArray ? slots.FieldType.GetElementType().GetField("Content", Flags) : null;
                    var instance = runner.GetField("Instance", Flags);
                    var ready = runner.GetProperty("Ready", Flags);
                    if (content == null || instance == null || ready == null) continue;
                    _instance = instance; _slots = slots; _content = content; _ready = ready;
                    _operation = runner.GetField("CurrentOp", Flags); _save = runner.GetField("Save", Flags);
                    _loading = _save != null ? _save.FieldType.GetField("Loading", Flags) : null;
                    _normalised = _save != null ? _save.FieldType.GetField("Normalised", Flags) : null;
                    _isDrawn = runner.GetMethod("IsDrawn", Flags);
                    _handOrigin = runner.GetField("_handOrigin", Flags); _handName = runner.GetField("_handName", Flags);
                    var plugin = asm.GetType(ns + ".Plugin", false);
                    _enabled = plugin != null ? plugin.GetField("Enabled", Flags) : null;
                    Plugin.Verbose("Equipment: logical inventory bridge to " + ns);
                    return;
                }
        }

        // False preserves previous mount assignments during transactions and saves.
        public bool Read()
        {
            if (_camera != Game.PlayerCamera)
            {
                _camera = Game.PlayerCamera;
                _weapons = _camera != null ? _camera.Find("HandItemUse/Weapons") : null;
                _weaponFsm = null;
                if (_weapons != null) foreach (var f in _weapons.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Weapons") _weaponFsm = f;
                Array.Clear(Items, 0, Items.Length); Drawn = -1;
            }
            Resolve();
            if (_instance != null)
            {
                var enabled = _enabled != null ? _enabled.GetValue(null) as ConfigEntry<bool> : null;
                var run = _instance.GetValue(null);
                if (run != null && (enabled == null || enabled.Value))
                {
                    if (!(bool)_ready.GetValue(run, null)) return false;
                    var save = _save != null ? _save.GetValue(run) : null;
                    if (_operation != null && _operation.GetValue(run) != null
                        || save != null && (_loading != null && (bool)_loading.GetValue(save) || _normalised != null && (bool)_normalised.GetValue(save))) return false;
                    var slots = _slots.GetValue(run) as Array;
                    Array.Clear(Items, 0, Items.Length); Drawn = -1;
                    for (int i = 0; slots != null && i < Mathf.Min(Items.Length, slots.Length); i++)
                    {
                        var slot = slots.GetValue(i);
                        Items[i] = slot != null ? _content.GetValue(slot) as GameObject : null;
                        if (_isDrawn != null && (bool)_isDrawn.Invoke(run, Arg(i))) Drawn = i;
                    }
                    if (_handOrigin != null && _handName != null && _camera != null)
                    {
                        int origin = (int)_handOrigin.GetValue(run);
                        var hand = FirstItem(_camera.Find("Hand"));
                        if (origin >= 0 && origin < Items.Length && Items[origin] == null && hand != null && hand.name == (string)_handName.GetValue(run))
                        { Items[origin] = hand; Drawn = origin; }
                    }
                    return true;
                }
            }
            Array.Clear(Items, 0, Items.Length); Drawn = -1;
            for (int i = 0; i < Items.Length; i++)
            {
                var holder = _weapons != null ? _weapons.Find(SlotNames[i]) : null;
                if (holder == null && i >= 3 && _camera != null)
                    holder = _camera.Find(PocketNames[i]) ?? _camera.Find(InvNames[i]);
                Items[i] = FirstItem(holder);
                if (_weaponFsm != null && _weaponFsm.ActiveStateName == SlotNames[i]) Drawn = i;
            }
            return true;
        }

        public static GameObject FirstItem(Transform holder)
        {
            if (holder == null) return null;
            for (int i = 0; i < holder.childCount; i++)
            {
                var child = holder.GetChild(i);
                if (child.GetComponent<Rigidbody>() != null || child.GetComponentInChildren<MeshRenderer>(true) != null) return child.gameObject;
            }
            return null;
        }
    }
}
