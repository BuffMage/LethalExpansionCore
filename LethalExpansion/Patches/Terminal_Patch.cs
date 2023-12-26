﻿using DunGen;
using HarmonyLib;
using LethalExpansion.Extenders;
using LethalExpansion.Utils;
using LethalSDK.Utils;
using LethalSDK.ScriptableObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LethalExpansion.Patches
{
    [HarmonyPatch(typeof(Terminal))]
    internal class Terminal_Patch
    {
        private static TerminalKeyword[] defaultTerminalKeywords;
        public static bool scrapsPatched = false;
        public static bool moonsPatched = false;
        public static bool assetsGotten = false;
        public static bool flowFireExitSaved = false;

        public static List<int> fireExitAmounts = new List<int>();

        public static TerminalKeyword routeKeyword;

        public static List<string> newScrapsNames = new List<string>();
        public static List<string> newMoonsNames = new List<string>();
        public static Dictionary<int, Moon> newMoons = new Dictionary<int, Moon>();

        public static void MainPatch(Terminal __instance)
        {
            scrapsPatched = false;
            moonsPatched = false;
            routeKeyword = __instance.terminalNodes.allKeywords.First(k => k.word == "route");

            // RemoveMoon(__instance, "Experimentation");
            Hotfix_DoubleRoutes();
            GatherAssets();
            AddScraps(__instance);
            ResetTerminalKeywords(__instance);
            AddMoons(__instance);
            UpdateMoonsCatalogue(__instance);
            SaveFireExitAmounts();

            if (LethalExpansion.delayedLevelChange != -1)
            {
                StartOfRound.Instance.ChangeLevel(LethalExpansion.delayedLevelChange);
                StartOfRound.Instance.ChangePlanet();
            }

            NetworkPacketManager.Instance.SendPacket(NetworkPacketManager.PacketType.Request, "hostweathers", string.Empty, 0);
            LethalExpansion.Log.LogInfo("Terminal Main Patch.");
            //AssetGather.Instance.GetList();
        }

        private static void GatherAssets()
        {
            if (assetsGotten)
            {
                return;
            }

            foreach (Item item in StartOfRound.Instance.allItemsList.itemsList)
            {
                AssetGather.Instance.AddAudioClip(item.grabSFX);
                AssetGather.Instance.AddAudioClip(item.dropSFX);
                AssetGather.Instance.AddAudioClip(item.pocketSFX);
                AssetGather.Instance.AddAudioClip(item.throwSFX);
            }

            AssetGather.Instance.AddSprites(GameObject.Find("Environment/HangarShip/StartGameLever").GetComponent<InteractTrigger>().hoverIcon);
            AssetGather.Instance.AddSprites(GameObject.Find("Environment/HangarShip/Terminal/TerminalTrigger/TerminalScript").GetComponent<InteractTrigger>().hoverIcon);
            AssetGather.Instance.AddSprites(GameObject.Find("Environment/HangarShip/OutsideShipRoom/Ladder/LadderTrigger").GetComponent<InteractTrigger>().hoverIcon);
            foreach (SelectableLevel level in StartOfRound.Instance.levels)
            {
                AssetGather.Instance.AddPlanetPrefabs(level.planetPrefab);
                level.spawnableMapObjects.ToList().ForEach(e => AssetGather.Instance.AddMapObjects(e.prefabToSpawn));
                level.spawnableOutsideObjects.ToList().ForEach(e => AssetGather.Instance.AddOutsideObject(e.spawnableObject));
                level.spawnableScrap.ForEach(e => AssetGather.Instance.AddScrap(e.spawnableItem));
                AssetGather.Instance.AddLevelAmbiances(level.levelAmbienceClips);
                level.Enemies.ForEach(e => AssetGather.Instance.AddEnemies(e.enemyType));
                level.OutsideEnemies.ForEach(e => AssetGather.Instance.AddEnemies(e.enemyType));
                level.DaytimeEnemies.ForEach(e => AssetGather.Instance.AddEnemies(e.enemyType));
            }

            foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.assetBundles)
            {
                (AssetBundle bundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

                if (manifest.assetBank == null)
                {
                    continue;
                }

                try
                {
                    foreach (var a in manifest.assetBank.AudioClips())
                    {
                        AssetGather.Instance.AddAudioClip(a.AudioClipName, bundleKeyValue.Value.Item1.LoadAsset<AudioClip>(a.AudioClipPath));
                    }

                    foreach (var p in manifest.assetBank.PlanetPrefabs())
                    {
                        var prefab = bundleKeyValue.Value.Item1.LoadAsset<GameObject>(p.PlanetPrefabPath);
                        if (prefab == null)
                        {
                            continue;
                        }

                        Animator animator;
                        if (prefab.GetComponent<Animator>() == null)
                        {
                            animator = prefab.AddComponent<Animator>();
                        }
                        animator = AssetGather.Instance.planetPrefabs.First().Value.GetComponent<Animator>();
                        AssetGather.Instance.AddPlanetPrefabs(prefab);
                    }
                }
                catch (Exception ex)
                {
                    LethalExpansion.Log.LogError(ex.Message);
                }
            }

            //Interfacing LE and SDK Asset Gathers
            AssetGatherDialog.audioClips = AssetGather.Instance.audioClips;
            AssetGatherDialog.audioMixers = AssetGather.Instance.audioMixers;
            AssetGatherDialog.sprites = AssetGather.Instance.sprites;

            assetsGotten = true;
        }

        public static void AddScraps(Terminal __instance)
        {
            if (scrapsPatched)
            {
                return;
            }

            AudioClip defaultDropSound = AssetGather.Instance.audioClips["DropCan"];
            AudioClip defaultGrabSound = AssetGather.Instance.audioClips["ShovelPickUp"];
            foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.assetBundles)
            {
                (AssetBundle bundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

                if (bundle == null || manifest == null)
                {
                    continue;
                }

                if (manifest.scraps == null)
                {
                    continue;
                }

                foreach (var newScrap in manifest.scraps)
                {
                    if (newScrap == null || newScrap.prefab == null)
                    {
                        continue;
                    }

                    if (newScrap.RequiredBundles != null && !AssetBundlesManager.Instance.BundlesLoaded(newScrap.RequiredBundles))
                    {
                        continue;
                    }

                    if (newScrap.IncompatibleBundles != null && AssetBundlesManager.Instance.IncompatibleBundlesLoaded(newScrap.IncompatibleBundles))
                    {
                        continue;
                    }

                    if (newScrapsNames.Contains(newScrap.itemName))
                    {
                        LethalExpansion.Log.LogWarning($"{newScrap.itemName} Scrap already added.");
                        continue;
                    }

                    try
                    {
                        Item tmpItem = newScrap.prefab.GetComponent<PhysicsProp>().itemProperties;

                        AudioSource audioSource = newScrap.prefab.GetComponent<AudioSource>();
                        audioSource.outputAudioMixerGroup = AssetGather.Instance.audioMixers.ContainsKey("Diagetic") ? AssetGather.Instance.audioMixers["Diagetic"].Item2.First(a => a.name == "Master") : null;

                        AudioClip _tpmGrabSFX = null;
                        if (newScrap.grabSFX.Length > 0 && AssetGather.Instance.audioClips.ContainsKey(newScrap.grabSFX))
                        {
                            _tpmGrabSFX = AssetGather.Instance.audioClips[newScrap.grabSFX];
                        }
                        tmpItem.grabSFX = _tpmGrabSFX != null ? _tpmGrabSFX : defaultGrabSound;
                        AudioClip _tpmDropSFX = null;
                        if (newScrap.grabSFX.Length > 0 && AssetGather.Instance.audioClips.ContainsKey(newScrap.dropSFX))
                        {
                            _tpmDropSFX = AssetGather.Instance.audioClips[newScrap.dropSFX];
                        }
                        tmpItem.dropSFX = _tpmDropSFX != null ? _tpmDropSFX : defaultDropSound;

                        StartOfRound.Instance.allItemsList.itemsList.Add(tmpItem);
                        if (newScrap.useGlobalSpawnWeight)
                        {
                            SpawnableItemWithRarity itemRarity = new SpawnableItemWithRarity();
                            itemRarity.spawnableItem = tmpItem;
                            itemRarity.rarity = newScrap.globalSpawnWeight;
                            foreach (SelectableLevel level in __instance.moonsCatalogueList)
                            {
                                level.spawnableScrap.Add(itemRarity);
                            }
                        }
                        else
                        {
                            var ddqdz = newScrap.perPlanetSpawnWeight();
                            foreach (SelectableLevel level in __instance.moonsCatalogueList)
                            {
                                try
                                {
                                    if (ddqdz.Any(l => l.SceneName == level.PlanetName))
                                    {
                                        var tmp = ddqdz.First(l => l.SceneName == level.PlanetName);

                                        SpawnableItemWithRarity itemRarity = new SpawnableItemWithRarity();
                                        itemRarity.spawnableItem = tmpItem;
                                        itemRarity.rarity = tmp.SpawnWeight;

                                        level.spawnableScrap.Add(itemRarity);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LethalExpansion.Log.LogError(ex.Message);
                                }
                            }
                        }

                        newScrapsNames.Add(tmpItem.itemName);
                        AssetGather.Instance.AddScrap(tmpItem);
                        LethalExpansion.Log.LogInfo($"{newScrap.itemName} Scrap added.");
                    }
                    catch (Exception ex)
                    {
                        LethalExpansion.Log.LogError(ex.Message);
                    }
                }
            }

            scrapsPatched = true;
        }

        public static void AddMoons(Terminal __instance)
        {
            newMoons = new Dictionary<int, Moon>();

            if (moonsPatched)
            {
                return;
            }

            foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.assetBundles)
            {
                (AssetBundle bundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

                if (bundle == null || manifest == null)
                {
                    continue;
                }

                if (manifest.moons == null)
                {
                    continue;
                }

                foreach (Moon newMoon in manifest.moons)
                {
                    if (newMoon == null || !newMoon.IsEnabled)
                    {
                        continue;
                    }

                    if (newMoonsNames.Contains(newMoon.MoonName))
                    {
                        LethalExpansion.Log.LogWarning(newMoon.MoonName + " Moon already added.");
                        continue;
                    }

                    try
                    {
                        if (newMoon.RequiredBundles != null)
                        {
                            List<string> missingBundles = AssetBundlesManager.Instance.GetMissingBundles(newMoon.RequiredBundles).ToList();
                            if (missingBundles.Count > 0)
                            {
                                LethalExpansion.Log.LogWarning($"{newMoon.MoonName} can't be added, missing bundles: {string.Join(", ", missingBundles)}");
                                continue;
                            }
                        }

                        if (newMoon.IncompatibleBundles != null)
                        {
                            List<string> incompatibleBundles = AssetBundlesManager.Instance.GetLoadedBundles(newMoon.IncompatibleBundles).ToList();
                            if (incompatibleBundles.Count > 0)
                            {
                                LethalExpansion.Log.LogWarning($"{newMoon.MoonName} can't be added, incompatible bundles: {string.Join(", ", incompatibleBundles)}");
                                continue;
                            }
                        }

                        TerminalKeyword confirmKeyword = __instance.terminalNodes.allKeywords.First(k => k.word == "confirm");
                        TerminalKeyword denyKeyword = __instance.terminalNodes.allKeywords.First(k => k.word == "deny");
                        TerminalNode cancelRouteNode = null;
                        foreach (CompatibleNoun option in routeKeyword.compatibleNouns[0].result.terminalOptions)
                        {
                            if (option.result.name == "CancelRoute")
                            {
                                cancelRouteNode = option.result;
                                break;
                            }
                        }

                        SelectableLevel newLevel = ScriptableObject.CreateInstance<SelectableLevel>();

                        newLevel.name = newMoon.PlanetName;
                        newLevel.PlanetName = newMoon.PlanetName;
                        newLevel.sceneName = "InitSceneLaunchOptions";
                        newLevel.levelID = StartOfRound.Instance.levels.Length;
                        if (newMoon.OrbitPrefabName != null && newMoon.OrbitPrefabName.Length > 0 && AssetGather.Instance.planetPrefabs.ContainsKey(newMoon.OrbitPrefabName))
                        {
                            newLevel.planetPrefab = AssetGather.Instance.planetPrefabs[newMoon.OrbitPrefabName];
                        }
                        else
                        {
                            newLevel.planetPrefab = AssetGather.Instance.planetPrefabs.First().Value;
                        }
                        newLevel.lockedForDemo = true;
                        newLevel.spawnEnemiesAndScrap = newMoon.SpawnEnemiesAndScrap;
                        if (newMoon.PlanetDescription != null && newMoon.PlanetDescription.Length > 0)
                        {
                            newLevel.LevelDescription = newMoon.PlanetDescription;
                        }
                        else
                        {
                            newLevel.LevelDescription = string.Empty;
                        }
                        newLevel.videoReel = newMoon.PlanetVideo;
                        if (newMoon.RiskLevel != null && newMoon.RiskLevel.Length > 0)
                        {
                            newLevel.riskLevel = newMoon.RiskLevel;
                        }
                        else
                        {
                            newLevel.riskLevel = string.Empty;
                        }
                        newLevel.timeToArrive = newMoon.TimeToArrive;
                        newLevel.DaySpeedMultiplier = newMoon.DaySpeedMultiplier;
                        newLevel.planetHasTime = newMoon.PlanetHasTime;
                        newLevel.factorySizeMultiplier = newMoon.FactorySizeMultiplier;

                        newLevel.overrideWeather = newMoon.OverwriteWeather;
                        newLevel.overrideWeatherType = (LevelWeatherType)(int)newMoon.OverwriteWeatherType;
                        newLevel.currentWeather = LevelWeatherType.None;

                        var tmpRandomWeatherTypes1 = newMoon.RandomWeatherTypes();
                        List<RandomWeatherWithVariables> tmpRandomWeatherTypes2 = new List<RandomWeatherWithVariables>();
                        foreach (var item in tmpRandomWeatherTypes1)
                        {
                            tmpRandomWeatherTypes2.Add(new RandomWeatherWithVariables() { weatherType = (LevelWeatherType)(int)item.Weather, weatherVariable = item.WeatherVariable1, weatherVariable2 = item.WeatherVariable2 });
                        }
                        newLevel.randomWeathers = tmpRandomWeatherTypes2.ToArray();

                        var tmpDungeonFlowTypes1 = newMoon.DungeonFlowTypes();
                        List<IntWithRarity> tmpDungeonFlowTypes2 = new List<IntWithRarity>();
                        foreach (var item in tmpDungeonFlowTypes1)
                        {
                            tmpDungeonFlowTypes2.Add(new IntWithRarity() { id = item.ID, rarity = item.Rarity });
                        }
                        newLevel.dungeonFlowTypes = tmpDungeonFlowTypes2.ToArray();

                        var tmpSpawnableScrap1 = newMoon.SpawnableScrap();
                        List<SpawnableItemWithRarity> tmpSpawnableScrap2 = new List<SpawnableItemWithRarity>();
                        foreach (var item in tmpSpawnableScrap1)
                        {
                            try
                            {
                                tmpSpawnableScrap2.Add(new SpawnableItemWithRarity() { spawnableItem = AssetGather.Instance.scraps[item.ObjectName], rarity = item.SpawnWeight });
                            }
                            catch (Exception ex)
                            {
                                LethalExpansion.Log.LogError(ex.Message);
                            }
                        }
                        newLevel.spawnableScrap = tmpSpawnableScrap2;

                        newLevel.minScrap = newMoon.MinScrap;
                        newLevel.maxScrap = newMoon.MaxScrap;

                        if (newMoon.LevelAmbienceClips != null && newMoon.LevelAmbienceClips.Length > 0 && AssetGather.Instance.levelAmbiances.ContainsKey(newMoon.LevelAmbienceClips))
                        {
                            newLevel.levelAmbienceClips = AssetGather.Instance.levelAmbiances[newMoon.LevelAmbienceClips];
                        }
                        else
                        {
                            newLevel.levelAmbienceClips = AssetGather.Instance.levelAmbiances.First().Value;
                        }

                        newLevel.maxEnemyPowerCount = newMoon.MaxEnemyPowerCount;

                        var tmpEnemies1 = newMoon.Enemies();
                        List<SpawnableEnemyWithRarity> tmpEnemies2 = new List<SpawnableEnemyWithRarity>();
                        foreach (var item in tmpEnemies1)
                        {
                            tmpEnemies2.Add(new SpawnableEnemyWithRarity() { enemyType = AssetGather.Instance.enemies[item.EnemyName], rarity = item.SpawnWeight });
                        }
                        newLevel.Enemies = tmpEnemies2;

                        newLevel.enemySpawnChanceThroughoutDay = newMoon.EnemySpawnChanceThroughoutDay;
                        newLevel.spawnProbabilityRange = newMoon.SpawnProbabilityRange;

                        var tmpSpawnableMapObjects1 = newMoon.SpawnableMapObjects();
                        List<SpawnableMapObject> tmpSpawnableMapObjects2 = new List<SpawnableMapObject>();
                        foreach (var item in tmpSpawnableMapObjects1)
                        {
                            tmpSpawnableMapObjects2.Add(new SpawnableMapObject() { prefabToSpawn = AssetGather.Instance.mapObjects[item.ObjectName], spawnFacingAwayFromWall = item.SpawnFacingAwayFromWall, numberToSpawn = item.SpawnRate });
                        }
                        newLevel.spawnableMapObjects = tmpSpawnableMapObjects2.ToArray();

                        var tmpSpawnableOutsideObjects1 = newMoon.SpawnableOutsideObjects();
                        List<SpawnableOutsideObjectWithRarity> tmpSpawnableOutsideObjects2 = new List<SpawnableOutsideObjectWithRarity>();
                        foreach (var item in tmpSpawnableOutsideObjects1)
                        {
                            tmpSpawnableOutsideObjects2.Add(new SpawnableOutsideObjectWithRarity() { spawnableObject = AssetGather.Instance.outsideObjects[item.ObjectName], randomAmount = item.SpawnRate });
                        }
                        newLevel.spawnableOutsideObjects = tmpSpawnableOutsideObjects2.ToArray();

                        newLevel.maxOutsideEnemyPowerCount = newMoon.MaxOutsideEnemyPowerCount;
                        newLevel.maxDaytimeEnemyPowerCount = newMoon.MaxDaytimeEnemyPowerCount;

                        var tmpOutsideEnemies1 = newMoon.OutsideEnemies();
                        List<SpawnableEnemyWithRarity> tmpOutsideEnemies2 = new List<SpawnableEnemyWithRarity>();
                        foreach (var item in tmpOutsideEnemies1)
                        {
                            tmpOutsideEnemies2.Add(new SpawnableEnemyWithRarity() { enemyType = AssetGather.Instance.enemies[item.EnemyName], rarity = item.SpawnWeight });
                        }
                        newLevel.OutsideEnemies = tmpOutsideEnemies2;

                        var tmpDaytimeEnemies1 = newMoon.DaytimeEnemies();
                        List<SpawnableEnemyWithRarity> tmpDaytimeEnemies2 = new List<SpawnableEnemyWithRarity>();
                        foreach (var item in tmpDaytimeEnemies1)
                        {
                            tmpDaytimeEnemies2.Add(new SpawnableEnemyWithRarity() { enemyType = AssetGather.Instance.enemies[item.EnemyName], rarity = item.SpawnWeight });
                        }
                        newLevel.DaytimeEnemies = tmpDaytimeEnemies2;

                        newLevel.outsideEnemySpawnChanceThroughDay = newMoon.OutsideEnemySpawnChanceThroughDay;
                        newLevel.daytimeEnemySpawnChanceThroughDay = newMoon.DaytimeEnemySpawnChanceThroughDay;
                        newLevel.daytimeEnemiesProbabilityRange = newMoon.DaytimeEnemiesProbabilityRange;
                        newLevel.levelIncludesSnowFootprints = newMoon.LevelIncludesSnowFootprints;

                        newLevel.SetFireExitAmountOverwrite(newMoon.FireExitsAmountOverwrite);

                        __instance.moonsCatalogueList = __instance.moonsCatalogueList.AddItem(newLevel).ToArray();

                        TerminalKeyword moonKeyword = ScriptableObject.CreateInstance<TerminalKeyword>();
                        moonKeyword.word = newMoon.RouteWord != null || newMoon.RouteWord.Length >= 3 ? newMoon.RouteWord.ToLower() : Regex.Replace(newMoon.MoonName, @"\s", "").ToLower();
                        moonKeyword.name = newMoon.MoonName;
                        moonKeyword.defaultVerb = routeKeyword;
                        __instance.terminalNodes.allKeywords = __instance.terminalNodes.allKeywords.AddItem(moonKeyword).ToArray();

                        TerminalNode moonRouteConfirm = ScriptableObject.CreateInstance<TerminalNode>();
                        moonRouteConfirm.name = newMoon.MoonName.ToLower() + "RouteConfirm";
                        moonRouteConfirm.displayText = $"Routing autopilot to {newMoon.PlanetName}.\r\nYour new balance is [playerCredits].\r\n\r\n{newMoon.BoughtComment}\r\n\r\n";
                        moonRouteConfirm.clearPreviousText = true;
                        moonRouteConfirm.buyRerouteToMoon = StartOfRound.Instance.levels.Length;
                        moonRouteConfirm.lockedInDemo = true;
                        moonRouteConfirm.itemCost = newMoon.RoutePrice;

                        TerminalNode moonRoute = ScriptableObject.CreateInstance<TerminalNode>();
                        moonRoute.name = newMoon.MoonName.ToLower() + "Route";
                        moonRoute.displayText = $"The cost to route to {newMoon.PlanetName} is [totalCost]. It is \r\ncurrently [currentPlanetTime] on this moon.\r\n\r\nPlease CONFIRM or DENY.\r\n\r\n\r\n";
                        moonRoute.clearPreviousText = true;
                        moonRoute.buyRerouteToMoon = -2;
                        moonRoute.displayPlanetInfo = StartOfRound.Instance.levels.Length;
                        moonRoute.lockedInDemo = true;
                        moonRoute.overrideOptions = true;
                        moonRoute.itemCost = newMoon.RoutePrice;
                        moonRoute.terminalOptions = new CompatibleNoun[]
                        {
                            new CompatibleNoun(){ noun = denyKeyword, result = cancelRouteNode != null ? cancelRouteNode : new TerminalNode() },
                            new CompatibleNoun(){ noun = confirmKeyword, result = moonRouteConfirm },
                        };

                        CompatibleNoun moonNoun = new CompatibleNoun();

                        moonNoun.noun = moonKeyword;
                        moonNoun.result = moonRoute;
                        routeKeyword.compatibleNouns = routeKeyword.compatibleNouns.AddItem(moonNoun).ToArray();

                        StartOfRound.Instance.levels = StartOfRound.Instance.levels.AddItem(newLevel).ToArray();

                        newMoons.Add(newLevel.levelID, newMoon);

                        LethalExpansion.Log.LogInfo(newMoon.MoonName + " Moon added.");
                    }
                    catch (Exception ex)
                    {
                        LethalExpansion.Log.LogError(ex.Message);
                    }
                }
            }

            moonsPatched = true;
        }

        private static void Hotfix_DoubleRoutes()
        {
            try
            {
                LethalExpansion.Log.LogDebug("Hotfix: Removing duplicated routes");
                HashSet<string> uniqueNames = new HashSet<string>();
                List<CompatibleNoun> uniqueNouns = new List<CompatibleNoun>();

                int duplicateCount = 0;

                foreach (CompatibleNoun noun in routeKeyword.compatibleNouns)
                {
                    if (!uniqueNames.Contains(noun.result.name) || noun.result.name == "Daily Moon" /* MoonOfTheDay 1.0.4 compatibility workaround*/)
                    {
                        uniqueNames.Add(noun.result.name);
                        uniqueNouns.Add(noun);
                    }
                    else
                    {
                        LethalExpansion.Log.LogDebug($"{noun.result.name} duplicated route removed.");
                        duplicateCount++;
                    }
                }

                routeKeyword.compatibleNouns = uniqueNouns.ToArray();

                LethalExpansion.Log.LogDebug($"Hotfix: {duplicateCount} duplicated route(s) removed");
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex.Message);
            }
        }

        private static void ResetTerminalKeywords(Terminal __instance)
        {
            try
            {
                if (defaultTerminalKeywords == null || defaultTerminalKeywords.Length == 0)
                {
                    defaultTerminalKeywords = __instance.terminalNodes.allKeywords;
                }
                else
                {
                    __instance.terminalNodes.allKeywords = defaultTerminalKeywords;
                }

                LethalExpansion.Log.LogInfo("Terminal reset.");
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex.Message);
            }
        }

        private static void UpdateMoonsCatalogue(Terminal __instance)
        {
            try
            {
                string text = "Welcome to the exomoons catalogue.\r\nTo route the autopilot to a moon, use the word ROUTE.\r\nTo learn about any moon, use the word INFO.\r\n____________________________\r\n\r\n* The Company building   //   Buying at [companyBuyingPercent].\r\n\r\n";

                foreach (SelectableLevel moon in __instance.moonsCatalogueList)
                {
                    bool isHidden = newMoons.ContainsKey(moon.levelID) && newMoons[moon.levelID].IsHidden;
                    if (isHidden)
                    {
                        text += "[hidden]";
                    }

                    text += $"* {moon.PlanetName} [planetTime]\r\n";
                }
                text += "\r\n";

                __instance.terminalNodes.allKeywords.First(node => node.name == "Moons").specialKeywordResult.displayText = text;
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex.Message);
            }
        }

        [HarmonyPatch("TextPostProcess")]
        [HarmonyPostfix]
        private static void TextPostProcess_Postfix(Terminal __instance, ref string __result)
        {
            __result = TextHidder(__result);
        }

        private static string TextHidder(string inputText)
        {
            try
            {
                string pattern = @"^\[hidden\].*\r\n";
                string result = Regex.Replace(inputText, pattern, "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                return result;
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex.Message);
            }
            return inputText;
        }

        public static T[] RemoveElementFromArray<T>(T[] originalArray, int indexToRemove)
        {
            if (indexToRemove < 0 || indexToRemove >= originalArray.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(indexToRemove));
            }

            T[] newArray = new T[originalArray.Length - 1];
            for (int i = 0, j = 0; i < originalArray.Length; i++)
            {
                if (i != indexToRemove)
                {
                    newArray[j] = originalArray[i];
                    j++;
                }
            }

            return newArray;
        }

        public static void RemoveMoon(Terminal __instance, string moonName)
        {
            try
            {
                if (moonName == null)
                {
                    return;
                }

                CompatibleNoun[] nouns = routeKeyword.compatibleNouns;
                if (!__instance.moonsCatalogueList.Any(level => level.name.Contains(moonName)) && !nouns.Any(level => level.noun.name.Contains(moonName)))
                {
                    LethalExpansion.Log.LogInfo($"{moonName} moon not exist.");
                    return;
                }

                for (int i = 0; i < __instance.moonsCatalogueList.Length; i++)
                {
                    if (__instance.moonsCatalogueList[i].name.Contains(moonName))
                    {
                        __instance.moonsCatalogueList = RemoveElementFromArray(__instance.moonsCatalogueList, i);
                    }
                }

                for (int i = 0; i < nouns.Length; i++)
                {
                    if (nouns[i].noun.name.Contains(moonName))
                    {
                        routeKeyword.compatibleNouns = RemoveElementFromArray(nouns, i);
                    }
                }

                if (!__instance.moonsCatalogueList.Any(level => level.name.Contains(moonName)) &&
                    !routeKeyword.compatibleNouns.Any(level => level.noun.name.Contains(moonName)))
                {
                    LethalExpansion.Log.LogInfo($"{moonName} moon removed.");
                }
                else
                {
                    LethalExpansion.Log.LogInfo($"{moonName} moon failed to remove.");
                }
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex.Message);
            }
        }

        private static void SaveFireExitAmounts()
        {
            try
            {
                if (!flowFireExitSaved)
                {
                    foreach (var flow in RoundManager.Instance.dungeonFlowTypes)
                    {
                        flow.SetDefaultFireExitAmount(flow.GlobalProps.First(p => p.ID == 1231).Count.Min);
                    }
                    flowFireExitSaved = true;
                }
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex.Message);
            }
        }

        public static void ResetFireExitAmounts()
        {
            try
            {
                if (flowFireExitSaved)
                {
                    foreach (var flow in RoundManager.Instance.dungeonFlowTypes)
                    {
                        flow.GlobalProps.First(p => p.ID == 1231).Count = new DunGen.IntRange(flow.GetDefaultFireExitAmount(), flow.GetDefaultFireExitAmount());
                    }
                }
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex.Message);
            }
        }
    }
}
