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
			//if MedicalCareCategory is within base game range, use original method.
			__state = false;
			if ((int)cat <= 4) __state = true;
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
		public static bool _Prefix(this MedicalCareCategory cat, ref bool __state)
		{
			//if MedicalCareCategory is within base game range, use original method.
			__state = false;
			if ((int)cat <= 4) __state = true;
			return __state;
		}

		[HarmonyPostfix]
		public static void _Postfix(this MedicalCareCategory cat, ThingDef meds, ref bool __result, bool __state)
		{
			if (!__state)
			{
				__result = ModMedicinePatch.GetDynamicAllowsMedicine(cat, meds);
			}
		}
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
}
