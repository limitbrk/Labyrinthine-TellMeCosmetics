using Il2CppInterop.Runtime;
using System;
using System.Collections.Generic;

namespace TellMeCosmetics.Predict;

// Script field layouts for ScenePreload from il2cpp reflection; handles are boxed Il2CppClass pointers.
// Use from a thread attached to the il2cpp domain.
internal sealed class Il2CppLayouts : ScenePreload.LayoutSource
{
    private const int FieldStatic = 0x10, FieldInitOnly = 0x20, FieldLiteral = 0x40, FieldNotSerialized = 0x80, FieldAccessMask = 0x7, FieldPublic = 0x6;
    private const int TypeInterface = 0x20, TypeAbstract = 0x80, TypeSerializable = 0x2000;

    private readonly IntPtr unityObject = IL2CPP.GetIl2CppClass("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
    private readonly IntPtr serializeField = IL2CPP.GetIl2CppClass("UnityEngine.CoreModule.dll", "UnityEngine", "SerializeField");
    private readonly IntPtr serializeReference = IL2CPP.GetIl2CppClass("UnityEngine.CoreModule.dll", "UnityEngine", "SerializeReference");

    private static IntPtr K(object t) => (IntPtr)t;
    private static object Box(IntPtr p) => p == IntPtr.Zero ? null : p;
    private static object TypeClass(IntPtr il2cppType) => il2cppType == IntPtr.Zero ? null : Box(IL2CPP.il2cpp_class_from_il2cpp_type(il2cppType));

    // MonoScript stores "Assembly-CSharp"; il2cpp images are named "Assembly-CSharp.dll".
    protected override object FindClass(string assembly, string ns, string name) =>
        Box(IL2CPP.GetIl2CppClass(assembly.EndsWith(".dll") ? assembly : assembly + ".dll", ns, name));

    protected override string FullName(object t)
    {
        string ns = IL2CPP.il2cpp_class_get_namespace_(K(t)), name = IL2CPP.il2cpp_class_get_name_(K(t));
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    protected override object ArrayElement(object t)
    {
        var k = K(t);
        if (IL2CPP.il2cpp_class_get_rank(k) == 1) return Box(IL2CPP.il2cpp_class_get_element_class(k));
        if (FullName(t) != "System.Collections.Generic.List`1") return null;
        // List<T> keeps its items in T[] _items.
        foreach (var f in Fields(k))
            if (IL2CPP.il2cpp_field_get_name_(f) == "_items") return Box(IL2CPP.il2cpp_class_get_element_class(K(TypeClass(IL2CPP.il2cpp_field_get_type(f)))));
        return null;
    }

    protected override object EnumUnderlying(object t) => IL2CPP.il2cpp_class_is_enum(K(t)) ? TypeClass(IL2CPP.il2cpp_class_enum_basetype(K(t))) : null;

    protected override bool IsUnityObject(object t) => K(t) == unityObject || IL2CPP.il2cpp_class_is_subclass_of(K(t), unityObject, false);

    protected override bool IsSerializableClass(object t)
    {
        int flags = IL2CPP.il2cpp_class_get_flags(K(t));
        return (flags & TypeSerializable) != 0 && (flags & (TypeAbstract | TypeInterface)) == 0 && !FullName(t).StartsWith("System.");
    }

    protected override IEnumerable<FieldInfo> SerializedFields(object t)
    {
        var chain = new List<IntPtr>();
        for (var k = K(t); k != IntPtr.Zero; k = IL2CPP.il2cpp_class_get_parent(k))
        {
            if (FullName(k) is "UnityEngine.MonoBehaviour" or "UnityEngine.ScriptableObject" or "UnityEngine.Object" or "System.Object" or "System.ValueType") break;
            chain.Insert(0, k);
        }
        foreach (var k in chain)
            foreach (var f in Fields(k))
            {
                int flags = IL2CPP.il2cpp_field_get_flags(f);
                if ((flags & (FieldStatic | FieldInitOnly | FieldLiteral)) != 0) continue;
                bool reference = serializeReference != IntPtr.Zero && IL2CPP.il2cpp_field_has_attribute(f, serializeReference);
                bool serialized = reference || IL2CPP.il2cpp_field_has_attribute(f, serializeField)
                    || ((flags & FieldAccessMask) == FieldPublic && (flags & FieldNotSerialized) == 0);
                var type = TypeClass(IL2CPP.il2cpp_field_get_type(f));
                if (serialized && type != null) yield return new FieldInfo(IL2CPP.il2cpp_field_get_name_(f), type, reference);
            }
    }

    private static IEnumerable<IntPtr> Fields(IntPtr k)
    {
        var iter = IntPtr.Zero;
        for (IntPtr f; (f = IL2CPP.il2cpp_class_get_fields(k, ref iter)) != IntPtr.Zero;) yield return f;
    }
}
