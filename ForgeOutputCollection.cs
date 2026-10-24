using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Blacksmithing;
using Alta.Inventory;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace Easycook
{
    internal static class ForgeOutputCollection
    {
        private static readonly HashSet<Item> outputs = new HashSet<Item>();
        private static readonly FieldInfo oreDocks = AccessTools.Field(typeof(Smelter), "oreDocks");
        private static readonly FieldInfo fuelDocks = AccessTools.Field(typeof(Smelter), "fuelDocks");
        private static readonly FieldInfo mouldDock = AccessTools.Field(typeof(Smelter), "mouldDock");

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Smelter), "Initialize"),
                postfix: new HarmonyMethod(typeof(ForgeOutputCollection), nameof(OnForgeInitialized)));
        }

        internal static void AddOutput(Item item)
        {
            if (item != null)
                outputs.Add(item);
        }

        internal static void ApplyToLoadedForges()
        {
            // Resources.LoadAll only finds assets in Resources folders. Dock
            // settings referenced by a prefab need not be directly loadable there.
            // Patch existing instances and loaded templates; the Initialize hook
            // handles later spawns and settings loaded with later scenes.
            foreach (var smelter in Resources.FindObjectsOfTypeAll<Smelter>())
                OnForgeInitialized(smelter);
        }

        private static void OnForgeInitialized(Smelter __instance)
        {
            if (__instance == null || outputs.Count == 0)
                return;

            try
            {
                var inputs = new HashSet<PickupDock>();
                inputs.UnionWith((PickupDock[])oreDocks.GetValue(__instance) ?? new PickupDock[0]);
                inputs.UnionWith((PickupDock[])fuelDocks.GetValue(__instance) ?? new PickupDock[0]);
                inputs.Add((PickupDock)mouldDock.GetValue(__instance));

                var prefab = __instance.GetComponentInParent<NetworkPrefab>();
                var root = prefab != null ? prefab.gameObject : __instance.gameObject;
                var settingsSeen = new HashSet<PickupDockSettings>();
                int changed = 0;

                // Use the actual automatic collection areas on this forge, not
                // localized/stripped asset names. Never add food to input docks.
                foreach (var area in root.GetComponentsInChildren<AutomaticDockArea>(true))
                {
                    foreach (var dock in area.Docks ?? new PickupDock[0])
                    {
                        if (dock == null || inputs.Contains(dock) || dock.Settings == null
                            || !settingsSeen.Add(dock.Settings))
                            continue;

                        foreach (var item in outputs)
                        {
                            if (!dock.Settings.IncludedItems.Contains(item))
                            {
                                dock.Settings.IncludedItems.Add(item);
                                changed++;
                            }
                        }
                    }
                }

                if (settingsSeen.Count == 0)
                    Core.Logger.Warning("No automatic output docks found on forge '" + __instance.name + "'.");
                else if (changed > 0)
                    Core.Logger.Msg("Enabled cooked item collection in " + settingsSeen.Count + " forge output filter(s).");
            }
            catch (Exception error)
            {
                Core.Logger.Error("Could not configure forge output collection: " + error);
            }
        }
    }
}
