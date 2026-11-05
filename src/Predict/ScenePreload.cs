using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TellMeCosmetics.Predict;

// Reads the maze scenes straight from Labyrinthine_Data (AssetsTools.NET, no Unity API, safe off the main thread) and builds the same
// MazePipeline.Config / ContractPredictor.Pool that MazeCapture / ContractPredictor.Capture record when the player enters a maze.
public static class ScenePreload
{
    public sealed class Result { public MazePipeline.Config Maze; public ContractPredictor.Pool Pool; }

    // Game build the caches belong to; caches stamped with another build read as missing and get rebuilt.
    private static string stamp;
    public static string Stamp => stamp ??= $"{MelonLoader.InternalUtils.UnityInformationHandler.GameVersion}/" +
        new FileInfo(Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "GameAssembly.dll")).Length;

    // Baseline or preloaded values per maze type, kept for the DebugMazeTests comparison with what the game holds in the maze.
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Result> Results = new();

    // On a background thread: the baseline shipped in the mod when it was built for this game version, otherwise
    // (game updated) reads the maze scenes for the maze types missing from the caches (all of them with debug, for the comparison).
    public static void Start(bool debug)
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            var thread = Il2CppInterop.Runtime.IL2CPP.il2cpp_thread_attach(Il2CppInterop.Runtime.IL2CPP.il2cpp_domain_get());
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (LoadBaseline())
                {
                    MelonLoader.MelonLogger.Msg($"Preload: game version matches the baseline ({Stamp}), using it; scenes not read");
                    return;
                }
                bool Missing(int t) => MazePipeline.Get(t) == null || ContractPredictor.GetPool(t) == null;
                var read = ReadAll(MelonLoader.Utils.MelonEnvironment.UnityGameDataDirectory, new Il2CppLayouts(), t => debug || Missing(t),
                    msg => MelonLoader.MelonLogger.Warning($"Preload: {msg}"));
                var stored = new List<int>();
                foreach (var (type, r) in read)
                {
                    Results[type] = r;
                    if (!Missing(type)) continue;
                    MazePipeline.Store(r.Maze, replace: false);
                    if (r.Pool != null) ContractPredictor.StoreIfMissing(type, r.Pool);
                    stored.Add(type);
                }
                MelonLoader.MelonLogger.Msg($"Preload: no baseline for build {Stamp}, read {read.Count} maze scenes in {sw.ElapsedMilliseconds} ms, cached maze types [{string.Join(",", stored)}]");
            }
            catch (Exception ex) { MelonLoader.MelonLogger.Warning($"Preload failed: {ex}"); }
            finally { Il2CppInterop.Runtime.IL2CPP.il2cpp_thread_detach(thread); }
        });
    }

    // Caches shipped inside the mod (baseline/ in the repo: copies of UserData caches from the developer's game). Used only when their
    // stamp matches this game build; they fill missing entries, so in-game captures stay. False when the game version differs.
    private static bool LoadBaseline()
    {
        bool matched = false;
        var asm = typeof(ScenePreload).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(r => r.StartsWith("baseline.")))
        {
            using var s = asm.GetManifestResourceStream(name);
            if (name == "baseline.TellMeCosmetics.maze.bin")
                foreach (var c in MazePipeline.Parse(s, Stamp).Values)
                {
                    Results.GetOrAdd(c.MazeType, _ => new Result()).Maze = c;
                    MazePipeline.Store(c, replace: false);
                    matched = true;
                }
            else if (name.StartsWith("baseline.pool.") && int.TryParse(name.Split('.')[2], out int type))
            {
                using var r = new StreamReader(s);
                var lines = new List<string>();
                for (string line; (line = r.ReadLine()) != null;) lines.Add(line); // same split as File.ReadAllLines (keeps empty pool lines)
                var pool = ContractPredictor.Parse(lines.ToArray(), Stamp);
                if (pool == null) continue;
                Results.GetOrAdd(type, _ => new Result()).Pool = pool;
                ContractPredictor.StoreIfMissing(type, pool);
            }
        }
        return matched;
    }

    // DebugMazeTests: preloaded vs captured in the maze; logs MATCH or the differing fields.
    public static void Check(int mazeType, MazePipeline.Config captured, ContractPredictor.Pool capturedPool)
    {
        if (!Results.TryGetValue(mazeType, out var r)) { MelonLoader.MelonLogger.Msg($"🧪Preload maze type {mazeType}: NOT PRELOADED (no baseline or scene for it)"); return; }
        if (captured != null && r.Maze != null) Report($"maze type {mazeType} config", MazePipeline.Dump(r.Maze), MazePipeline.Dump(captured));
        if (capturedPool != null) Report($"maze type {mazeType} pool", r.Pool == null ? new string[0] : ContractPredictor.Lines(r.Pool), ContractPredictor.Lines(capturedPool));
    }

    private static void Report(string what, string[] preload, string[] game)
    {
        var diff = Enumerable.Range(0, Math.Max(preload.Length, game.Length))
            .Where(i => i >= preload.Length || i >= game.Length || preload[i] != game[i])
            .Select(i => $"\n  preload: {(i < preload.Length ? preload[i] : "-")}\n  game:    {(i < game.Length ? game[i] : "-")}").ToList();
        MelonLoader.MelonLogger.Msg($"🧪Preload {what} {(diff.Count == 0 ? "MATCH" : "MISMATCH" + string.Concat(diff))}");
    }

    // The build ships without type trees, so script layouts come from the game's own type info (Il2CppLayouts in game).
    // Mirrors Unity's serialization rules; a type handle is whatever the source uses, null meaning "none".
    public abstract class LayoutSource : ILayouts
    {
        public sealed record FieldInfo(string Name, object Type, bool SerializeReference);
        protected abstract object FindClass(string assembly, string ns, string name);
        protected abstract string FullName(object t);                 // "System.Int32", "UnityEngine.Vector2Int", ...
        protected abstract object ArrayElement(object t);                   // element of T[] or List<T>, else null
        protected abstract object EnumUnderlying(object t);                 // null when not an enum
        protected abstract bool IsUnityObject(object t);
        protected abstract bool IsSerializableClass(object t);         // [Serializable], not abstract, not a System.* type
        protected abstract IEnumerable<FieldInfo> SerializedFields(object t); // base classes first; public or [SerializeField], not static/const/readonly/[NonSerialized]

        private readonly Dictionary<string, List<AssetTypeTemplateField>> cache = new();

        public List<AssetTypeTemplateField> ScriptFields(string assembly, string ns, string name)
        {
            string key = $"{assembly}|{ns}|{name}";
            if (!cache.TryGetValue(key, out var fields))
            {
                var t = FindClass(assembly, ns, name);
                cache[key] = fields = t == null ? null : Fields(t, 0);
            }
            return fields;
        }

        private List<AssetTypeTemplateField> Fields(object t, int depth) =>
            SerializedFields(t).Select(f => f.SerializeReference ? Prim(f.Name, "SInt64", AssetValueType.Int64, false) : Field(f.Name, f.Type, depth, true))
                .Where(f => f != null).ToList();

        private AssetTypeTemplateField Field(string name, object t, int depth, bool isField)
        {
            var elem = ArrayElement(t);
            if (elem != null)
            {
                if (ArrayElement(elem) != null) return null; // Unity skips nested arrays
                var e = Field("data", elem, depth, false);
                return e == null ? null : Array(name, e);
            }
            var u = EnumUnderlying(t);
            if (u != null) return Builtin(FullName(u), name, isField);
            if (IsUnityObject(t)) return PPtr(name);
            var b = Builtin(FullName(t), name, isField);
            if (b != null) return b;
            // Unity stops serializing nested custom classes past depth 10.
            if (!IsSerializableClass(t) || depth >= 10) return null;
            return Class(name, FullName(t), Fields(t, depth + 1));
        }
    }

    public interface ILayouts { List<AssetTypeTemplateField> ScriptFields(string assembly, string ns, string name); }

    // ---------- templates ----------

    private static AssetTypeTemplateField Prim(string name, string type, AssetValueType vt, bool align) =>
        new() { Name = name, Type = type, ValueType = vt, HasValue = true, IsAligned = align, Children = new() };

    private static AssetTypeTemplateField Array(string name, AssetTypeTemplateField elem) => new()
    {
        Name = name, Type = "Array", ValueType = AssetValueType.Array, IsArray = true, HasValue = true, IsAligned = true,
        Children = new() { Prim("size", "int", AssetValueType.Int32, false), elem },
    };

    private static AssetTypeTemplateField Class(string name, string type, List<AssetTypeTemplateField> children) =>
        new() { Name = name, Type = type, ValueType = AssetValueType.None, Children = children };

    private static AssetTypeTemplateField PPtr(string name) =>
        Class(name, "PPtr", new() { Prim("m_FileID", "int", AssetValueType.Int32, false), Prim("m_PathID", "SInt64", AssetValueType.Int64, false) });

    private static AssetTypeTemplateField Floats(string name, params string[] f) => Class(name, "floats", f.Select(x => Prim(x, "float", AssetValueType.Float, false)).ToList());
    private static AssetTypeTemplateField Ints(string name, params string[] f) => Class(name, "ints", f.Select(x => Prim(x, "int", AssetValueType.Int32, false)).ToList());

    // Primitives and the Unity structs with a native serializer. 1- and 2-byte fields are padded to 4, array elements are not.
    private static AssetTypeTemplateField Builtin(string fullName, string name, bool isField) => fullName switch
    {
        "System.Boolean" => Prim(name, "bool", AssetValueType.Bool, isField),
        "System.Byte" => Prim(name, "UInt8", AssetValueType.UInt8, isField),
        "System.SByte" => Prim(name, "SInt8", AssetValueType.Int8, isField),
        "System.Int16" => Prim(name, "SInt16", AssetValueType.Int16, isField),
        "System.UInt16" or "System.Char" => Prim(name, "UInt16", AssetValueType.UInt16, isField),
        "System.Int32" => Prim(name, "int", AssetValueType.Int32, false),
        "System.UInt32" => Prim(name, "unsigned int", AssetValueType.UInt32, false),
        "System.Int64" => Prim(name, "SInt64", AssetValueType.Int64, false),
        "System.UInt64" => Prim(name, "UInt64", AssetValueType.UInt64, false),
        "System.Single" => Prim(name, "float", AssetValueType.Float, false),
        "System.Double" => Prim(name, "double", AssetValueType.Double, false),
        "System.String" => Prim(name, "string", AssetValueType.String, true),
        "UnityEngine.Vector2" => Floats(name, "x", "y"),
        "UnityEngine.Vector3" => Floats(name, "x", "y", "z"),
        "UnityEngine.Vector4" or "UnityEngine.Quaternion" => Floats(name, "x", "y", "z", "w"),
        "UnityEngine.Color" => Floats(name, "r", "g", "b", "a"),
        "UnityEngine.Rect" => Floats(name, "x", "y", "width", "height"),
        "UnityEngine.Bounds" => Class(name, "Bounds", new() { Floats("m_Center", "x", "y", "z"), Floats("m_Extent", "x", "y", "z") }),
        "UnityEngine.Matrix4x4" => Floats(name, Enumerable.Range(0, 16).Select(i => $"e{i % 4}{i / 4}").ToArray()),
        "UnityEngine.Vector2Int" => Ints(name, "m_X", "m_Y"),
        "UnityEngine.Vector3Int" => Ints(name, "m_X", "m_Y", "m_Z"),
        "UnityEngine.Color32" => Prim(name, "rgba", AssetValueType.UInt32, false),
        "UnityEngine.LayerMask" => Class(name, "BitField", new() { Prim("m_Bits", "unsigned int", AssetValueType.UInt32, false) }),
        // Serializable in Unity but with a layout this reader does not know; fail loudly instead of misreading every later field.
        "UnityEngine.AnimationCurve" or "UnityEngine.Gradient" or "UnityEngine.RectOffset" or "UnityEngine.GUIStyle"
            or "UnityEngine.BoundsInt" or "UnityEngine.RectInt" or "UnityEngine.Hash128" => throw new NotSupportedException($"Unity type {fullName} ({name})"),
        _ => null,
    };

    // MonoBehaviour header (Unity 2022.3).
    private static readonly List<AssetTypeTemplateField> Header = new()
    {
        PPtr("m_GameObject"), Prim("m_Enabled", "UInt8", AssetValueType.UInt8, true), PPtr("m_Script"), Prim("m_Name", "string", AssetValueType.String, true),
    };

    // ---------- reading ----------

    internal sealed class Reader : IDisposable
    {
        private readonly AssetsManager am = new();
        private readonly string dataDir;
        private readonly ILayouts layouts;
        private readonly Dictionary<(string, long), (string asm, string ns, string cls)> scripts = new();
        private readonly Dictionary<(string, long), AssetTypeValueField> values = new();
        private readonly Dictionary<string, AssetsFileInstance> files = new(StringComparer.OrdinalIgnoreCase);

        public Reader(string dataDir, ILayouts layouts) { this.dataDir = dataDir; this.layouts = layouts; }
        public void Dispose() => am.UnloadAll(true); // closes the game files

        private readonly Dictionary<string, (BundleFileInstance bundle, int index)> bundleEntries = new(StringComparer.OrdinalIgnoreCase);

        // Labyrinthine_Data first, then the serialized files inside bundles added with AddBundles (referenced as "archive:/CAB-.../CAB-...").
        public AssetsFileInstance File(string name)
        {
            if (!files.TryGetValue(name, out var f))
            {
                var path = Path.Combine(dataDir, name);
                files[name] = f = System.IO.File.Exists(path) ? am.LoadAssetsFile(path, false)
                    : bundleEntries.TryGetValue(name, out var e) ? am.LoadAssetsFileFromBundle(e.bundle, e.index, false) : null;
            }
            return f;
        }

        // Addressables bundles (LZ4 chunks, decompressed on demand); returns the serialized files they hold.
        public List<string> AddBundles(string dir)
        {
            var names = new List<string>();
            if (!Directory.Exists(dir)) return names;
            foreach (var path in Directory.GetFiles(dir, "*.bundle", SearchOption.AllDirectories))
            {
                var b = am.LoadBundleFile(path, false);
                var dirs = b.file.BlockAndDirInfo.DirectoryInfos;
                for (int i = 0; i < dirs.Count; i++)
                    if ((dirs[i].Flags & 4) != 0) { bundleEntries[dirs[i].Name] = (b, i); names.Add(dirs[i].Name); }
            }
            return names;
        }

        public void Unload(AssetsFileInstance f)
        {
            if (f == null) return;
            am.UnloadAssetsFile(f);
            files.Remove(f.name);
            foreach (var k in values.Keys.Where(k => k.Item1 == f.name).ToList()) values.Remove(k);
        }

        // PPtr (m_FileID, m_PathID) relative to file f → the referenced object; null for null/unresolvable refs.
        public (AssetsFileInstance f, AssetFileInfo info)? Resolve(AssetsFileInstance f, AssetTypeValueField pptr)
        {
            if (pptr == null || pptr.IsDummy) return null;
            int fileId = pptr["m_FileID"].AsInt;
            long pathId = pptr["m_PathID"].AsLong;
            if (pathId == 0) return null;
            if (fileId != 0)
            {
                var ext = f.file.Metadata.Externals;
                if (fileId - 1 >= ext.Count) return null;
                f = File(Path.GetFileName(ext[fileId - 1].PathName)); // "Resources/unity default resources" is not under Labyrinthine_Data
                if (f == null) return null;
            }
            var info = f.file.GetAssetInfo(pathId);
            return info == null ? null : (f, info);
        }

        private AssetsFileReader At(AssetsFileInstance f, AssetFileInfo info)
        {
            var r = f.file.Reader;
            r.Position = info.GetAbsoluteByteOffset(f.file);
            return r;
        }

        public (string asm, string ns, string cls) Script(AssetsFileInstance f, AssetFileInfo mb)
        {
            if (scripts.TryGetValue((f.name, mb.PathId), out var s)) return s;
            var head = Class("Base", "MonoBehaviour", Header).MakeValue(At(f, mb));
            s = default;
            if (Resolve(f, head["m_Script"]) is var (sf, si))
            {
                // MonoScript: m_Name, m_ExecutionOrder, m_PropertiesHash (16 bytes), m_ClassName, m_Namespace, m_AssemblyName.
                var r = At(sf, si);
                r.ReadCountStringInt32(); r.Align();
                r.Position += 4 + 16;
                string cls = r.ReadCountStringInt32(); r.Align();
                string ns = r.ReadCountStringInt32(); r.Align();
                string asm = r.ReadCountStringInt32(); r.Align();
                s = (asm, ns, cls);
            }
            return scripts[(f.name, mb.PathId)] = s;
        }

        // Whole MonoBehaviour / ScriptableObject value; null when it is not a script or its class is unknown.
        public AssetTypeValueField Read(AssetsFileInstance f, AssetFileInfo info)
        {
            if (info.TypeId != (int)AssetClassID.MonoBehaviour) return null;
            if (values.TryGetValue((f.name, info.PathId), out var v)) return v;
            var s = Script(f, info);
            var fields = s.cls == null ? null : layouts.ScriptFields(s.asm, s.ns, s.cls);
            v = fields == null ? null : Class("Base", s.cls, Header.Concat(fields).ToList()).MakeValue(At(f, info));
            return values[(f.name, info.PathId)] = v;
        }

        public AssetTypeValueField Deref(AssetsFileInstance f, AssetTypeValueField pptr, out AssetsFileInstance where)
        {
            where = null;
            if (Resolve(f, pptr) is not var (rf, ri)) return null;
            where = rf;
            return Read(rf, ri);
        }

        // GameObject.m_Component: the PPtr<Component> list.
        public IEnumerable<(AssetsFileInstance f, AssetFileInfo info)> Components(AssetsFileInstance f, AssetFileInfo go)
        {
            if (go.TypeId != (int)AssetClassID.GameObject) yield break;
            foreach (var c in GameObject.MakeValue(At(f, go))["m_Component"].Children)
                if (Resolve(f, c["component"]) is var (cf, ci)) yield return (cf, ci);
        }

        // GetComponentsInChildren(includeInactive: false) order: the object's components, then each active child in m_Children order.
        public void ActiveHierarchy(AssetsFileInstance f, AssetFileInfo go, Action<AssetsFileInstance, AssetFileInfo> component)
        {
            if (!GameObject.MakeValue(At(f, go))["m_IsActive"].AsBool) return;
            foreach (var (cf, ci) in Components(f, go))
            {
                component(cf, ci);
                if (ci.TypeId is not ((int)AssetClassID.Transform or (int)AssetClassID.RectTransform)) continue;
                foreach (var child in Transform.MakeValue(At(cf, ci))["m_Children"].Children)
                    if (Resolve(cf, child) is var (tf, ti) && Resolve(tf, Transform.MakeValue(At(tf, ti))["m_GameObject"]) is var (cgf, cgi))
                        ActiveHierarchy(cgf, cgi, component);
            }
        }

        // Unity 2022.3 layouts, up to the fields used here.
        private static readonly AssetTypeTemplateField GameObject = Class("GameObject", "GameObject", new()
        {
            Array("m_Component", Class("data", "ComponentPair", new() { PPtr("component") })), Prim("m_Layer", "unsigned int", AssetValueType.UInt32, false),
            Prim("m_Name", "string", AssetValueType.String, true), Prim("m_Tag", "UInt16", AssetValueType.UInt16, false), Prim("m_IsActive", "bool", AssetValueType.Bool, true),
        });
        private static readonly AssetTypeTemplateField Transform = Class("Transform", "Transform", new()
        {
            PPtr("m_GameObject"), Floats("m_LocalRotation", "x", "y", "z", "w"), Floats("m_LocalPosition", "x", "y", "z"), Floats("m_LocalScale", "x", "y", "z"),
            Array("m_Children", PPtr("data")),
        });

        public IEnumerable<AssetFileInfo> Scripts(AssetsFileInstance f, string cls) =>
            f.file.GetAssetsOfType(AssetClassID.MonoBehaviour).Where(i => Script(f, i).cls == cls);
    }

    // ---------- extraction ----------

    // Every maze scene in the build: mazeType → its Config and cosmetic Pool. Scenes that fail are reported through log and skipped.
    public static Dictionary<int, Result> ReadAll(string dataDir, ILayouts layouts, Func<int, bool> wanted, Action<string> log)
    {
        var result = new Dictionary<int, Result>();
        using var rd = new Reader(dataDir, layouts);
        var scenes = SceneNames(rd);
        var maze = MazeScenes(rd);
        if (maze.Count == 0) { log("MazeTypesConfigSO not found"); return result; }
        Dictionary<string, string> bundleScenes = null; // Addressables key → scene file inside a bundle, read only when needed
        foreach (var (type, scene) in maze)
        {
            if (!wanted(type)) continue;
            // Build scenes are matched like SceneManager.LoadScene (case-insensitive). The DLC mazes are Addressables scenes
            // (sceneName is the Addressables key) and are read only when their bundles are installed.
            int level = scenes.FindIndex(s => string.Equals(Path.GetFileNameWithoutExtension(s), scene, StringComparison.OrdinalIgnoreCase));
            string file = level >= 0 ? $"level{level}" : null;
            if (file == null)
            {
                bundleScenes ??= BundleScenes(rd, Path.Combine(dataDir, "StreamingAssets", "aa"));
                if (!bundleScenes.TryGetValue(scene, out file)) continue;
            }
            AssetsFileInstance f = null;
            try
            {
                f = rd.File(file);
                var r = ReadScene(rd, f, type);
                if (r == null) log($"maze type {type}: {file} ({scene}) has no MazeGenerator");
                else result[type] = r;
            }
            catch (Exception ex) { log($"maze type {type}: {file} ({scene}) failed: {ex}"); }
            finally { rd.Unload(f); } // level files are large; the shared asset files stay loaded for the next scene
        }
        return result;
    }

    // BuildSettings.m_Scenes (first field) gives the levelN index of each scene path.
    private static List<string> SceneNames(Reader rd)
    {
        var gg = rd.File("globalgamemanagers");
        var bs = gg.file.GetAssetsOfType(AssetClassID.BuildSettings).First();
        var v = Array("m_Scenes", Prim("data", "string", AssetValueType.String, true)).MakeValue(gg.file.Reader, bs.GetAbsoluteByteOffset(gg.file));
        return v.Children.Select(c => c.AsString).ToList();
    }

    // MazeTypesConfigSO.mazeTypesData is indexed by MazeType and names each maze type's scene.
    private static List<(int type, string scene)> MazeScenes(Reader rd)
    {
        foreach (var name in new[] { "resources.assets", "globalgamemanagers.assets" }.Concat(Enumerable.Range(0, 100).Select(i => $"sharedassets{i}.assets")))
        {
            var f = rd.File(name);
            if (f == null) continue;
            var info = rd.Scripts(f, "MazeTypesConfigSO").FirstOrDefault();
            if (info == null) continue;
            var data = rd.Read(f, info)["mazeTypesData"].Children;
            return data.Select((d, i) => (i, d["sceneName"].AsString)).Where(x => !string.IsNullOrEmpty(x.Item2)).ToList();
        }
        return new();
    }

    // Addressables key → serialized scene file inside an installed bundle: catalog.json maps the key to the scene path,
    // and the scene bundle's AssetBundle.m_SceneHashes maps that path to its CAB file.
    private static Dictionary<string, string> BundleScenes(Reader rd, string aaDir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var catalog = Path.Combine(aaDir, "catalog.json");
        if (!System.IO.File.Exists(catalog)) return result;
        var cabs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in rd.AddBundles(aaDir))
        {
            var f = rd.File(name);
            foreach (var info in f.file.GetAssetsOfType(AssetClassID.AssetBundle))
                foreach (var pair in AssetBundle.MakeValue(f.file.Reader, info.GetAbsoluteByteOffset(f.file))["m_SceneHashes"].Children)
                    cabs[pair["first"].AsString] = pair["second"].AsString;
        }
        foreach (var (key, path) in CatalogScenes(catalog))
            if (cabs.TryGetValue(path, out var cab)) result[key] = cab;
        return result;
    }

    // AssetBundle (Unity 2022.3) up to m_SceneHashes.
    private static AssetTypeTemplateField AssetInfo(string name) => Class(name, "AssetInfo", new()
        { Prim("preloadIndex", "int", AssetValueType.Int32, false), Prim("preloadSize", "int", AssetValueType.Int32, false), PPtr("asset") });
    private static AssetTypeTemplateField StringPair(AssetTypeTemplateField second) => Class("data", "pair", new() { Prim("first", "string", AssetValueType.String, true), second });
    private static readonly AssetTypeTemplateField AssetBundle = Class("AssetBundle", "AssetBundle", new()
    {
        Prim("m_Name", "string", AssetValueType.String, true), Array("m_PreloadTable", PPtr("data")), Array("m_Container", StringPair(AssetInfo("second"))),
        AssetInfo("m_MainAsset"), Prim("m_RuntimeCompatibility", "unsigned int", AssetValueType.UInt32, false), Prim("m_AssetBundleName", "string", AssetValueType.String, true),
        Array("m_Dependencies", Prim("data", "string", AssetValueType.String, true)), Prim("m_IsStreamedSceneAssetBundle", "bool", AssetValueType.Bool, true),
        Prim("m_ExplicitDataLayout", "int", AssetValueType.Int32, false), Prim("m_PathFlags", "int", AssetValueType.Int32, false),
        Array("m_SceneHashes", StringPair(Prim("second", "string", AssetValueType.String, true))),
    });

    // Addressables catalog: bucket i lists the entries of key i; an entry's first int is its m_InternalIds index.
    private static List<(string key, string scenePath)> CatalogScenes(string catalogPath)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(catalogPath));
        var root = doc.RootElement;
        var prefixes = root.TryGetProperty("m_InternalIdPrefixes", out var px) ? px.EnumerateArray().Select(x => x.GetString()).ToArray() : new string[0];
        // Ids are "<prefix index>#<rest>" when the catalog shares id prefixes.
        string Id(string id)
        {
            int h = id.IndexOf('#');
            return prefixes.Length > 0 && h > 0 && int.TryParse(id[..h], out int p) && p < prefixes.Length ? prefixes[p] + id[(h + 1)..] : id;
        }
        var ids = root.GetProperty("m_InternalIds").EnumerateArray().Select(x => Id(x.GetString())).ToArray();
        byte[] B64(string n) => Convert.FromBase64String(root.GetProperty(n).GetString());
        byte[] keys = B64("m_KeyDataString"), buckets = B64("m_BucketDataString"), entries = B64("m_EntryDataString");
        var list = new List<(string, string)>();
        for (int i = 0, n = BitConverter.ToInt32(buckets, 0), pos = 4; i < n; i++)
        {
            int off = BitConverter.ToInt32(buckets, pos), count = BitConverter.ToInt32(buckets, pos + 4);
            pos += 8;
            // Key object type 0 = ASCII string, 1 = UTF-16 string; the others (hashes, ints) are never scene keys.
            string key = keys[off] switch
            {
                0 => System.Text.Encoding.ASCII.GetString(keys, off + 5, BitConverter.ToInt32(keys, off + 1)),
                1 => System.Text.Encoding.Unicode.GetString(keys, off + 5, BitConverter.ToInt32(keys, off + 1)),
                _ => null,
            };
            for (int e = 0; e < count; e++, pos += 4)
            {
                string id = ids[BitConverter.ToInt32(entries, 4 + BitConverter.ToInt32(buckets, pos) * 28)];
                if (key != null && id.EndsWith(".unity")) list.Add((key, id));
            }
        }
        return list;
    }

    internal static Result ReadScene(Reader rd, AssetsFileInstance f, int mazeType)
    {
        var gInfo = rd.Scripts(f, "MazeGenerator").FirstOrDefault();
        if (gInfo == null) return null;
        var g = rd.Read(f, gInfo);
        var prefabIds = new Dictionary<(string, long), int>();

        // MazeStructure → Structure like MazeStructuresTest.ToStructure; GetInstanceID only serves as an identity, so a per-scene counter stands in.
        MazeStructures.Structure Structure(AssetsFileInstance from, AssetTypeValueField pptr)
        {
            var m = rd.Deref(from, pptr, out var mf);
            if (m == null) return null;
            int prefabId = 0;
            if (rd.Resolve(mf, m["Prefab"]) is var (pf, pi))
                if (!prefabIds.TryGetValue((pf.name, pi.PathId), out prefabId)) prefabIds[(pf.name, pi.PathId)] = prefabId = prefabIds.Count + 1;
            return new MazeStructures.Structure
            {
                DimX = m["Dimensions"]["m_X"].AsInt, DimY = m["Dimensions"]["m_Y"].AsInt, SubmazeMask = m["supportedSubmazes"].AsInt, PrefabId = prefabId,
                Border = m["Border"].Children.Select(c => c.AsInt).ToArray(),
                NonWalkable = m["nonWalkableOverrides"].Children.Select(c => (c["m_X"].AsInt, c["m_Y"].AsInt)).ToArray(),
                Objectives = m["supportedObjectives"].Children.Select(c => rd.Deref(mf, c, out _)).Where(o => o != null).Select(o => o["m_Name"].AsString).ToArray(),
            };
        }
        MazeStructures.Structure[] Structures(AssetsFileInstance from, AssetTypeValueField arr) => arr.Children.Select(c => Structure(from, c)).ToArray();

        var sg = g["subGenerationSettings"];
        var cfg = new MazePipeline.Config
        {
            MazeType = mazeType,
            SubmazeTries = g["submazeTries"].AsInt, StructureAttempts = g["structureAttempts"].AsInt, EdgeDist = g["landmarkDistanceFromEdge"].AsInt,
            MinRequired = g["minRequiredLandmarksSpawned"].AsInt, MinDistStartEnd = g["minDistanceFromStartToEndPiece"].AsInt, MinDistEdge = g["minDistanceFromEdgeToStartEndPiece"].AsInt,
            CanChangeDist = true, // not serialized; the MazeGenerator constructor sets it
            RequireAtLeastOneLandmark = g["requireAtLeastOneLandmark"].AsBool,
            SubChance = sg["chance"].AsFloat, SubRMin = sg["radiusMin"].AsInt, SubRMax = sg["radiusMax"].AsInt, SubPct = sg["radiusMaxMapPercent"].AsFloat,
            Subs = g["possibleSubmazes"].Children.Select(x => new MazePipeline.Submaze
            {
                RadiusMin = x["submazeRadiusMin"].AsInt, RadiusMax = x["submazeRadiusMax"].AsInt, SpawnTries = x["spawnTries"].AsInt,
                Chance = x["chanceToSpawn"].AsFloat, CanPlaceSafezones = x["canPlaceSafezones"].AsBool,
            }).ToArray(),
            SafeCells = g["safezoneCells"].AsInt, SafeMinSpawned = g["minSafezonesSpawned"].AsInt, SafeMinTried = g["minSafezonesTriedToSpawn"].AsInt,
            SafeDistStart = g["safezoneMinDistanceToStart"].AsInt, SafeDistLandmark = g["safezoneMinDistanceToLandmark"].AsInt, SafeDistOthers = g["safezoneMinDistanceToOthers"].AsInt,
            GridSize = g["SafezoneGrid"]["Size"].AsInt, GridSpacing = g["SafezoneGrid"]["Spacing"].AsInt, MoveSafezonesForward = g["moveSafezonesForward"].AsBool,
            Required = Structures(f, g["requiredLandmarks"]), Puzzles = Structures(f, g["puzzleStructures"]), Landmarks = Structures(f, g["landmarkStructures"]),
            Tile = g["tileSize"].AsInt, // Awake: TILE_SIZE = tileSize
        };

        var om = rd.Scripts(f, "RndObjectiveManager").Select(i => rd.Read(f, i)).FirstOrDefault();
        if (om != null)
        {
            var sets = om["objectiveSets"].Children.Select(s => (s, objectives: s["objectives"].Children.Select(o => rd.Deref(f, o["objective"], out var of) is { } v ? (v, of) : default)
                .Where(x => x.v != null).ToList())).ToList();
            cfg.Sets = sets.Select(x => new MazePipeline.ObjectiveSet { MinMapSize = x.s["minRequiredMapSize"].AsInt, Objectives = x.objectives.Select(o => o.v["m_Name"].AsString).ToArray() }).ToArray();
            // CaseObjectiveWithLandmarkSO.GetRequiredLandmarks: requiredLandmarks[mazeType], or the first entry when out of range.
            foreach (var (v, of) in sets.SelectMany(x => x.objectives))
            {
                string name = v["m_Name"].AsString;
                var req = v["requiredLandmarks"];
                if (req.IsDummy || cfg.LandmarkObjectives.ContainsKey(name)) continue;
                var arr = req.Children;
                var entry = mazeType < arr.Count ? arr[mazeType] : arr.FirstOrDefault();
                int min = v["minLandmarksToSpawn"].AsInt;
                cfg.LandmarkObjectives[name] = new MazePipeline.LandmarkObjective
                {
                    Min = min > 0 ? Math.Min(min, arr.Count) : arr.Count, // MinLandmarksToSpawn getter
                    Landmarks = entry == null ? new MazeStructures.Structure[0] : Structures(of, entry["structures"]),
                };
            }
        }

        // MazeGenerator.Start: aiWaypointsGenerators = GetComponentsInChildren<AIWaypointsGenerator>() (active objects, hierarchy order).
        var generators = new List<AssetTypeValueField>();
        if (rd.Resolve(f, g["m_GameObject"]) is var (gof, goi)) rd.ActiveHierarchy(gof, goi, (cf, ci) => { if (rd.Script(cf, ci).cls == "AIWaypointsGenerator") generators.Add(rd.Read(cf, ci)); });
        cfg.Waypoints = generators.Where(x => x != null).Select(x => new MazeWaypoints.Generator
        {
            CheckRange = x["waypointDistanceCheckRange"].AsInt, PlaceMinDistSafezone = x["wapointPlacementMinDistanceFromSafezone"].AsInt, ValidTile = x["validTileType"].AsInt,
            SizeMin = x["waypointsMapSizeMin"].AsInt, SizeMax = x["waypointsMapSizeMax"].AsInt, CountMin = x["waypointsCountForMinSize"].AsInt, CountMax = x["waypointsCountForMaxSize"].AsInt,
            FromStart = x["waypointDistanceFromStart"].AsFloat, FromSafezone = x["waypointDistanceFromSafezone"].AsFloat, FromPuzzle = x["waypointDistanceFromPuzzle"].AsFloat,
            Mult = x["waypointsMapSizeMultiplier"].AsFloat,
        }).ToArray();

        // AIManager.Awake writes its maxWaypointDistance into the BehaviorDesigner global when > 0; otherwise the global keeps the asset value.
        // shortcut: a scene without an override inherits whatever the previous maze set at runtime; the asset value is assumed (all build scenes override).
        float own = new[] { "AIManager", "RndAIManager", "RndAIManagerMazeQ" }.SelectMany(c => rd.Scripts(f, c)).Select(i => rd.Read(f, i)?["maxWaypointDistance"]).FirstOrDefault(v => v != null && !v.IsDummy)?.AsFloat ?? 0;
        cfg.MaxWaypointsDistance = own > 0 ? own : GlobalFloat(rd, "max_waypoints_distance");

        return new Result { Maze = cfg, Pool = ReadPool(rd, f) };
    }

    // BehaviorDesigner GlobalVariables (Resources) keeps its variables as JSON: {"Variables":[{"Name":..,"SinglemValue":..}]}.
    private static float GlobalFloat(Reader rd, string name)
    {
        var f = rd.File("resources.assets");
        var info = f == null ? null : rd.Scripts(f, "GlobalVariables").FirstOrDefault();
        var json = info == null ? null : rd.Read(f, info)?["mVariableData"]["JSONSerialization"].AsString;
        if (string.IsNullOrEmpty(json)) return float.NaN;
        foreach (var v in System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("Variables").EnumerateArray())
            if (v.TryGetProperty("Name", out var n) && n.GetString() == name && v.TryGetProperty("SinglemValue", out var x)) return x.GetSingle();
        return float.NaN;
    }

    // RndCustomizationSpawners in the given files (default: the scene and all its dependency files) with the pool each would give.
    internal static List<(string file, long pathId, ContractPredictor.Pool pool)> PoolCandidates(Reader rd, AssetsFileInstance f, IEnumerable<string> fileNames = null)
    {
        var mgr = rd.Scripts(f, "RndCustomizationItemsManager").Select(i => rd.Read(f, i)).FirstOrDefault();
        var result = new List<(string, long, ContractPredictor.Pool)>();
        if (mgr == null) return result;
        fileNames ??= new[] { f.name }.Concat(f.file.Metadata.Externals.Select(e => Path.GetFileName(e.PathName)));
        foreach (var sf in fileNames.Select(n => n == f.name ? f : rd.File(n)).Where(x => x != null))
            foreach (var si in rd.Scripts(sf, "RndCustomizationSpawner"))
                if (rd.Read(sf, si) is { } sp) result.Add((sf.name, si.PathId, Pool(rd, f, mgr, sf, sp)));
        return result;
    }

    private static ContractPredictor.Pool Pool(Reader rd, AssetsFileInstance mf, AssetTypeValueField mgr, AssetsFileInstance sf, AssetTypeValueField spawner)
    {
        // WeightedPrefab → Entry: ItemID of the prefab's CustomizationPickup, -1 without one.
        ContractPredictor.Entry Entry(AssetsFileInstance from, AssetTypeValueField wp)
        {
            int id = -1;
            if (rd.Resolve(from, wp["Value"]) is var (gf, gi))
                foreach (var (cf, ci) in rd.Components(gf, gi))
                    if (rd.Script(cf, ci).cls == "CustomizationPickup" && rd.Read(cf, ci) is { } c) { id = c["itemID"].AsInt; break; }
            return new ContractPredictor.Entry(id, wp["weight"].AsFloat);
        }
        ContractPredictor.Entry[] Entries(AssetsFileInstance from, AssetTypeValueField arr) => arr.IsDummy ? new ContractPredictor.Entry[0] : arr.Children.Select(c => Entry(from, c)).ToArray();
        ContractPredictor.Entry[] Global(string field) => rd.Deref(sf, spawner[field], out var gf) is { } so ? Entries(gf, so["value"]) : new ContractPredictor.Entry[0];

        return new ContractPredictor.Pool(
            mgr["chanceForItem"].Children.Select(c => c.AsFloat).ToArray(),
            Global("globalPrefabs").Concat(Entries(sf, spawner["possiblePrefabs"])).ToArray(),
            Global("globalPrefabsHardcore").Concat(Entries(sf, spawner["possiblePrefabsHardcore"])).ToArray(),
            mgr["seasonalItems"].Children.Select(c => new ContractPredictor.Seasonal(c["seasonalEventType"].AsInt, c["chance"].AsFloat, Entries(mf, c["prefabs"]))).ToArray());
    }

    // The spawners live in prefabs the maze instantiates. The ones in the scene's own sharedassets file match the in-game capture
    // (the other dependency files hold prefabs of other mazes). Maze_I has two pools there; the majority stands in for spawners[0].
    private static ContractPredictor.Pool ReadPool(Reader rd, AssetsFileInstance f)
    {
        // levelN → sharedassetsN.assets; a bundle scene CAB-x → CAB-x.sharedAssets.
        var own = PoolCandidates(rd, f, new[] { f.name, f.name.Replace("level", "sharedassets") + ".assets", f.name + ".sharedAssets" });
        return (own.Count > 0 ? own : PoolCandidates(rd, f)).GroupBy(c => string.Join("\n", ContractPredictor.Lines(c.pool)))
            .OrderByDescending(g => g.Count()).Select(g => g.First().pool).FirstOrDefault();
    }
}
