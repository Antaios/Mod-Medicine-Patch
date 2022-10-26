using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using RimWorld;
using Verse;

namespace ModMedicinePatch
{
	// 'Replace' MedicalCareSetter with new version
	[HarmonyPatch(typeof(RimWorld.MedicalCareUtility), "MedicalCareSetter")]
	public static class MedicalCareSetter
	{
		[HarmonyPrefix]
		public static bool _Prefix()
		{
			return false;
		}

		[HarmonyPostfix]
		public static void _Postfix(Rect rect, ref MedicalCareCategory medCare)
		{
			ModMedicinePatch.DynamicMedicalCareSetter(rect, ref medCare);
		}
	}

	//'Replace' GetLabel, so tooltips make sense
	[HarmonyPatch(typeof(RimWorld.MedicalCareUtility), "GetLabel")]
	public static class GetLabel
	{
		[HarmonyPrefix]
		public static bool _Prefix(this MedicalCareCategory cat, ref bool __state)
		{
			//if MedicalCareCategory is within base game range, use original method - EXCEPT glitterworld.
			__state = false;
			if ((int)cat <= 3) __state = true;
			return __state;
		}

		[HarmonyPostfix]
		public static void _Postfix(this MedicalCareCategory cat, ref string __result, bool __state)
		{
			if (!__state)
			{
				__result = ModMedicinePatch.GetDynamicLabel(cat);
			}
		}
	}

	//'Replace' AllowsMedicine, so extra values in MedicalCareCategory are mapped to medicines correctly
	[HarmonyPatch(typeof(RimWorld.MedicalCareUtility), "AllowsMedicine")]
	public static class AllowsMedicine
	{
        [HarmonyPrefix]
        public static bool _Prefix(this MedicalCareCategory cat, ThingDef meds, ref bool __state,ref bool __result)
        {
            //if MedicalCareCategory is within base game range, use original method - EXCEPT glitterworld.
            __state = false;
            if ((int)cat <= 3) __state = true;

            if (!__state)
            {
                __result = ModMedicinePatch.GetDynamicAllowsMedicine(cat, meds);
            }
            return __state;
        }
        /*[HarmonyPrefix]
		public static bool _Prefix(this MedicalCareCategory cat, ref bool __state)
		{
			//if MedicalCareCategory is within base game range, use original method - EXCEPT glitterworld.
			__state = false;
			if ((int)cat <= 3) __state = true;
			return __state;
		}

		[HarmonyPostfix]
		public static void _Postfix(this MedicalCareCategory cat, ThingDef meds, ref bool __result, bool __state)
		{
			if (!__state)
			{
				__result = ModMedicinePatch.GetDynamicAllowsMedicine(cat, meds);
			}
		}*/
    }

	//'Replace' MedicalCareSelectButton to take into account new medicines
	[HarmonyPatch(typeof(RimWorld.MedicalCareUtility), "MedicalCareSelectButton")]
	public static class MedicalCareSelectButton
	{
		[HarmonyPrefix]
		public static bool _Prefix(Rect rect, Pawn pawn)
		{
			ModMedicinePatch.DynamicMedicalCareSelectButton(rect, pawn);
			return false;
		}
	}

    //Patch HealthTabUtility, so we can capture the current pawn
    public static class DrawOverviewTab
    {
        public static bool _Prefix(Pawn pawn)
        {
            ModMedicinePatch.currentMedCarePawn = pawn;
            return true;
        }
        public static void _Postfix()
        {
            ModMedicinePatch.currentMedCarePawn = null;
        }
    }

	public static class SM_Patches
    {
		public static bool HediffRowPriorityCare_LabelButton_Prefix(Rect rect, string text, Hediff hediff)
        {
			ModMedicinePatch.SmartMedicineLabelButton(rect, text, hediff);
			return false;
		}
		public static bool PriorityHediff_Prefix(Hediff __instance, ref float __result)
        {
			if (SmartMedicine.PriorityCareComp.Get().TryGetValue(__instance, out MedicalCareCategory hediffCare))
			{
				MedicalCareCategory defaultCare = __instance.pawn.playerSettings.medCare;
				if (ModMedicinePatch.pharmacist)
                {
					defaultCare = GetCarePharmacist(__instance.pawn);
                }
				int diff = ModMedicinePatch.medList.IndexOf(ModMedicinePatch.GetMedicineByCare(hediffCare)) - ModMedicinePatch.medList.IndexOf(ModMedicinePatch.GetMedicineByCare(defaultCare));
				//float diff = ModMedicinePatch.GetCarePotency(hediffCare) - ModMedicinePatch.GetCarePotency(defaultCare);
				__result += diff * 5;//Raise priority for higher meds, lower for lower meds.
				return false;
			}
			return true;
		}

		public static MedicalCareCategory GetCarePharmacist(Pawn p)
        {
			return Pharmacist.PharmacistUtility.TendAdvice(p);
        }
    }
}
