using HarmonyLib;
using HMLLibrary;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;
using I2.Loc;
using System.Text;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;


namespace Raftmobile
{
    public class Main : Mod
    {
        Harmony harmony;
        List<Object> createdObjects = new List<Object>();
        Transform prefabHolder;
        public static bool loaded = false;
        public static LanguageSourceData language;
        public static Block_SnowmobileStation waitingToInitialize;
        public static BinaryFormatter formatter = new BinaryFormatter() { Binder = new PreMergeToMergedDeserializationBinder() };
        public void Start()
        {
            if (RAPI.IsCurrentSceneGame() && Raft_Network.IsHost && ComponentManager<Raft_Network>.Value.remoteUsers.Count > 1)
            {
                Debug.LogError("[Raftmobile]: Cannot load while in a multiplayer");
                modlistEntry.modinfo.unloadBtn.GetComponent<Button>().onClick.Invoke();
                return;
            }
            loaded = true;
            prefabHolder = new GameObject("prefabHolder").transform;
            prefabHolder.gameObject.SetActive(false);
            DontDestroyOnLoad(prefabHolder.gameObject);
            createdObjects.Add(prefabHolder.gameObject);

            language = new LanguageSourceData()
            {
                mDictionary = new Dictionary<string, TermData>
                {
                    ["Item/Placeable_SnowmobileStation"] = new TermData() { Languages = new[] { "Snowmobile Platform@A platform for keeping a snowmobile on the raft for personal use" } },
                    ["Game/ResetSnowmobile"] = new TermData() { Languages = new[] { "Hold: Reset Snowmobile" } }
                },
                mLanguages = new List<LanguageData> { new LanguageData() { Code = "en", Name = "English" } }
            };
            LocalizationManager.Sources.Add(language);

            var brickItem = ItemManager.GetItemByIndex(72);
            var stationItem = brickItem.Clone(24301, "Placeable_SnowmobileStation");
            createdObjects.Add(stationItem);
            stationItem.name = stationItem.UniqueName;
            stationItem.settings_Inventory.LocalizationTerm = "Item/Placeable_SnowmobileStation";
            var t = LoadImage("SnowmobileStation.png",true);
            var t2 = new Texture2D(t.width, t.height, t.format, false);
            t2.SetPixels(t.GetPixels(0));
            t2.Apply();
            Destroy(t);
            stationItem.settings_Inventory.Sprite = t2.ToSprite();
            createdObjects.Add(t2);
            createdObjects.Add(stationItem.settings_Inventory.Sprite);
            stationItem.SetRecipe(new[]
                {
                    new CostMultiple(new[] { ItemManager.GetItemByIndex(307) }, 10), // Titanium Ingot
                    new CostMultiple(new[] { ItemManager.GetItemByIndex(178) }, 6), // Circuit Board
                    new CostMultiple(new[] { ItemManager.GetItemByIndex(155),ItemManager.GetItemByIndex(154) }, 6), // Hinge, Bolt
                    new CostMultiple(new[] { ItemManager.GetItemByIndex(246) }, 6), // Leather
                    new CostMultiple(new[] { ItemManager.GetItemByIndex(243) }, 6), // Wool
                    new CostMultiple(new[] { ItemManager.GetItemByIndex(67) }, 3) // Glass
                }, CraftingCategory.Navigation, 1, false);

            var shedPrefab = Instantiate(stationItem.settings_buildable.GetBlockPrefab(0), prefabHolder, false);
            shedPrefab.name = "Block_SnowmobileStation";
            var cc = shedPrefab.gameObject.AddComponent<Block_SnowmobileStation>();
            cc.CopyFieldsOf(shedPrefab);
            cc.ReplaceValues(shedPrefab, cc);
            shedPrefab.ReplaceValues(brickItem, stationItem);
            DestroyImmediate(shedPrefab);
            shedPrefab = cc;
            waitingToInitialize = cc;
            DestroyImmediate(shedPrefab.GetComponent<Brick_Wet>());
            DestroyImmediate(shedPrefab.transform.Find("model").gameObject);
            var m = new GameObject("model");
            m.transform.SetParent(shedPrefab.transform, false);
            m.transform.localPosition += Vector3.up * 0.15f;
            var m2 = ItemManager.GetItemByIndex(439).settings_buildable.GetBlockPrefab(0).transform.GetChild(0);
            m.AddComponent<MeshFilter>().sharedMesh = m2.GetComponent<MeshFilter>().sharedMesh;
            m.AddComponent<MeshRenderer>().material = m2.GetComponent<MeshRenderer>().material;
            m.transform.localScale = new Vector3(2f, 10, 3f);
            var c = shedPrefab.GetComponent<BoxCollider>();
            c.size = new Vector3(1.5f, 0.15f, 3.8f);
            c.center = new Vector3(0, c.size.y / 2, 0);
            Traverse.Create(stationItem.settings_buildable).Field("blockPrefabs").SetValue(new[] { shedPrefab });
            foreach (var q in Resources.FindObjectsOfTypeAll<SO_BlockQuadType>())
                if (q.AcceptsBlock(brickItem))
                    Traverse.Create(q).Field("acceptableBlockTypes").GetValue<List<Item_Base>>().Add(stationItem);
            foreach (var q in Resources.FindObjectsOfTypeAll<SO_BlockCollisionMask>())
                if (q.IgnoresBlock(brickItem))
                    Traverse.Create(q).Field("blockTypesToIgnore").GetValue<List<Item_Base>>().Add(stationItem);
            shedPrefab.networkType = NetworkType.NetworkIDBehaviour;
            cc.spawnPoint = new GameObject("spawnPoint").transform;
            cc.spawnPoint.SetParent(cc.transform, false);
            cc.spawnPoint.localPosition = Vector3.up * 0.3f;
            RAPI.RegisterItem(stationItem);

            (harmony = new Harmony("com.aidanamite.Raftmobile")).PatchAll();
            StartCoroutine(UseScene("55#Landmark_Temperance#", TryInitialize));
            Traverse.Create(typeof(LocalizationManager)).Field("OnLocalizeEvent").GetValue<LocalizationManager.OnLocalizeCallback>().Invoke();
            Log("Mod has been loaded!");
        }

        IEnumerator UseScene(string sceneName, Action onComplete)
        {
            if (SceneManager.GetSceneByName(sceneName).isLoaded)
            {
                onComplete();
                yield break;
            }
            var async = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            async.allowSceneActivation = false;
            async.completed += delegate
            {
                harmony.Unpatch(typeof(RConsole).GetMethod("HandleUnityLog", BindingFlags.Instance | BindingFlags.NonPublic), HarmonyPatchType.Prefix, harmony.Id);
                onComplete();
                foreach (var g in SceneManager.GetSceneByName(sceneName).GetRootGameObjects())
                    DestroyImmediate(g);
                SceneManager.UnloadSceneAsync(sceneName);
            };
            while (async.progress < 0.9f)
                yield return null;
            harmony.Patch(typeof(RConsole).GetMethod("HandleUnityLog", BindingFlags.Instance | BindingFlags.NonPublic), new HarmonyMethod(typeof(Patch_Log),nameof(Patch_Log.Prefix)));
            async.allowSceneActivation = true;
            yield break;
        }

        public void OnModUnload()
        {
            if (!loaded)
                return;
            waitingToInitialize = null;
            harmony?.UnpatchAll(harmony.Id);
            LocalizationManager.Sources.Remove(language);
            ItemManager.GetAllItems().RemoveAll(x => createdObjects.Any(y => (y as Item_Base)?.UniqueIndex == x?.UniqueIndex));
            foreach (var block in BlockCreator.GetPlacedBlocks().ToArray())
                if (createdObjects.Any(y => (y as Item_Base)?.UniqueIndex == block.buildableItem?.UniqueIndex))
                    BlockCreator.RemoveBlock(block, null, false);
            foreach (var q in Resources.FindObjectsOfTypeAll<SO_BlockQuadType>())
                Traverse.Create(q).Field("acceptableBlockTypes").GetValue<List<Item_Base>>().RemoveAll(x => createdObjects.Any(y => (y as Item_Base)?.UniqueIndex == x?.UniqueIndex));
            foreach (var q in Resources.FindObjectsOfTypeAll<SO_BlockCollisionMask>())
                Traverse.Create(q).Field("blockTypesToIgnore").GetValue<List<Item_Base>>().RemoveAll(x => createdObjects.Any(y => (y as Item_Base)?.UniqueIndex == x?.UniqueIndex));
            foreach (var o in createdObjects)
                Destroy(o);
            Log("Mod has been unloaded!");
        }

        public void TryInitialize()
        {
            if (!waitingToInitialize)
                return;
            var shed = Resources.FindObjectsOfTypeAll<SnowmobileShed>().FirstOrDefault();
            if (!shed)
                return;
            var newShed = waitingToInitialize.gameObject.AddComponent<SnowmobileShed>();
            Traverse.Create(newShed).Field("eventRef_respawn").SetValue(Traverse.Create(shed).Field("eventRef_respawn").GetValue());
            var newMobile = Instantiate(Traverse.Create(shed).Field("snowmobilePrefab").GetValue<Snowmobile>(), prefabHolder);
            Traverse.Create(newShed).Field("snowmobilePrefab").SetValue(newMobile);
            waitingToInitialize.networkedIDBehaviour = newShed;
            waitingToInitialize.shed = newShed;
            Traverse.Create(newShed).Field("spawnPoint").SetValue(waitingToInitialize.spawnPoint);
            waitingToInitialize = null;
        }

        public Texture2D LoadImage(string filename, bool leaveReadable = false)
        {
            var t = new Texture2D(0, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
            t.LoadImage(GetEmbeddedFileBytes(filename), !leaveReadable);
            if (leaveReadable)
                t.Apply();
            return t;
        }
        public static T CreateObject<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    }

    public class Block_SnowmobileStation : Block, IRaycastable
    {
        public Transform spawnPoint;
        public SnowmobileShed shed;
        public Network_Player localPlayer;
        public override RGD_Block GetBlockCreationData()
        {
            var r = Main.CreateObject<RGD_Storage>();
            r.CopyFieldsOf(base.GetBlockCreationData());
            var s = Main.CreateObject<RGD_Slot>();
            var m = new MemoryStream();
            Main.formatter.Serialize(m, shed.spawnedSnowmobile.GetRGD());
            while (m.Position % 4 != 0)
                m.WriteByte(0);
            s.exclusiveString = Encoding.Unicode.GetString(m.ToArray());
            r.slots = new[] { s };
            return r;
        }
        public override RGD Serialize_Save() => GetBlockCreationData();
        public void Restore(RGD_Block block)
        {
            if (block is RGD_Storage)
                shed.Restore(Main.formatter.Deserialize(new MemoryStream(Encoding.Unicode.GetBytes((block as RGD_Storage).slots[0].exclusiveString))) as RGD_Snowmobile);
        }
        public override void OnFinishedPlacement()
        {
            base.OnFinishedPlacement();
            shed.SpawnSnowmobileNetwork();
        }

        protected override void Start()
        {
            base.Start();
            localPlayer = ComponentManager<Network_Player>.Value;
        }

        static readonly MethodInfo _ResetSnowmobileToShed = typeof(SnowmobileShed).GetMethod("ResetSnowmobileToShed", ~BindingFlags.Default);
        public bool ResetSnowmobileToShed(bool viaWater = false) => (bool)_ResetSnowmobileToShed.Invoke(shed, new object[] { viaWater });

        void IRaycastable.OnRayEnter() { }
        void IRaycastable.OnIsRayed()
        {
            if (!PlayerItemManager.IsBusy && CanvasHelper.ActiveMenu == MenuType.None && !localPlayer.PlayerScript.IsDead)
            {
                ComponentManager<CanvasHelper>.Value.displayTextManager.ShowText(Helper.GetTerm("Game/ResetSnowmobile"), MyInput.Keybinds["Interact"].MainKey, 0, 0, true);
                if (MyInput.GetButton("Interact"))
                {
                    ResetTimer += Time.deltaTime;
                    if (ResetTimer >= resetHoldTime)
                    {
                        resetTimer = 0;
                        ResetSnowmobileToShed();
                    }
                }
            }
            if (MyInput.GetButtonUp("Interact"))
                ResetTimer = 0f;
        }
        void IRaycastable.OnRayExit()
        {
            ComponentManager<CanvasHelper>.Value.displayTextManager.HideDisplayTexts();
            ComponentManager<CanvasHelper>.Value.SetLoadCircle(false);
            resetTimer = 0;
        }
        float resetTimer = 0;
        public float resetHoldTime = 1;
        public float ResetTimer
        {
            get => resetTimer;
            set
            {
                resetTimer = value;
                ComponentManager<CanvasHelper>.Value.SetLoadCircle(resetTimer / resetHoldTime);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            shed?.spawnedSnowmobile?.MakeAllPlayersLeave();
            Destroy(shed?.spawnedSnowmobile?.gameObject);
        }
    }

    sealed class PreMergeToMergedDeserializationBinder : SerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            string exeAssembly = Assembly.GetExecutingAssembly().FullName;
            Type typeToDeserialize = Type.GetType($"{typeName}, {exeAssembly}");
            return typeToDeserialize;
        }
    }

    static class ExtentionMethods
    {
        public static Item_Base Clone(this Item_Base source, int uniqueIndex, string uniqueName)
        {
            Item_Base item = ScriptableObject.CreateInstance<Item_Base>();
            item.Initialize(uniqueIndex, uniqueName, source.MaxUses);
            item.settings_buildable = source.settings_buildable.Clone();
            item.settings_consumeable = source.settings_consumeable.Clone();
            item.settings_cookable = source.settings_cookable.Clone();
            item.settings_equipment = source.settings_equipment.Clone();
            item.settings_Inventory = source.settings_Inventory.Clone();
            item.settings_recipe = source.settings_recipe.Clone();
            item.settings_usable = source.settings_usable.Clone();
            return item;
        }
        public static void SetRecipe(this Item_Base item, CostMultiple[] cost, CraftingCategory category = CraftingCategory.Resources, int amountToCraft = 1, bool learnedFromBeginning = false, string subCategory = null, int subCatergoryOrder = 0)
        {
            Traverse recipe = Traverse.Create(item.settings_recipe);
            recipe.Field("craftingCategory").SetValue(category);
            recipe.Field("amountToCraft").SetValue(amountToCraft);
            recipe.Field("learnedFromBeginning").SetValue(learnedFromBeginning);
            recipe.Field("subCategory").SetValue(subCategory);
            recipe.Field("subCatergoryOrder").SetValue(subCatergoryOrder);
            item.settings_recipe.NewCost = cost;
        }

        public static void CopyFieldsOf(this object value, object source)
        {
            var t1 = value.GetType();
            var t2 = source.GetType();
            while (!t1.IsAssignableFrom(t2))
                t1 = t1.BaseType;
            while (t1 != typeof(Object) && t1 != typeof(object))
            {
                foreach (var f in t1.GetFields(~BindingFlags.Default))
                    if (!f.IsStatic)
                        f.SetValue(value, f.GetValue(source));
                t1 = t1.BaseType;
            }
        }

        public static void ReplaceValues(this Component value, object original, object replacement)
        {
            foreach (var c in value.GetComponentsInChildren<Component>())
                (c as object).ReplaceValues(original, replacement);
        }
        public static void ReplaceValues(this GameObject value, object original, object replacement)
        {
            foreach (var c in value.GetComponentsInChildren<Component>())
                (c as object).ReplaceValues(original, replacement);
        }

        public static void ReplaceValues(this object value, object original, object replacement)
        {
            var t = value.GetType();
            while (t != typeof(Object) && t != typeof(object))
            {
                foreach (var f in t.GetFields(~BindingFlags.Default))
                    if (!f.IsStatic && f.GetValue(value) == original)
                        f.SetValue(value, replacement);
                t = t.BaseType;
            }
        }

        public static Sprite ToSprite(this Texture2D texture, Rect? rect = null, Vector2? pivot = null) => Sprite.Create(texture, rect ?? new Rect(0, 0, texture.width, texture.height), pivot ?? new Vector2(0.5f, 0.5f));
    }

    [HarmonyPatch(typeof(RGD_Block),"RestoreBlock")]
    static class Patch_RestoreBlock
    {
        static void Postfix(RGD_Block __instance, Block block)
        {
            (block as Block_SnowmobileStation)?.Restore(__instance);
        }
    }

    [HarmonyPatch(typeof(Snowmobile), "Update")]
    static class Patch_Snowmobile_Update
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            code.Insert(code.FindLastIndex(code.FindIndex(x => x.opcode == OpCodes.Call && (x.operand as MethodInfo).Name == "Raycast"), x => x.opcode == OpCodes.Ldsfld && (x.operand as FieldInfo).Name == "MASK_Obstruction") + 1, new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Patch_Snowmobile_Update),nameof(EditMask))));
            return code;
        }
        public static LayerMask EditMask(LayerMask original) => original | LayerMasks.MASK_GroundMask_Raft;
        static void Postfix(Snowmobile __instance, Transform ___groundCheckPoint, Rigidbody ___body)
        {
            var flag = Physics.Raycast(___groundCheckPoint.position, Vector3.down, out var hit, 100, LayerMasks.MASK_GroundMask_Raft) && hit.collider.transform.IsChildOf(SingletonGeneric<GameManager>.Singleton.lockedPivot);
            if (___body.transform.ParentedToRaft() != flag)
                ___body.transform.SetParent(flag ? SingletonGeneric<GameManager>.Singleton.lockedPivot : null, true);
        }
    }

    [HarmonyPatch(typeof(LanguageSourceData), "GetLanguageIndex")]
    static class Patch_GetLanguageIndex
    {
        static void Postfix(LanguageSourceData __instance, ref int __result)
        {
            if (__result == -1 && __instance == Main.language)
                __result = 0;
        }
    }

    [HarmonyPatch(typeof(BaseModHandler), "UnloadMod")]
    public class Patch_ModUnload
    {
        static bool Prefix(ModData moddata)
        {
            if (Main.loaded && moddata.modinfo.mainClass is Main && RAPI.IsCurrentSceneGame() && Raft_Network.IsHost && ComponentManager<Raft_Network>.Value.remoteUsers.Count > 1)
            {
                Debug.LogError("[Raftmobile]: Cannot unload while in a multiplayer");
                return false;
            }
            return true;
        }
    }

    class Patch_Log
    {
        public static bool Prefix() => false;
    }
}