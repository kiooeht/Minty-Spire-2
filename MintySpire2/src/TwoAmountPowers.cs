using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;
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
        { typeof(PaleBlueDotPower), power => {
            // Displays X/5 as amount2 where X is cards played this turn
            var cardCount = CombatManager.Instance.History.CardPlaysFinished.Count(c =>
                c.RoundNumber == power.CombatState.RoundNumber &&
                c.CardPlay.Card.Owner == power.Owner.Player);
            var threshold = power.DynamicVars[PaleBlueDotPower.cardPlayThresholdKey].IntValue;
            return $"{cardCount}/{threshold}";
        } },
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
                typeof(PaleBlueDotPower).Method(nameof(PaleBlueDotPower.ModifyHandDraw)),
            ];
        }

        [HarmonyPatch]
        static class SpecificFixes
        {
            private static readonly List<Type> AfterCardPlayedPowers = [
                typeof(PaleBlueDotPower),
            ];
        
            private static void AfterCardPlayed(AbstractModel model)
            {
                if (model is PowerModel power) {
                    if (AfterCardPlayedPowers.Contains(power.GetType())) {
                        CallRefreshAmount(power);
                    }
                }
            }
        
            static MethodBase TargetMethod() {
                var method = typeof(Hook).Method(nameof(Hook.AfterCardPlayed));
                var stateMachineAttribute = method.GetCustomAttribute<AsyncStateMachineAttribute>();
                var moveNextMethod =
                    stateMachineAttribute?.StateMachineType.GetMethod("MoveNext",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                return moveNextMethod!;
            }

            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> RefreshAfterCardPlayed(IEnumerable<CodeInstruction> instructions)
            {
                var codeMatcher = new CodeMatcher(instructions);

                codeMatcher
                    .MatchStartForward(
                        CodeMatch.Calls(typeof(AbstractModel).Method(nameof(AbstractModel.InvokeExecutionFinished)))
                    )
                    .ThrowIfInvalid("Failed to find InvokeExecutionFinished()")
                    .InsertAndAdvance(
                        new CodeInstruction(OpCodes.Dup),
                        CodeInstruction.Call<AbstractModel>(model => AfterCardPlayed(model))
                    );

                return codeMatcher.Instructions();
            }
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
