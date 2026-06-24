using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace MakeTea;

// Useful article: https://harmony.pardeike.net/articles/patching-prefix.html
[HarmonyPatch(typeof(BlockLiquidContainerBase), "tryEatStop")]
public class BlockLiquidContainerBasePatch
{
    [HarmonyPrefix]
    public static void TryEatStopPrefix(
        BlockLiquidContainerBase __instance, out TryEatStopState __state,
        float secondsUsed, ItemSlot slot, EntityAgent byEntity)
    {
        __state = new TryEatStopState();

        if (slot.Itemstack == null) return;

        var handle = true;
        handle &= slot.Itemstack != null;
        var contentStack = __instance.GetContent(slot.Itemstack);
        handle = handle && contentStack != null && contentStack.StackSize > 0;
        handle = handle && (contentStack.Item?.WildCardMatch("teaportion-*") ?? false);
        handle = handle && contentStack.Item.Attributes["makeTeaPortionProps"].Exists;
        handle = handle && byEntity.HasBehavior<EntityBehaviorTemporalStabilityAffected>();

        if (handle)
        {
            __state.OldSize = contentStack?.StackSize ?? 0;
            __state.ContentStack = contentStack;
        }
    }

    [HarmonyPostfix]
    public static void TryEatStopPostfix(
        BlockLiquidContainerBase __instance, TryEatStopState __state,
        float secondsUsed, ItemSlot slot, EntityAgent byEntity)
    {
        if (__state.OldSize <= 0 || __state.ContentStack == null || slot.Itemstack == null) return;

        var consumedSize = __state.OldSize - (__instance.GetContent(slot.Itemstack)?.StackSize ?? 0);
        if (consumedSize <= 0) return;

        var dummySlot = typeof(BlockLiquidContainerBase)
            .GetMethod("GetContentInDummySlot", BindingFlags.Instance | BindingFlags.NonPublic)?
            .Invoke(__instance, [slot, __state.ContentStack]) as ItemSlot;
        var api = typeof(BlockLiquidContainerBase)
            .GetField("api", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(__instance) as ICoreAPI;

        if (api == null) return;

        var states = __state.ContentStack.Collectible.UpdateAndGetTransitionStates(api.World, dummySlot);
        var spoilState = states.FirstOrDefault(s => s.Props.Type == EnumTransitionType.Perish)?.TransitionLevel ?? 0f;
        var containableProps = BlockLiquidContainerBase.GetContainableProps(__state.ContentStack);
        if (containableProps == null) return;

        var stabilityGain = __state.ContentStack.Item.Attributes["makeTeaPortionProps"]["stabilityGain"].AsFloat();
        var stabilityBehavior = byEntity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
        if (stabilityBehavior == null) return;

        var stabilityGainTotal = stabilityGain * consumedSize * Math.Max(0.0f, 1f - spoilState) / containableProps.ItemsPerLitre;
        // api.Logger.Debug(
        //     "MakeTeaMod: entity [{0}] with stability {1:F3}, gain +{2:F3} stability using [{3}] liquid ({4:P1} spoiled) from container [{5}]",
        //     byEntity.GetName(), stabilityBehavior.OwnStability, stabilityGainTotal, __state.ContentStack.GetName(),
        //     spoilState, slot.Itemstack.GetName());

        stabilityBehavior.OwnStability += stabilityGainTotal;
    }

    public class TryEatStopState
    {
        public ItemStack ContentStack;
        public int OldSize;
    }
}