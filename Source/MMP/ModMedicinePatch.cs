using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
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

        public static Pawn currentMedCarePawn;
        private static bool androids = false;
        private static List<ModMedicine> androidMedList;
        private static List<ModMedicine> humanMedList;
        private static FleshTypeDef AndroidFlesh;

		static ModMedicinePatch()
		{
            androids = TestAndroidTiers();
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

            InventoryStockGroupDefOf.Medicine.thingDefs = new List<ThingDef>();

            Log.Message("Sorted meds list: ");
			foreach (ModMedicine m in medList)
			{
                Log.Message(m.care.GetLabel());
                if (m.thingDef != null)
                {
                    InventoryStockGroupDefOf.Medicine.thingDefs.Add(m.thingDef);
                    Log.Message(m.thingDef.LabelCap);
                }
                Log.Message("-");
			}

            InventoryStockGroupDefOf.Medicine.max = InventoryStockGroupDefOf.Medicine.thingDefs.Count;

            //execute patches
            var harmony = new Harmony("ModMedicinePatch");
			Log.Message("Patching Mod Medicines...");
			harmony.PatchAll(Assembly.GetExecutingAssembly());

			Traverse.Create(typeof(MedicalCareUtility)).Field("careTextures").SetValue(careTex);

            if (androids)
            {
                PatchAndroids(harmony);
            }

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
            Widgets.DrawHighlightIfMouseover(rect);

            //aspect = number of squares rect fits along the width that are height x height
            int aspect = Mathf.FloorToInt(rect.width / rect.height);

            //initialScaleFac is how much to scale the texture to make it the height of the rect
            float initialScaleFac = medList[0].tex.height / rect.height;

            List<ModMedicine> localMedList = medList;

            if (androids && currentMedCarePawn != null)
            {
                localMedList = GetAndroidCompatMedList(currentMedCarePawn);
            }

            int nRows = 1;
            int nInRow = 0;
            if (localMedList.Count > aspect * 2)
            {
                nRows = Mathf.CeilToInt(Mathf.Sqrt((float)localMedList.Count / (float)aspect));
            }
            int nPerRow = Mathf.CeilToInt((float)localMedList.Count / (float)nRows);


            float scaleFac = Mathf.Min((float)aspect / (localMedList.Count),1.0f);
			if (nRows > 1)
            {
                scaleFac = 1.0f / nRows;
            }
            //scaleFac *= initialScaleFac;

			Rect rect2 = new Rect(rect.x + 0.5f * (rect.width - (nPerRow * scaleFac * rect.height)), rect.y + (1 - scaleFac * nRows) * 0.5f * rect.height, rect.height * scaleFac, rect.height * scaleFac);
			for (int i = 0; i < localMedList.Count; i++)
			{
				ModMedicine med = localMedList[i];

                //if (medCare == med.care)
                if ((i > 1 && med.potency == indexedMedList[(int)medCare].potency) || medCare == med.care)
                {
                    Widgets.DrawBox(rect2, 1);
                }

                if ((med.potency <= indexedMedList[(int)medCare].potency && i != 0 && (int)medCare !=0) || medCare == med.care)
                {
                    Widgets.DrawBoxSolid(rect2, new Color(0, 1, 1, 0.3f));
                }

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
            if (androids)
            {
                buttonIcon = FindBestAndroidCompatMedicine(pawn).tex;
            }
			Widgets.Dropdown<Pawn, MedicalCareCategory>(rect, pawn, getPayload, menuGenerator, null, buttonIcon, null, null, null, true);
		}

		private static MedicalCareCategory DynGetMedicalCare(Pawn pawn)
		{
			return pawn.playerSettings.medCare;
		}

		public static IEnumerable<Widgets.DropdownMenuElement<MedicalCareCategory>> DynMedGenerateMenu(Pawn p)
		{
            List<ModMedicine> localMedList = medList;
            if (androids)
            {
                localMedList = GetAndroidCompatMedList(p);
            }
			for (int i = 0; i < localMedList.Count; i++)
			{
				ModMedicine med = localMedList[i];

                yield return new Widgets.DropdownMenuElement<MedicalCareCategory>
                {
                    /*
                    option = new FloatMenuOption(med.care.GetLabel(), delegate
                    {
                        p.playerSettings.medCare = med.care;
                    }, MenuOptionPriority.Default, null, null, 30f, rect =>
                    {
                        //Float menu medicine icon inspired by Fluffy's Pharmacist.
                        Rect iconRect = new Rect(0f, 0f, 24, 24).CenteredOnXIn(rect).CenteredOnYIn(rect);
                        GUI.DrawTexture(iconRect, med.tex);
                        return false;
                    }, null),
                    payload = med.care*/
                    option = new FloatMenuOption(med.care.GetLabel(), delegate
                    {
                        p.playerSettings.medCare = med.care;
                    }, med.tex, Color.white),
                    payload = med.care
                };
			}
		}

        public static List<ModMedicine> GetAndroidCompatMedList(Pawn p = null)
        {
            if (MOARANDROIDS.Settings.androidsCanUseOrganicMedicine || p == null)
            {
                return medList;
            }

            if (IsAndroid(p))
            {
                return androidMedList;
            }
            else
            {
                return humanMedList;
            }
        }

        public static void PatchAndroids(Harmony harmony)
        {
            //patch the drawOverviewTab, so we can pass info about the current pawn displayed to the medicalcaresetter
            harmony.Patch(typeof(HealthCardUtility).GetMethod("DrawOverviewTab", BindingFlags.NonPublic | BindingFlags.Static), new HarmonyMethod(typeof(DrawOverviewTab).GetMethod("_Prefix")), new HarmonyMethod(typeof(DrawOverviewTab).GetMethod("_Postfix")));

            //unpatch the medical care select button, we'll take over that for andoirdtiers
            /*var androidMedCareSelectPatch = AccessTools.Inner(AccessTools.TypeByName("MOARANDROIDS.MedicalCareUtility_Patch"), "MedicalCareSelectButton_Patch").GetMethod("Listener", BindingFlags.Static);
            harmony.Unpatch(typeof(MedicalCareUtility).GetMethod("MedicalCareSelectButton"),androidMedCareSelectPatch);
            */
            harmony.Unpatch(typeof(MedicalCareUtility).GetMethod("MedicalCareSelectButton"), HarmonyPatchType.Prefix, "rimworld.rwmods.androidtiers");

            //get android flesh type
            AndroidFlesh = (FleshTypeDef)GenDefDatabase.GetDef(typeof(FleshTypeDef), "AndroidTier", true);
            
            //create android-only med list and store
            androidMedList = new List<ModMedicine>();
            humanMedList = new List<ModMedicine>();
            
            for (int i = 0; i < medList.Count; i++)
            {
                //always add no care and no medicine
                if (i == 0)
                {
                    androidMedList.Add(medList[i]);
                    humanMedList.Add(medList[i]);
                }
                else if (i == 1)
                {
                    humanMedList.Add(medList[i]);
                    //change no medicine texture for androids
                    androidMedList.Add(new ModMedicine((MedicalCareCategory)1, 0, ContentFinder<Texture2D>.Get("Things/Misc/ATPP_OnlyDocVisit", true)));
                }
                else
                {
                    if (IsAndroidMedicine(medList[i]))
                    {
                        androidMedList.Add(medList[i]);
                    }
                    else
                    {
                        humanMedList.Add(medList[i]);
                    }
                }
            }
        }

        public static ModMedicine FindBestAndroidCompatMedicine(Pawn p)
        {
            ModMedicine bestMed = medList[0];
            if (DynGetMedicalCare(p) == MedicalCareCategory.NoMeds)
            {
                bestMed = medList[1];
            }
            if (IsAndroid(p))
            {
                List<ModMedicine> localMedList = androidMedList;
                if (MOARANDROIDS.Settings.androidsCanUseOrganicMedicine)
                {
                    localMedList = medList;
                }
                
                for (int i = 2; i < localMedList.Count; i++)
                {
                    if (!GetDynamicAllowsMedicine(DynGetMedicalCare(p), localMedList[i].thingDef))
                    {
                        continue;
                    }
                    if (localMedList[i].potency > bestMed.potency)
                    {
                        bestMed = localMedList[i];
                    }
                    else if (localMedList[i].potency == bestMed.potency)
                    {
                        if (IsAndroidMedicine(localMedList[i]))
                        {
                            bestMed = localMedList[i];
                        }
                    }
                }
            }
            else
            {
                List<ModMedicine> localMedList = humanMedList;

                for (int i = 2; i < localMedList.Count; i++)
                {
                    if (!GetDynamicAllowsMedicine(DynGetMedicalCare(p), localMedList[i].thingDef))
                    {
                        continue;
                    }
                    if (localMedList[i].potency > bestMed.potency)
                    {
                        bestMed = localMedList[i];
                    }
                    else if (localMedList[i].potency == bestMed.potency)
                    {
                        if (!IsAndroidMedicine(localMedList[i]))
                        {
                            bestMed = localMedList[i];
                        }
                    }
                }
            }
            return bestMed;
        }

        public static bool IsAndroid(Pawn p)
        {
            return p.RaceProps.FleshType == AndroidFlesh;
        }

        public static bool IsAndroidMedicine(ModMedicine m)
        {
            return MOARANDROIDS.Utils.ExceptionNanoKits.Contains(m.thingDef.defName);
        }

        public static bool TestAndroidTiers()
        {
            bool an = false;
            try
            {
                ((Action)(() =>
                {
                    if (MOARANDROIDS.Utils.ExceptionNanoKits != null)
                    {
                        Log.Message("MMP: Detected Android Tiers");
                        an = true;
                    }
                    else
                    {
                        Log.Message("MMP: How'd we get here?");
                    }
                }))();
            }
            catch (TypeLoadException)
            {
                Log.Message("MMP: Android Tiers not detected");
            }
            return an;
        }
	}

    //get rimworld to give me the flesh type def of androids, if they exist.
}
