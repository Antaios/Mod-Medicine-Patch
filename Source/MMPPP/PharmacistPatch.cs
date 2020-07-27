using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using RimWorld;
using Verse;
using HarmonyLib;

namespace ModMedicinePatch
{
	[StaticConstructorOnStartup]
	public static class PharmacistPatch
	{
		static PharmacistPatch()
		{
			try
			{
				((Action)(() =>
				{
					Log.Message("Patching Pharmacist medcare list");

					//get ordered medicine list
					List<MedicalCareCategory> careList = ModMedicinePatch.GetOrderedCareList();

					//setup replacement Pharmacist medical care list
					MedicalCareCategory[] medCareReplacement = careList.ToArray();

					//add language labels
					foreach (MedicalCareCategory care in careList)
					{
						if (!LanguageDatabase.activeLanguage.HaveTextForKey($"MedicalCareCategory_{(int)care}"))
						{
							LoadedLanguage.KeyedReplacement kr = new LoadedLanguage.KeyedReplacement();
							kr.key = $"MedicalCareCategory_{(int)care}";
							kr.value = MedicalCareUtility.GetLabel(care);
							LanguageDatabase.activeLanguage.keyedReplacements.Add(kr.key, kr);
						}
					}

					//set Pharmacist's medcares array
					Traverse.Create<Pharmacist.MainTabWindow_Pharmacist>().Field("medcares").SetValue(medCareReplacement);

					//add modded meds to Pharmacists texture library
					Texture2D[] tex = new Texture2D[ModMedicinePatch.indexedMedList.Count];
					for (int i = 0; i < ModMedicinePatch.indexedMedList.Count; i++)
					{
						tex[i] = ModMedicinePatch.indexedMedList[i].tex;
					}

					Traverse.Create(typeof(Pharmacist.Resources)).Field("medcareGraphics").SetValue(tex);

					Log.Message("Done Patching Pharmacist medcare list");

					Log.Message("Patching Pharmacist comparison function..");
					var harmony = new Harmony("Antaios.Rimworld.PharmMedicinePatch");

					harmony.Patch(
						typeof(Pharmacist.PharmacistUtility).GetMethod("TendAdvice", new Type[] { typeof(Pawn), typeof(Pharmacist.InjurySeverity) }),
						null,
						new HarmonyMethod(typeof(PharmacistPatch).GetMethod("TendAdvicePostfix"))
							);

					Log.Message("Done patching Pharmacist comparison function..");
				}))();
			}
			catch (TypeLoadException)
			{
				Log.Message("Pharmacist not detected");
			}
		}

		public static void TendAdvicePostfix(ref MedicalCareCategory __result, Pawn patient, Pharmacist.InjurySeverity severity)
		{

			Pharmacist.Population population = Pharmacist.PharmacistUtility.GetPopulation(patient);
			var pharmacist = Pharmacist.PharmacistSettings.medicalCare[population][severity];
			var playerSetting = patient?.playerSettings?.medCare ?? MedicalCareCategory.Best;

			//get values which indicate relative medical potency
			float ph = ModMedicinePatch.GetCarePotency(pharmacist);
			float ps = ModMedicinePatch.GetCarePotency(playerSetting);

			MedicalCareCategory r = playerSetting;

			if (ph < ps)
				r = pharmacist;

			__result = r;
		}
	}
}
