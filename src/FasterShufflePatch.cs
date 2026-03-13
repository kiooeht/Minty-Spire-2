using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MintySpire2.util;

namespace MintySpire2;

public class FasterShufflePatch
{

    private static float GetMultiplier()
    {
        return 0.5f;
    }
    
    //Transpiler patch
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
        
        
        //Harmony is trying to apply the patch twice for some reason
        //Using a Prepare method to prevent it
        private static bool didPatch = false;

        static bool Prepare(MethodBase original)
        {
            if (original == null)
            {
                return true;
            }
            if (didPatch)
            {
                return false;
            }
            didPatch = true;
            return true;
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
