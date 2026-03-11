using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace MintySpire2.MintySpire2.src;

[HarmonyPatch(typeof(NPower))]
static class TwoAmountPowers
{
    private static readonly Dictionary<Type, Func<PowerModel, string>> DisplaySecondAmount = new() {
        { typeof(OutbreakPower), power => power.Amount.ToString() },
        { typeof(PanachePower), power => power.Amount.ToString() },
        { typeof(TheBombPower), power => power.DynamicVars.Damage.IntValue.ToString() },
        { typeof(VoidFormPower), power => Math.Max(0, power.Amount - power.GetInternalData<VoidFormPower.Data>().cardsPlayedThisTurn).ToString() },
        { typeof(JugglingPower), power => power.GetInternalData<JugglingPower.Data>().attacksPlayedThisTurn.ToString() },
    };

    [HarmonyPatch(nameof(NPower._Ready))]
    [HarmonyPostfix]
    static void AddSecondAmountLabel(NPower __instance)
    {
        __instance._amountLabel.AddThemeConstantOverride("line_spacing", -2);
        __instance._amountLabel.SetVGrowDirection(Control.GrowDirection.Begin);
    }
    
    [HarmonyPatch(nameof(NPower.RefreshAmount))]
    [HarmonyPostfix]
    static void SetSecondAmountText(NPower __instance)
    {
        if (__instance._model == null) return;
        
        DisplaySecondAmount.TryGetValue(__instance.Model.GetType(), out var func);
        if (func != null) {
            var text = __instance._amountLabel.Text;
            var amount2 = func.Invoke(__instance.Model);
            if (!string.IsNullOrEmpty(amount2)) {
                __instance._amountLabel.SetTextAutoSize($"{amount2}\n{text}");
            }
        }
    }
    
    [HarmonyPatch]
    static class ExtraRefreshAmountCalls
    {
        [HarmonyPostfix]
        static void CallRefreshAmount(PowerModel __instance)
        {
            doNotFlashOnAmountRefresh = true;
            __instance.InvokeDisplayAmountChanged();
            doNotFlashOnAmountRefresh = true;
        }
        
        static IEnumerable<MethodBase> TargetMethods()
        {
            return [
                typeof(VoidFormPower).Method(nameof(VoidFormPower.AfterCardPlayed)),
                typeof(VoidFormPower).Method(nameof(VoidFormPower.BeforeSideTurnStart)),
                typeof(JugglingPower).Method(nameof(JugglingPower.AfterApplied)),
                typeof(JugglingPower).Method(nameof(JugglingPower.AfterCardPlayed)),
                typeof(JugglingPower).Method(nameof(JugglingPower.AfterTurnEnd)),
            ];
        }
    }
    
    private static bool doNotFlashOnAmountRefresh = false;

    [HarmonyPatch(nameof(NPower.OnDisplayAmountChanged))]
    [HarmonyPrefix]
    static bool DontFlashIfOnlyRefreshingAmount2(NPower __instance)
    {
        if (doNotFlashOnAmountRefresh) {
            __instance.RefreshAmount();
            return false;
        }

        return true;
    }
}
