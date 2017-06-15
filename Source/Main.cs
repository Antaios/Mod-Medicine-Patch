using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using System.Reflection;
using Verse;
using RimWorld;
using UnityEngine;
using Verse.Sound;

/* notes
 * Medical Defaults vertical padding: 34
 * Medical Overview tab vertical padding: ~32
 * ITab pawn visitor (?) 44?
 * Medical Tab vertical padding: 30 minimum
 */

namespace ModMedicinePatch
{
	[StaticConstructorOnStartup]
	class Main
	{
		static Main()
		{
			//Init and check for modded meds
			bool medsFound = ModMedicalCareUtility.Init();
			if (medsFound)
			{
				//execute patches
				var harmony = HarmonyInstance.Create("ModMedicinePatch");
				Log.Message("Patching Mod Medicines...");
				harmony.PatchAll(Assembly.GetExecutingAssembly());
			}
			else
			{
				Log.Warning("No modded medicines found, unable to patch, cancelling medicine patch.");
			}
		}
	}

	// 'Replace' MedicalCareSetter with new version
	[HarmonyPatch(typeof(RimWorld.MedicalCareUtility),"MedicalCareSetter")]
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
			ModMedicalCareUtility.DynamicMedicalCareSetter(rect, ref medCare);
		}
	}

	//'Replace' GetLabel, so tooltips make sense
	[HarmonyPatch(typeof(RimWorld.MedicalCareUtility), "GetLabel")]
	public static class GetLabel
	{
		[HarmonyPrefix]
		public static bool _Prefix(this MedicalCareCategory cat,ref bool __state)
		{
			//if MedicalCareCategory is within base game range, use original method.
			__state = false;
			if ((int)cat <= 4) __state = true;
			return __state;
		}

		[HarmonyPostfix]
		public static void _Postfix(this MedicalCareCategory cat, ref string __result, bool __state)
		{
			if (!__state) {
				__result = ModMedicalCareUtility.GetDynamicLabel(cat);
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
				__result = ModMedicalCareUtility.GetDynamicAllowsMedicine(cat, meds);
			}
		}
	}


	public static class ModMedicalCareUtility
	{
		private static Texture2D[] careTextures;

		private static List<ThingDef> medsList;
		private static List<int> medsListOrder;

		public static bool Init()
		{
			medsList = new List<ThingDef>();

			Log.Message("Adding Basegame medsList");

			//add base game medicines in correct order, attempting to preserve base game MedicalCareCategory order.
			medsList.Add(ThingDefOf.HerbalMedicine);
			medsList.Add(ThingDefOf.Medicine);
			medsList.Add(ThingDefOf.GlitterworldMedicine);

			bool foundMeds = false;

			//Find mod medicines
			//Cycle through all defs
			Log.Message("Adding mod medicines");
			foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
			{
				//limt by 'Medicine' category
				if (d.thingCategories != null && d.thingCategories.Contains(ThingCategoryDefOf.Medicine))
				{
					//Exclude base game medicines
					if (d.defName != "HerbalMedicine" && d.defName != "Medicine" && d.defName != "GlitterworldMedicine" && d.GetStatValueAbstract(StatDefOf.MedicalPotency, null) > 0)
					{
						//add found medicine to medsList
						Log.Message("Detected mod medicine " + d.label);
						medsList.Add(d);
						foundMeds = true;
					}
				}
			}

			medsListOrder = new List<int>();

			Log.Message("Setting up textures");
			//grab a local copy of all the textures, for UI icons.
			ModMedicalCareUtility.careTextures = new Texture2D[medsList.Count + 2];
			ModMedicalCareUtility.careTextures[0] = ContentFinder<Texture2D>.Get("UI/Icons/Medical/NoCare", true);
			ModMedicalCareUtility.careTextures[1] = ContentFinder<Texture2D>.Get("UI/Icons/Medical/Nomeds", true);
			//make sure to cycle through in order
			for (int i = 0; i < medsList.Count; i++)
			{
				//Add icon texture for medicine.
				ModMedicalCareUtility.careTextures[i+2] = medsList[i].uiIcon;
				//add medicine MedicalCareCategory to medsListOrder
				medsListOrder.Add(i);
			}

			//sort the set of MedicalCareCategory values by associated medical potency
			medsListOrder.Sort(delegate (int a, int b)
			{
				if (medsList[a].GetStatValueAbstract(StatDefOf.MedicalPotency, null) < medsList[b].GetStatValueAbstract(StatDefOf.MedicalPotency, null)) return -1;
				else if (medsList[a].GetStatValueAbstract(StatDefOf.MedicalPotency, null) > medsList[b].GetStatValueAbstract(StatDefOf.MedicalPotency, null)) return 1;
				else return 0;
			});

			Log.Message("Sorted meds list: ");
			for (int i = 0; i < medsList.Count; i++)
			{
				Log.Message(medsList[medsListOrder[i]].label);
			}

			return foundMeds;
		}

		public static string GetDynamicLabel(MedicalCareCategory cat)
		{
			return String.Format("MedicalCareCategory_X".Translate(), medsList[(int)cat-2].LabelCap);
		}
		
		public static bool GetDynamicAllowsMedicine(MedicalCareCategory cat, ThingDef meds)
		{
			if ((int)cat < medsList.Count + 2)
			{
				//compare medical potencies
				return (meds.GetStatValueAbstract(StatDefOf.MedicalPotency, null) <= medsList[(int)cat - 2].GetStatValueAbstract(StatDefOf.MedicalPotency, null));
			}
			else
			{
				throw new InvalidOperationException();
			}
		}
		
		public static void DynamicMedicalCareSetter(Rect rect, ref MedicalCareCategory medCare)
		{
			//modified CareSetter/UI panel
			float scaleFacV = 0.5f;
			float scaleFacH = 5.0f / (medsList.Count + 2);
			int nFirstRow = (int)Mathf.Ceil(0.5f * (medsList.Count + 2));
			bool row = (scaleFacV > scaleFacH);
			if (row)
			{
				scaleFacH = 5.0f / nFirstRow;
			}
			float scaleFac = Mathf.Max(scaleFacV, scaleFacH);
			


			Rect rect2 = new Rect(rect.x + (row ? 0.5f * nFirstRow * rect.width * scaleFac / 5 : 0), rect.y + (row ? scaleFac * -0.5f * rect.height : (1f-scaleFac) * rect.height * 0.5f), rect.width * scaleFac / 5, rect.height * scaleFac);
			for (int i = 0; i < medsList.Count + 2; i++)
			{
				int k = i;
				if (i >= 2)
				{
					k = medsListOrder[i - 2]+2;
				}

				MedicalCareCategory mc = (MedicalCareCategory)k;
				Widgets.DrawHighlightIfMouseover(rect2);
				GUI.DrawTexture(rect2, ModMedicalCareUtility.careTextures[k]);
				if (Widgets.ButtonInvisible(rect2, false))
				{
					medCare = mc;
					SoundDefOf.TickHigh.PlayOneShotOnCamera(null);
				}
				if (medCare == mc)
				{
					Widgets.DrawBox(rect2, 1);
				}
				TooltipHandler.TipRegion(rect2, () => mc.GetLabel(), 632165 + k * 17);

				rect2.x += rect2.width;
				if (row)
				{
					if (i == nFirstRow - 1) {
						rect2.y += rect2.height;
						rect2.x = rect.x + (row ? 0.5f * nFirstRow * rect.width * scaleFac / 5 : 0);
					}
				}
			}

			/*
			float scaleFac = 5f / (medsList.Count + 2);
			Rect rect2 = new Rect(rect.x, rect.y + (1-scaleFac) * 0.5f * rect.height, rect.width / (medsList.Count + 2), rect.height * scaleFac);
			if (medsList.Count + 2 > 7)
			{
				float c = Mathf.Ceil(0.5f * (medsList.Count + 2f));
				scaleFac = Mathf.Min(0.83f, 5f / c);
				rect2 = new Rect(rect.x + (rect.width * 0.5f) - (rect.width * scaleFac / 5) * Mathf.Ceil(0.5f * (medsList.Count + 2f)) * 0.5f, rect.y + (rect.height * 0.5f) - (rect.height * scaleFac), rect.width * scaleFac / 5, rect.height * scaleFac);
			}
			for (int i = 0; i < medsList.Count + 2; i++)
			{
				int k = i;
				if (i >= 2)
				{
					k = medsListOrder[i - 2]+2;
				}

				MedicalCareCategory mc = (MedicalCareCategory)k;
				Widgets.DrawHighlightIfMouseover(rect2);
				GUI.DrawTexture(rect2, ModMedicalCareUtility.careTextures[k]);
				if (Widgets.ButtonInvisible(rect2, false))
				{
					medCare = mc;
					SoundDefOf.TickHigh.PlayOneShotOnCamera(null);
				}
				if (medCare == mc)
				{
					Widgets.DrawBox(rect2, 1);
				}
				TooltipHandler.TipRegion(rect2, () => mc.GetLabel(), 632165 + k * 17);

				rect2.x += rect2.width;
				if (medsList.Count + 2 > 7)
				{
					if (i == Mathf.Ceil(0.5f * (medsList.Count + 2f))-1) {
						rect2.y += rect2.height;
						rect2.x = rect.x + (rect.width * 0.5f) - (rect.width * scaleFac / 5) * (medsList.Count + 1 - i) * 0.5f;
					}
				}
			}
			*/
		}
	}
}
