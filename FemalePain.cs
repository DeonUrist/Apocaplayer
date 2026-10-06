using System;
using System.IO;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace Apocaplayer
{
    internal static class FemalePain
    {
        private static readonly string[] Names = { "human_hurt", "human_hurt_2" };
        private static readonly AudioClip[] Clips = new AudioClip[2];

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                try { Clips[i] = Load(Plugin.ModPath("Sounds/Female/" + Names[i] + ".wav")); }
                catch (Exception e) { Plugin.Warn("Female pain " + Names[i] + ": " + e.Message); }
            }
            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(PlayRandomSound), "OnEnter"),
                prefix: new HarmonyLib.HarmonyMethod(typeof(FemalePain), nameof(Before)),
                finalizer: new HarmonyLib.HarmonyMethod(typeof(FemalePain), nameof(Finally)));
        }

        internal static void Before(PlayRandomSound __instance, out FsmObject[] __state)
        {
            __state = null;
            if (!Plugin.Enabled.Value || !Plugin.Female || __instance.Fsm == null || __instance.audioClips == null) return;
            string fsm = __instance.Fsm.Name;
            if (fsm != "DamageEffectSound" && fsm != "DamageEffectSound_InCar") return;
            var owner = __instance.Owner;
            if (owner == null) return;
            bool player = Game.Player != null ? owner == Game.Player || owner.transform.IsChildOf(Game.Player.transform)
                : owner.name == "Player" || owner.name == "head" && owner.transform.parent != null && owner.transform.parent.name == "Player";
            if (!player) return;
            FsmObject[] replacement = null;
            for (int i = 0; i < __instance.audioClips.Length; i++)
            {
                var original = __instance.audioClips[i] != null ? __instance.audioClips[i].Value as AudioClip : null;
                int index = original != null ? Array.IndexOf(Names, original.name) : -1;
                if (index < 0 || Clips[index] == null) continue;
                if (replacement == null) replacement = (FsmObject[])__instance.audioClips.Clone();
                replacement[i] = new FsmObject { ObjectType = typeof(AudioClip), Value = Clips[index] };
            }
            if (replacement == null) return;
            __state = __instance.audioClips; __instance.audioClips = replacement;
        }

        internal static Exception Finally(PlayRandomSound __instance, FsmObject[] __state, Exception __exception)
        {
            if (__state != null) __instance.audioClips = __state;
            return __exception;
        }

        private static AudioClip Load(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a RIFF WAV");
                reader.ReadUInt32(); if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAV");
                ushort format = 0, channels = 0, bits = 0; int rate = 0; byte[] data = null;
                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    string chunk = new string(reader.ReadChars(4)); uint size = reader.ReadUInt32();
                    long end = reader.BaseStream.Position + size;
                    if (end > reader.BaseStream.Length) throw new InvalidDataException("Truncated WAV");
                    if (chunk == "fmt ")
                    {
                        format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                        reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
                    }
                    else if (chunk == "data") data = reader.ReadBytes((int)size);
                    reader.BaseStream.Position = Math.Min(reader.BaseStream.Length, end + (size & 1));
                }
                if (format != 1 || bits != 16 || channels < 1 || rate < 1 || data == null || data.Length % (channels * 2) != 0)
                    throw new InvalidDataException("Expected 16-bit PCM WAV");
                var samples = new float[data.Length / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = (short)(data[i * 2] | data[i * 2 + 1] << 8) / 32768f;
                var clip = AudioClip.Create("Apocaplayer.Female." + Path.GetFileNameWithoutExtension(path), samples.Length / channels, channels, rate, false);
                clip.hideFlags = HideFlags.DontUnloadUnusedAsset; clip.SetData(samples, 0); return clip;
            }
        }
    }
}
