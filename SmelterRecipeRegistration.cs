using System;
using System.Linq;
using System.Collections.Generic;
using Alta.Cooking;
using Alta.Networking;
using System.Reflection;
using Alta.Blacksmithing;
using Alta.Inventory;
using UnityEngine;

namespace Easycook
{
    internal static class SmelterRecipeRegistration
    {
        private sealed class CookingRecipe
        {
            internal readonly uint InputHash;
            internal readonly uint OutputHash;
            internal readonly uint RecipeHash;
            internal readonly string Name;

            internal CookingRecipe(uint inputHash, uint outputHash, uint recipeHash, string name)
            {
                InputHash = inputHash;
                OutputHash = outputHash;
                RecipeHash = recipeHash;
                Name = "Easycook - " + name;
            }
        }

        // Preserve IDs already used by saved games; other IDs depend only on the
        // input prefab, never resource enumeration order or randomized string hashes.
        private static uint RecipeHash(uint inputHash)
        {
            switch (inputHash)
            {
                case 5972u: return 0xEC040001u;
                case 43430u: return 0xEC040002u;
                case 47394u: return 0xEC040003u;
                case 43294u: return 0xEC040004u;
                case 2562u: return 0xEC040005u;
                case 50252u: return 0xEC040006u;
                default: return 0xEC000000u | (inputHash & 0x00FFFFFFu);
            }
        }

        private static CookingRecipe[] DiscoverRecipes()
        {
            var recipes = new List<CookingRecipe>();
            var seen = new HashSet<uint>();
            var cookedField = GetRequiredField(typeof(Cookable), "cookedVersion");
            foreach (var item in Item.All.Where(item => item != null).OrderBy(item => item.Hash))
            {
                try
                {
                    var prefab = item.Prefab;
                    if (prefab == null || seen.Contains(prefab.Hash))
                        continue;

                    var cookable = prefab.GetComponentInPrefab<Cookable>();
                    if (cookable == null || !cookable.IsCookable)
                        continue;

                    var cooked = cookedField.GetValue(cookable) as NetworkPrefab;
                    if (cooked == null || cooked.Hash == prefab.Hash
                        || Item.GetByPrefabHash(cooked.Hash) == null)
                    {
                        Core.Logger.Warning("Skipping cookable item without a distinct cooked pickup: " + item.name);
                        continue;
                    }

                    seen.Add(prefab.Hash);
                    recipes.Add(new CookingRecipe(prefab.Hash, cooked.Hash, RecipeHash(prefab.Hash), item.name));
                }
                catch (Exception error)
                {
                    Core.Logger.Error("Could not inspect cooking definition for " + item.name + ": " + error);
                }
            }

            Core.Logger.Msg("Discovered " + recipes.Count + " cookable item recipes.");
            return recipes.ToArray();
        }

        private static bool registrationAttempted;

        internal static void Register()
        {
            if (registrationAttempted)
            {
                return;
            }

            registrationAttempted = true;

            try
            {
                Item.CheckItems();
                SmeltingRecipe.CheckItems();
                var recipes = DiscoverRecipes();

                var inputField = GetRequiredField(typeof(SmeltingRecipe), "input");
                var outputField = GetRequiredField(typeof(SmeltingRecipe), "output");
                var template = SmeltingRecipe.All.FirstOrDefault(candidateRecipe =>
                    HasSingleInputAndOutput(candidateRecipe, inputField, outputField));

                if (template == null)
                {
                    throw new InvalidOperationException("No one-input, one-output smelting recipe template was found.");
                }

                var upgradeSets = SmelterUpgrades.All.Where(upgrades => upgrades != null).ToArray();
                if (upgradeSets.Length == 0)
                {
                    throw new InvalidOperationException("No smelter upgrade sets were found.");
                }

                foreach (var definition in recipes)
                {
                    try
                    {
                        RegisterRecipe(definition, template, upgradeSets);
                    }
                    catch (Exception error)
                    {
                        Core.Logger.Error(definition.Name + " registration failed: " + error);
                    }
                }

                ForgeOutputCollection.ApplyToLoadedForges();
            }
            catch (Exception error)
            {
                Core.Logger.Error("Easycook recipe registration failed: " + error);
            }
        }

        private static void RegisterRecipe(CookingRecipe definition, SmeltingRecipe template, SmelterUpgrades[] upgradeSets)
        {
            var rawItem = Item.GetByPrefabHash(definition.InputHash);
            var cookedItem = Item.GetByPrefabHash(definition.OutputHash);
            if (rawItem == null || cookedItem == null)
            {
                Core.Logger.Error(definition.Name + " could not find "
                    + (rawItem == null ? "input prefab #" + definition.InputHash : "output prefab #" + definition.OutputHash)
                    + ".");
                return;
            }

            var existingRecipe = SmeltingRecipe.All.FirstOrDefault(candidateRecipe => candidateRecipe.Hash == definition.RecipeHash);
            if (existingRecipe != null)
            {
                Core.Logger.Error(definition.Name + " recipe hash 0x" + definition.RecipeHash.ToString("X8")
                    + " is already used by '" + existingRecipe.name + "'.");
                return;
            }

            var recipe = UnityEngine.Object.Instantiate(template);
            recipe.name = definition.Name;
            SetField(recipe, "hash", unchecked((int)definition.RecipeHash));
            SetField(recipe, "input", new[] { CreateItemCount(rawItem, 1) });
            SetField(recipe, "output", new[] { CreateItemCount(cookedItem, 1) });

            CustomRecipesAPI.Core.RegisterSmeltingRecipe(recipe);
            foreach (var upgrades in upgradeSets)
            {
                CustomRecipesAPI.Core.AddSmeltingRecipeToSmelterUpgrades(recipe, upgrades, false);
            }

            if (!CustomRecipesAPI.Core.itemsToAddToSmelter.Contains(rawItem))
            {
                CustomRecipesAPI.Core.itemsToAddToSmelter.Add(rawItem);
            }

            ForgeOutputCollection.AddOutput(cookedItem);

            Core.Logger.Msg("Registered forge recipe: 1 " + rawItem.name + " -> 1 " + cookedItem.name + ".");
        }

        internal static ItemCount CreateItemCount(Item item, int count)
        {
            var value = Activator.CreateInstance(typeof(ItemCount), true);
            SetField(value, "item", item);
            SetField(value, "count", count);
            return (ItemCount)value;
        }

        private static bool HasSingleInputAndOutput(
            SmeltingRecipe recipe,
            FieldInfo inputField,
            FieldInfo outputField)
        {
            var inputs = inputField.GetValue(recipe) as ItemCount[];
            var outputs = outputField.GetValue(recipe) as ItemCount[];
            return inputs != null && inputs.Length == 1 && outputs != null && outputs.Length == 1;
        }

        private static void SetField(object target, string name, object value)
        {
            GetRequiredField(target.GetType(), name).SetValue(target, value);
        }

        private static FieldInfo GetRequiredField(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }
            }

            throw new MissingFieldException(type.FullName, name);
        }
    }
}
