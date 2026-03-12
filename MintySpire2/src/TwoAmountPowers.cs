using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using MintySpire2.MintySpire2.src.util;

namespace MintySpire2.MintySpire2.src;

[HarmonyPatch(typeof(NPower))]
static class TwoAmountPowers
{
    private static readonly Dictionary<Type, Func<PowerModel, Amount2Data>> DisplaySecondAmount = new() {
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
        { typeof(VulnerablePower), power => {
            // Displays Vulnerable's % increase if it's not 50%
            var player = LocalContext.GetMe(RunManager.Instance.State);
            var mult = power.ModifyDamageMultiplicative(power.Owner, 1M, ValueProp.Move, player?.Creature, null);
            if (mult != power.DynamicVars[VulnerablePower._damageIncrease].BaseValue) {
                mult = (mult - 1M) * 100M;
                return mult.ToString("0.##") + "%";
            }
            else {
                return string.Empty;
            }
        } },
        { typeof(WeakPower), power => {
            // Displays Weak's % decrease if it's not 25%
            var player = LocalContext.GetMe(RunManager.Instance.State);
            var mult = power.ModifyDamageMultiplicative(player?.Creature, 1M, ValueProp.Move, power.Owner, null);
            if (mult != power.DynamicVars[WeakPower._damageDecrease].BaseValue) {
                mult = (1M - mult) * 100M;
                return mult.ToString("0.##") + "%";
            }
            else {
                return string.Empty;
            }
        } },
        { typeof(ToricToughnessPower), power => power.DynamicVars.Block.IntValue.ToString() },
        { typeof(InfernoPower), power => {
                var selfDamage = power.DynamicVars[InfernoPower._selfDamageKey].IntValue;
                return selfDamage != 0 ? new Amount2Data(selfDamage.ToString(), PowerModel._debuffAmountLabelColor) : string.Empty;
            }
        },
        { typeof(CrimsonMantlePower), power => {
                var selfDamage = power.DynamicVars[CrimsonMantlePower._selfDamageKey].IntValue;
                return selfDamage != 0 ? new Amount2Data(selfDamage.ToString(), PowerModel._debuffAmountLabelColor) : string.Empty;
            }
        },
    };

    private class Amount2Data(string text, Color? color = null)
    {
        public string Text = text;
        public Color? Color = color;

        public static implicit operator Amount2Data(string text) => new Amount2Data(text);
    }
    
    private static readonly ConditionalWeakTable<NPower, MegaLabel> Amount2Labels = new();

    [HarmonyPatch(nameof(NPower._Ready))]
    [HarmonyPrefix]
    static void AddSecondAmountLabel(NPower __instance)
    {
        var amount1Label = __instance.GetNode<MegaLabel>("%AmountLabel");
        var amount2Label = Amount2Labels.GetValue(__instance, _ => (MegaLabel) amount1Label.Duplicate());
        amount2Label.Name = "Amount2Label";
        amount2Label.UniqueNameInOwner = true;
        amount2Label.Visible = false;
        __instance.AddChild(amount2Label);
        __instance.MoveChild(amount2Label, amount1Label.GetIndex());
    }
    
    [HarmonyPatch(nameof(NPower.RefreshAmount))]
    [HarmonyPostfix]
    static void SetSecondAmountText(NPower __instance)
    {
        if (__instance._model == null) return;
        
        var amount2Label = Amount2Labels.GetOrCreateValue(__instance);
        amount2Label.Visible = false;
        DisplaySecondAmount.TryGetValue(__instance.Model.GetType(), out var func);
        if (func != null) {
            var amount2 = func.Invoke(__instance.Model);
            if (!string.IsNullOrEmpty(amount2.Text)) {
                amount2Label.Visible = true;
                amount2Label.AddThemeColorOverride(ThemeConstants.Label.fontColor, amount2.Color ?? __instance.Model.AmountLabelColor);
                amount2Label.SetTextAutoSize(amount2.Text);
                var fontSize = amount2Label.GetThemeFontSize(ThemeConstants.Label.fontSize);
                amount2Label.Position = __instance._amountLabel.Position + new Vector2(0, -(fontSize + 2));
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
        
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> MethodsToPostfixRefreshAmount2()
        {
            return [
                typeof(VoidFormPower).Method(nameof(VoidFormPower.AfterCardPlayed)),
                typeof(VoidFormPower).Method(nameof(VoidFormPower.BeforeSideTurnStart)),
                typeof(JugglingPower).Method(nameof(JugglingPower.AfterApplied)),
                typeof(JugglingPower).Method(nameof(JugglingPower.AfterCardPlayed)),
                typeof(JugglingPower).Method(nameof(JugglingPower.AfterTurnEnd)),
                typeof(PaleBlueDotPower).Method(nameof(PaleBlueDotPower.ModifyHandDraw)),
                typeof(ToricToughnessPower).Method(nameof(ToricToughnessPower.SetBlock)),
                typeof(InfernoPower).Method(nameof(InfernoPower.IncrementSelfDamage)),
                typeof(CrimsonMantlePower).Method(nameof(InfernoPower.IncrementSelfDamage)),
            ];
        }

        [HarmonyPatch]
        static class SpecificFixes
        {
            private static readonly Dictionary<MethodBase, HashSet<Type>> AfterHookPowers = new() {
                { typeof(Hook).Method(nameof(Hook.AfterCardPlayed)).PatchAsync(), [
                    typeof(PaleBlueDotPower),
                ] },
                { typeof(Hook).Method(nameof(Hook.AfterPowerAmountChanged)).PatchAsync(), [
                    typeof(VulnerablePower),
                    typeof(WeakPower),
                ] },
            };
        
            private static void AfterHook(AbstractModel model, MethodBase method)
            {
                // Get original, unpatched method so we can use it as a lookup key properly
                if (method is MethodInfo methodInfo) {
                    method = Harmony.GetOriginalMethod(methodInfo);
                }

                if (model is PowerModel power) {
                    if (AfterHookPowers.TryGetValue(method, out var powers) && powers.Contains(power.GetType())) {
                        CallRefreshAmount(power);
                    }
                }
            }
        
            [HarmonyTargetMethods]
            static IEnumerable<MethodBase> HooksToRefreshAmount2After()
            {
                return AfterHookPowers.Keys;
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
                        CodeInstruction.Call(() => MethodBase.GetCurrentMethod()),
                        new CodeInstruction(OpCodes.Call, typeof(SpecificFixes).Method(nameof(AfterHook)))
                    );

                return codeMatcher.Instructions();
            }
            
            [HarmonyPatch(typeof(PowerModel), nameof(PowerModel.RemoveInternal))]
            static class VulnWeakOnDebilitateRemoval
            {
                [HarmonyPostfix]
                static void RefreshVulnWeak(PowerModel __instance)
                {
                    if (__instance is not DebilitatePower) return;
                    foreach (var powerModel in __instance.Owner.Powers.Where(p => p is VulnerablePower or WeakPower)) {
                        CallRefreshAmount(powerModel);
                    }
                }
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
