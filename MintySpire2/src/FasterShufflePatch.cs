using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MintySpire2.MintySpire2.src.util;

namespace MintySpire2.MintySpire2.src;

public class FasterShufflePatch
{

    private static float GetMultiplier()
    {
        return 0.5f;
    }
    
    
    
    
    //Transpiler patch
    //With extra because it's an async method. Should be reduced if BaseLib is added to dependencies
    [HarmonyPatch]
    public static class CardPileCmdShufflePatch
    {
        static MethodBase TargetMethod()
        {
            return typeof(CardPileCmd).Method(nameof(CardPileCmd.Shuffle)).PatchAsync();
        }
        private static float MultiplyShuffleSpeed(float normalTime)
        {
            return normalTime *  GetMultiplier();
        }
        
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
        
            codeMatcher
                .MatchEndForward(
                    new CodeMatch(OpCodes.Conv_R4),
                    new CodeMatch(OpCodes.Div),
                    CodeMatch.Calls(typeof(Mathf).GetMethod(nameof(Mathf.Min), [typeof(float), typeof(float)])),
                    new CodeMatch(OpCodes.Stfld)
                )
                .ThrowIfInvalid("Didn't find a match for Fast Shuffle Patch")
                .InsertAndAdvance(
                    CodeInstruction.Call<float,float>( time => MultiplyShuffleSpeed(time))
                );
        
            return codeMatcher.Instructions();
        }
    }
}
