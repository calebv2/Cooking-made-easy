using CustomRecipesAPI;
using MelonLoader;

[assembly: MelonInfo(typeof(Easycook.Core), "Easycook", "1.2.1", "ATT", null)]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace Easycook
{
    public sealed class Core : MelonMod
    {
        internal static MelonLogger.Instance Logger { get; private set; }

        public override void OnInitializeMelon()
        {
            Logger = LoggerInstance;
            Logger.Msg("Easycook initializing.");
            ForgeOutputCollection.Install(HarmonyInstance);
            CustomRecipesAPI.Core.SetUpRecipes += SmelterRecipeRegistration.Register;
        }
    }
}
