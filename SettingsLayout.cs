using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;

namespace Apocaplayer
{
    internal static class SettingsLayout
    {
        static ConfigFile owner;
        static readonly object gate = new object();
        internal static void Install(ConfigFile config)
        {
            owner = config;
            new Harmony(Plugin.GUID + ".settings-layout").Patch(AccessTools.Method(typeof(ConfigFile), "Save"),
                postfix: new HarmonyMethod(typeof(SettingsLayout), nameof(AfterSave)));
        }
        static int Rank(string section)
        {
            if (section.Equals("General", StringComparison.OrdinalIgnoreCase)) return 0;
            if (section.Equals("VEHICLE", StringComparison.OrdinalIgnoreCase)) return 1;
            if (section.Equals("CAMERA", StringComparison.OrdinalIgnoreCase)) return 2;
            if (section.Equals("Occlusion", StringComparison.OrdinalIgnoreCase)) return 3;
            if (section.Equals("climbing", StringComparison.OrdinalIgnoreCase)) return 100;
            if (section.Equals("Debug", StringComparison.OrdinalIgnoreCase)) return 101;
            return 50;
        }
        internal static void Arrange(ConfigFile config)
        {
            // Apocasetter enumerates BepInEx entries in dictionary order.
            var property = typeof(ConfigFile).GetProperty("Entries", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var entries = property?.GetValue(config, null) as IDictionary;
            if (entries != null)
            {
                var snapshot = new List<DictionaryEntry>();
                foreach (DictionaryEntry entry in entries) snapshot.Add(entry);
                var ordered = snapshot.OrderBy(e => Rank(((ConfigDefinition)e.Key).Section)).ToArray();
                entries.Clear();
                foreach (var entry in ordered) entries.Add(entry.Key, entry.Value);
            }
            config.Save();
        }
        static void AfterSave(ConfigFile __instance)
        {
            if (!ReferenceEquals(owner, __instance)) return;
            try
            {
                lock (gate)
                {
                    string source = File.ReadAllText(owner.ConfigFilePath);
                    var headings = Regex.Matches(source, @"(?m)^\[([^\]\r\n]+)\][ \t]*\r?$");
                    if (headings.Count == 0) return;
                    var blocks = new List<KeyValuePair<string, string>>();
                    for (int i = 0; i < headings.Count; i++)
                    {
                        int end = i + 1 < headings.Count ? headings[i + 1].Index : source.Length;
                        blocks.Add(new KeyValuePair<string, string>(headings[i].Groups[1].Value,
                            source.Substring(headings[i].Index, end - headings[i].Index)));
                    }
                    string ordered = source.Substring(0, headings[0].Index) + string.Concat(blocks.OrderBy(b => Rank(b.Key)).Select(b => b.Value));
                    if (ordered != source) File.WriteAllText(owner.ConfigFilePath, ordered, new UTF8Encoding(false));
                }
            }
            catch (Exception e) { Plugin.Warn("Settings section order: " + e.Message); }
        }
    }
}
