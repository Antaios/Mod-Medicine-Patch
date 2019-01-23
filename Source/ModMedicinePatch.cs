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
	public struct ModMedicine
	{
		public MedicalCareCategory care;
		public float potency;
		public ThingDef thingDef;
		public Texture2D tex;

		public ModMedicine(MedicalCareCategory c, float p, ThingDef d, Texture2D t)
		{
			care = c;
			potency = p;
			thingDef = d;
			tex = t;
		}

		public ModMedicine(MedicalCareCategory c, float p, Texture2D t)
		{
			care = c;
			potency = p;
			thingDef = null;
			tex = t;
		}
	}

	[StaticConstructorOnStartup]
	public static class ModMedicinePatch
	{
		//meds list, in order from worst to best
		public static List<ModMedicine> medList;
		//meds list, in asdcending order of MedicalCareCategory
		public static List<ModMedicine> indexedMedList;

		private static bool medicalCarePainting = false;

		static ModMedicinePatch()
		{
			List<ThingDef> medThingList = new List<ThingDef>();

			Log.Message("Adding Basegame medsList");

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
					if (d.defName != ThingDefOf.MedicineHerbal.defName && d.defName != ThingDefOf.MedicineIndustrial.defName && d.defName != ThingDefOf.MedicineUltratech.defName && d.IsMedicine)
					{
						//add found medicine to medsList
						Log.Message("Detected mod medicine " + d.label + ":" + d.GetStatValueAbstract(StatDefOf.MedicalPotency, null));
						medThingList.Add(d);
						foundMeds = true;
					}
				}
			}

			//sort the set of mod medicines by medical potency so there's some assured consistency in care index
			medThingList.Sort(delegate (ThingDef a, ThingDef b)
			{
				if (a.GetStatValueAbstract(StatDefOf.MedicalPotency, null) < b.GetStatValueAbstract(StatDefOf.MedicalPotency, null)) return -1;
				else if (a.GetStatValueAbstract(StatDefOf.MedicalPotency, null) > b.GetStatValueAbstract(StatDefOf.MedicalPotency, null)) return 1;
				else return 0;
			});

			//add base game medicines in correct order, attempting to preserve base game MedicalCareCategory order.
			medThingList.Insert(0,ThingDefOf.MedicineHerbal);
			medThingList.Insert(1,ThingDefOf.MedicineIndustrial);
			medThingList.Insert(2,ThingDefOf.MedicineUltratech);

			medList = new List<ModMedicine>();

			Log.Message("Setting up textures");
			Texture2D[] careTex = new Texture2D[medThingList.Count + 2];

			for (int i = 2; i < medThingList.Count+2; i++)
			{
				ThingDef td = medThingList[i-2];
				careTex[i] = td.uiIcon;
				medList.Add(new ModMedicine((MedicalCareCategory)i,td.GetStatValueAbstract(StatDefOf.MedicalPotency, null),td,td.uiIcon));
			}

			//create the indexed med list before messing up index order
			indexedMedList = new List<ModMedicine>(medList);

			//sort the set of medicines by associated medical potency
			medList.Sort(delegate (ModMedicine a, ModMedicine b)
			{
				if (a.potency < b.potency) return -1;
				else if (a.potency > b.potency) return 1;
				else return 0;
			});

			//add nocare and nomeds
			careTex[0] = ContentFinder<Texture2D>.Get("UI/Icons/Medical/NoCare", true);
			careTex[1] = ContentFinder<Texture2D>.Get("UI/Icons/Medical/Nomeds", true);
			medList.Insert(0, new ModMedicine((MedicalCareCategory)0, 0, careTex[0]));
			medList.Insert(1, new ModMedicine((MedicalCareCategory)1, 0, careTex[1]));
			//add nocare and nomeds to the indexed list aswell
			indexedMedList.Insert(0, new ModMedicine((MedicalCareCategory)0, 0, careTex[0]));
			indexedMedList.Insert(1, new ModMedicine((MedicalCareCategory)1, 0, careTex[1]));

			Log.Message("Sorted meds list: ");
			foreach (ModMedicine m in medList)
			{
				Log.Message(m.care.GetLabel());
			}

			//execute patches
			var harmony = HarmonyInstance.Create("ModMedicinePatch");
			Log.Message("Patching Mod Medicines...");
			harmony.PatchAll(Assembly.GetExecutingAssembly());

			Traverse.Create(typeof(MedicalCareUtility)).Field("careTextures").SetValue(careTex);

			if (!foundMeds)
			{
				Log.Warning("Note: No modded medicines found");
			}
		}

		public static ModMedicine GetMedicineByCare(MedicalCareCategory care)
		{
			return indexedMedList[(int)care];
		}

		public static Texture2D GetCareTexture(MedicalCareCategory care)
		{
			return GetMedicineByCare(care).tex;
		}

		public static float GetCarePotency(MedicalCareCategory care)
		{
			return GetMedicineByCare(care).potency;
		}

		public static List<MedicalCareCategory> GetOrderedCareList()
		{
			List<MedicalCareCategory> careList = new List<MedicalCareCategory>();
			foreach (ModMedicine med in medList)
			{
				careList.Add(med.care);
			}
			return careList;
		}

		public static void DynamicMedicalCareSetter(Rect rect, ref MedicalCareCategory medCare)
		{
            //modified CareSetter/UI panel

            int aspect = Mathf.FloorToInt(rect.width / rect.height);
            float initialScaleFac = medList[0].tex.height / rect.height;

            int nRows = 1;
            int nInRow = 0;
            if (medList.Count > aspect * 2)
            {
                nRows = Mathf.CeilToInt(Mathf.Sqrt((float)medList.Count / (float)aspect));
            }
            int nPerRow = Mathf.CeilToInt((float)medList.Count / (float)nRows);


            float scaleFac = Mathf.Min((float)aspect / (medList.Count),1.0f);
			if (nRows > 1)
            {
                scaleFac = 1.0f / nRows;
            }
            scaleFac *= initialScaleFac;

			Rect rect2 = new Rect(rect.x + 0.5f * (rect.width - (nPerRow * scaleFac * rect.height)), rect.y + (1 - scaleFac * nRows) * 0.5f * rect.height, rect.height * scaleFac, rect.height * scaleFac);
			for (int i = 0; i < medList.Count; i++)
			{
				ModMedicine med = medList[i];

				Widgets.DrawHighlightIfMouseover(rect2);
				GUI.DrawTexture(rect2, med.tex);

				Widgets.DraggableResult draggableResult = Widgets.ButtonInvisibleDraggable(rect2, false);
				if (draggableResult == Widgets.DraggableResult.Dragged)
				{
					medicalCarePainting = true;
				}
				if ((medicalCarePainting && Mouse.IsOver(rect2) && medCare != med.care) || (draggableResult == Widgets.DraggableResult.Pressed || draggableResult == Widgets.DraggableResult.DraggedThenPressed))
				{
					medCare = med.care;
					SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
				}
				if (medCare == med.care)
				{
					Widgets.DrawBox(rect2, 1);
				}
				TooltipHandler.TipRegion(rect2, () => med.care.GetLabel(), 632165 + (int)med.care * 17);

                nInRow++;

				rect2.x += rect2.width;
				if (nInRow == nPerRow)
				{
                    nInRow = 0;
					rect2.y += rect2.height;
					rect2.x = rect.x + 0.5f * (rect.width - (nPerRow * scaleFac * rect.height));
				}
			}
			if (!Input.GetMouseButton(0))
			{
				medicalCarePainting = false;
			}
		}

		public static string GetDynamicLabel(MedicalCareCategory cat)
		{
			return String.Format("MedicalCareCategory_X".Translate(), GetMedicineByCare(cat).thingDef.LabelCap);
		}

		public static bool GetDynamicAllowsMedicine(MedicalCareCategory cat, ThingDef meds)
		{
			if ((int)cat < medList.Count)
			{
				//compare medical potencies
				return (meds.GetStatValueAbstract(StatDefOf.MedicalPotency, null) <= indexedMedList[(int)cat].potency);
			}
			else
			{
				throw new InvalidOperationException();
			}
		}

		public static void DynamicMedicalCareSelectButton(Rect rect, Pawn pawn)
		{
			Func<Pawn, MedicalCareCategory> getPayload = new Func<Pawn, MedicalCareCategory>(DynGetMedicalCare);
			Func<Pawn, IEnumerable<Widgets.DropdownMenuElement<MedicalCareCategory>>> menuGenerator = new Func<Pawn, IEnumerable<Widgets.DropdownMenuElement<MedicalCareCategory>>>(DynMedGenerateMenu);
			Texture2D buttonIcon = GetCareTexture(pawn.playerSettings.medCare);
			Widgets.Dropdown<Pawn, MedicalCareCategory>(rect, pawn, getPayload, menuGenerator, null, buttonIcon, null, null, null, true);
		}

		private static MedicalCareCategory DynGetMedicalCare(Pawn pawn)
		{
			return pawn.playerSettings.medCare;
		}

		public static IEnumerable<Widgets.DropdownMenuElement<MedicalCareCategory>> DynMedGenerateMenu(Pawn p)
		{
			for (int i = 0; i < medList.Count; i++)
			{
				ModMedicine med = medList[i];

				yield return new Widgets.DropdownMenuElement<MedicalCareCategory>
				{
					option = new FloatMenuOption(med.care.GetLabel(), delegate
					{
						p.playerSettings.medCare = med.care;
					}, MenuOptionPriority.Default, null, null, 30f, rect =>
					{
						//Float menu medicine icon inspired by Fluffy's Pharmacist.
						Rect iconRect = new Rect(0f, 0f, 24,24).CenteredOnXIn(rect).CenteredOnYIn(rect);
						GUI.DrawTexture(iconRect, med.tex);
						return false;
					}, null),
					payload = med.care
				};
			}
		}

	}
}
