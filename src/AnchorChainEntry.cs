using System;
using System.IO;
using System.Reflection;
using AnchorChain;
using BepInEx.Bootstrap;
using UnityEngine;

namespace AlternativeWeaponsControl
{
    /// <summary>
    /// Anchor Chain entry point: lets the mod be distributed as a Steam Workshop / StreamingAssets mod
    /// (a folder with _info.ini + this dll, enabled in the in-game Mods menu).
    /// The same dll also works as a plain BepInEx plugin (BepInEx/plugins) - in that case this class is not used.
    /// </summary>
    [ACPlugin("io.github.rgreat.seapower-alternative-weapons-control", "Alternative Weapons Control", AwcPlugin.PluginVersion)]
    public class AnchorChainEntry : IAnchorChainMod
    {
        public void TriggerEntryPoint()
        {
            var host = Chainloader.ManagerObject; // BepInEx's own GameObject that hosts all plugins
            if (host == null)
            {
                Debug.LogWarning("[Alternative Weapons Control] BepInEx manager object not found; not started.");
                return;
            }

            // Already loaded as a BepInEx plugin (dll also lies in BepInEx/plugins)? Then do not start a second copy.
            if (Chainloader.PluginInfos.ContainsKey(AwcPlugin.PluginGuid) || host.GetComponent<AwcPlugin>() != null)
            {
                Debug.LogWarning("[Alternative Weapons Control] already loaded (a copy is also in BepInEx/plugins); keep only one copy.");
                return;
            }

            // Anchor Chain starts every mod folder, ticked or not: the plugin follows the tick of the game's Mods menu
            // at run time and leaves the game untouched while it is off.
            ModState.Controlled = true;
            host.AddComponent<AwcPlugin>(); // BaseUnityPlugin: BepInEx logger and config work as usual
        }
    }

    /// <summary>Whether this mod is ticked in the game's Mods menu.</summary>
    static class ModState
    {
        /// <summary>True when started by Anchor Chain (a mod folder); a copy in BepInEx/plugins is always on.</summary>
        public static bool Controlled;

        static string _id;
        public static string ModId { get { return _id ?? (_id = OwnModId()); } }

        /// <summary>Current state; null when the game's mod list cannot be read.</summary>
        public static bool? Read()
        {
            return Controlled ? IsModEnabled(ModId) : true;
        }

        /// <summary>
        /// The id the game uses for this mod in its load order: the Workshop item id (the folder under
        /// steamapps/workshop/content/1286220) or the folder name under StreamingAssets.
        /// </summary>
        public static string OwnModId()
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(ModState).Assembly.Location) ?? "";
                string path = dir.Replace('/', '\\').TrimEnd('\\') + "\\";
                foreach (var marker in new[] { "\\StreamingAssets\\", "\\1286220\\" })
                {
                    int i = path.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    string rest = path.Substring(i + marker.Length);
                    int j = rest.IndexOf('\\');
                    return j < 0 ? rest : rest.Substring(0, j);
                }
                return Path.GetFileName(dir); // unusual layout: the dll's own folder
            }
            catch { return ""; }
        }

        /// <summary>
        /// Whether the game's Mods menu has this mod ticked: [LoadOrder] of usersettings.ini lists
        /// ModNDirectory = "folder name,True|False,steam id" (folder name: the Workshop item id, or the folder in StreamingAssets).
        /// A mod missing from the list is off, as the game also adds new StreamingAssets folders unticked.
        /// Null when the list cannot be read (game changed): the caller then treats the mod as on.
        /// </summary>
        public static bool? IsModEnabled(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return null;
            try
            {
                var utils = Type.GetType("SeaPower.Utils, Seapower-Scripts");
                var open = utils == null ? null : utils.GetMethod("openPersistentIniFile", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }, null);
                var ini = open == null ? null : open.Invoke(null, new object[] { "", "usersettings.ini", true, false });
                if (ini == null) return null;

                const BindingFlags inst = BindingFlags.Public | BindingFlags.Instance;
                var readInt = ini.GetType().GetMethod("readValue", inst, null, new[] { typeof(string), typeof(string), typeof(int), typeof(bool) }, null);
                var readStr = ini.GetType().GetMethod("readValue", inst, null, new[] { typeof(string), typeof(string), typeof(string), typeof(bool) }, null);
                if (readInt == null || readStr == null) return null;

                int count = (int)readInt.Invoke(ini, new object[] { "LoadOrder", "NumberOfModFiles", 0, false });
                for (int i = 1; i <= count; i++)
                {
                    var value = readStr.Invoke(ini, new object[] { "LoadOrder", "Mod" + i + "Directory", string.Empty, false }) as string;
                    if (string.IsNullOrEmpty(value)) continue;
                    var parts = value.Split(',');
                    if (!string.Equals(parts[0].Trim(), modId, StringComparison.OrdinalIgnoreCase)) continue;
                    // same rule as the game (FileManager): no flag or an unreadable one counts as ticked
                    bool on = true;
                    if (parts.Length > 1 && !bool.TryParse(parts[1].Trim(), out on)) on = true;
                    return on;
                }
                return false; // not in the list: the game does not load it either
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Alternative Weapons Control] reading the mod list failed: " + (e.InnerException ?? e).Message);
                return null;
            }
        }
    }
}
