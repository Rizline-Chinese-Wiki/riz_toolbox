using System.Runtime.InteropServices;

internal static partial class NativePlaybackStockMetadataChecks
{
    private sealed partial class MetadataFixture
    {
        internal IntPtr ResourceType, ResourceClass, ResourceClassType, ResourceField, ResourceFieldType, ResourceFieldParent, ResourceImage;
        internal int ResourceFieldOffset = 0x30;
        internal uint ResourceFieldFlags, ProgressInstanceBytes = 0x70;
        internal bool ResourceClassGeneric, ResourceClassInflated = true, ResourceFieldMissing, ResourceFieldDuplicated;
        internal readonly Dictionary<IntPtr, IntPtr> ResourceTypeClasses = new();
        private IntPtr _resourceProgress, _resourceModule, _resourceFieldName;

        private void ConfigureProgressResourceMetadata(IntPtr module, IntPtr progress)
        {
            _resourceProgress = progress;
            _resourceModule = module;
            ResourceType = Base.AddType("IResourceReference<UnityEngine.AudioClip>", 21, false, IntPtr.Size);
            ResourceClass = Base.ClassOf(ResourceType);
            ResourceClassType = ResourceType;
            _classes.Add((_gameImage, "", "IResourceReference`1"), ResourceClass);
            _classNames.Add(ResourceClass, (String("IResourceReference`1"), String("")));
            ResourceImage = _gameImage;
            ResourceField = Allocate(16); ResourceFieldType = ResourceType; ResourceFieldParent = progress;
            _resourceFieldName = String("<Clip>k__BackingField");
            Add("ProgressClip", progress, "set_Clip", 0x4016f0, module, ResourceType);
            Export("il2cpp_class_get_type", "Ptr1", nameof(DeclaredClassType));
            Export("il2cpp_class_from_type", "Ptr1", nameof(DeclaredTypeClass));
            Export("il2cpp_class_get_image", "Ptr1", nameof(DeclaredClassImage));
            Export("il2cpp_class_is_generic", "Predicate", nameof(DeclaredClassGeneric));
            Export("il2cpp_class_is_inflated", "Predicate", nameof(DeclaredClassInflated));
            Export("il2cpp_class_instance_size", "ArraySize", nameof(DeclaredClassInstanceSize));
            Export("il2cpp_class_get_fields", "Ptr2", nameof(DeclaredFields));
            Export("il2cpp_class_get_field_from_name", "FieldLookup", nameof(DeclaredField));
            Export("il2cpp_field_get_parent", "Ptr1", nameof(DeclaredFieldParent));
            Export("il2cpp_field_get_name", "Ptr1", nameof(DeclaredFieldName));
            Export("il2cpp_field_get_type", "Ptr1", nameof(DeclaredFieldType));
            Export("il2cpp_field_get_offset", "FieldOffset", nameof(DeclaredFieldOffset));
            Export("il2cpp_field_get_flags", "Flags", nameof(DeclaredFieldFlags));
        }
        internal void ResourceClassName(string name, string ns = "")
            => _classNames[ResourceClass] = (String(name), String(ns));
        internal void ResourceFieldName(string name) => _resourceFieldName = String(name);
        internal void ReplaceResourceField() => ResourceField = Allocate(16);
        internal void DuplicateResourceSetter()
            => Add("ProgressClipDuplicate", _resourceProgress, "set_Clip", 0x4016f0, _resourceModule, ResourceType);
        internal IntPtr ResourceTypeAlias()
        {
            var type = Base.AddType("IResourceReference<UnityEngine.AudioClip>", 21, false, IntPtr.Size);
            ResourceTypeClasses.Add(type, ResourceClass); return type;
        }
        private IntPtr DeclaredClassType(IntPtr cls)
        { Reads++; return cls == ResourceClass ? ResourceClassType : Types.Keys.Single(type => Base.ClassOf(type) == cls); }
        private IntPtr DeclaredTypeClass(IntPtr type)
        { Reads++; return ResourceTypeClasses.TryGetValue(type, out var cls) ? cls : Base.ClassOf(type); }
        private IntPtr DeclaredClassImage(IntPtr cls)
        {
            Reads++;
            return cls == ResourceClass ? ResourceImage : _classes.FirstOrDefault(row => row.Value == cls).Key.Image;
        }
        private bool DeclaredClassGeneric(IntPtr cls) { Reads++; return cls == ResourceClass && ResourceClassGeneric; }
        private bool DeclaredClassInflated(IntPtr cls) { Reads++; return cls == ResourceClass && ResourceClassInflated; }
        private uint DeclaredClassInstanceSize(IntPtr cls)
        { Reads++; return cls == _resourceProgress ? ProgressInstanceBytes : cls == _startClass ? 0x28u : 0x40u; }
        private IntPtr DeclaredFields(IntPtr cls, IntPtr iterator)
        {
            Reads++;
            var fields = cls == _resourceProgress
                ? ResourceFieldMissing ? Array.Empty<IntPtr>() : ResourceFieldDuplicated ? new[] { ResourceField, ResourceField } : new[] { ResourceField }
                : cls == _startClass ? _startFields.Keys.ToArray() : Array.Empty<IntPtr>();
            var index = Marshal.ReadIntPtr(iterator).ToInt32();
            if (index >= fields.Length) return IntPtr.Zero;
            Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return fields[index];
        }
        private IntPtr DeclaredField(IntPtr cls, string name)
        {
            if (cls == _resourceProgress)
            { Reads++; return !ResourceFieldMissing && name == "<Clip>k__BackingField" ? ResourceField : IntPtr.Zero; }
            return StartField(cls, name);
        }
        private IntPtr DeclaredFieldParent(IntPtr field)
        { Reads++; return field == ResourceField ? ResourceFieldParent : _startFields.ContainsKey(field) ? _startClass : IntPtr.Zero; }
        private IntPtr DeclaredFieldName(IntPtr field)
        { Reads++; return field == ResourceField ? _resourceFieldName : String(_startFieldNames.Single(row => row.Value == field).Key); }
        private IntPtr DeclaredFieldType(IntPtr field)
        {
            if (field != ResourceField) return StartFieldType(field);
            Reads++;
            return _nativeTypeOriginals.Count == 0 ? ResourceFieldType : _nativeClassTypes[Base.ClassOf(ResourceFieldType)];
        }
        private int DeclaredFieldOffset(IntPtr field)
        { if (field != ResourceField) return StartFieldOffset(field); Reads++; return ResourceFieldOffset; }
        private uint DeclaredFieldFlags(IntPtr field)
        { if (field != ResourceField) return StartFieldFlags(field); Reads++; return ResourceFieldFlags; }
    }
}
