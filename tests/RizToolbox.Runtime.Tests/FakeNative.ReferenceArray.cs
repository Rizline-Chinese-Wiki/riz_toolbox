using System.Runtime.InteropServices;

sealed partial class FakeNative
{
    private readonly IntPtr _arrayRank = new(31001), _arrayLength = new(31002), _arrayGet = new(31003);
    private readonly Dictionary<IntPtr, IntPtr[]> _referenceArrays = new();
    private readonly Dictionary<IntPtr, int> _referenceRanks = new();
    private IntPtr _referenceArrayType;
    internal readonly List<int> ArrayReadIndices = new();
    internal int? ArrayReportedLength, FailArrayIndex;
    internal int ArrayRankReads, ArrayLengthReads;
    internal bool AllArrayReadsRooted = true;

    internal IntPtr ReferenceArray(int length, int rank = 1)
    {
        if (_referenceArrayType == IntPtr.Zero) _referenceArrayType = AddType("System.Object[]", 29, false, IntPtr.Size);
        var array = Box(_referenceArrayType, new byte[IntPtr.Size * 2]);
        _referenceArrays[array] = Enumerable.Range(0, length).Select(i => new IntPtr(100000 + i)).ToArray();
        _referenceRanks[array] = rank;
        return array;
    }
    private bool IsArrayMethod(IntPtr method) => method == _arrayRank || method == _arrayLength || method == _arrayGet;
    private IntPtr MethodName(IntPtr method)
    {
        if (method == Method && NamedMethodAvailable) { var callName = Marshal.StringToHGlobalAnsi("Call"); _allocations.Add(callName); return callName; }
        if(method==_constructor) { var constructorName=Marshal.StringToHGlobalAnsi(".ctor");_allocations.Add(constructorName);return constructorName; }
        if (!IsArrayMethod(method)) return IntPtr.Zero;
        var name = Marshal.StringToHGlobalAnsi(method == _arrayRank ? "get_Rank" : method == _arrayLength ? "get_Length" : "GetValue");
        _allocations.Add(name); return name;
    }
    private IntPtr ClassMethods(IntPtr cls, IntPtr iterator)
    {
        if (NamedMethodAvailable && (cls == TypeClass(Receiver) || cls == TypeClass(String))) {
            if (Marshal.ReadIntPtr(iterator) != IntPtr.Zero) return IntPtr.Zero;
            Marshal.WriteIntPtr(iterator, new IntPtr(1)); return Method;
        }
        if(ConstructorAvailable && cls==ConstructorClass) {
            if(Marshal.ReadIntPtr(iterator)!=IntPtr.Zero)return IntPtr.Zero;
            Marshal.WriteIntPtr(iterator,new IntPtr(1));return _constructor;
        }
        if (_referenceArrayType == IntPtr.Zero || cls != TypeClass(_referenceArrayType)) return IntPtr.Zero;
        var index = Marshal.ReadIntPtr(iterator).ToInt32();
        if (index >= 3) return IntPtr.Zero;
        Marshal.WriteIntPtr(iterator, new IntPtr(index + 1));
        return index == 0 ? _arrayRank : index == 1 ? _arrayLength : _arrayGet;
    }
    private IntPtr InvokeReferenceArray(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        AllArrayReadsRooted &= _handles.Values.Contains(instance);
        if (method == _arrayRank) { ArrayRankReads++; return Box(Int32, BitConverter.GetBytes(_referenceRanks[instance])); }
        if (method == _arrayLength) { ArrayLengthReads++; return Box(Int32, BitConverter.GetBytes(ArrayReportedLength ?? _referenceArrays[instance].Length)); }
        var index = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments)); ArrayReadIndices.Add(index);
        if (index == FailArrayIndex) { Marshal.WriteIntPtr(exception, new IntPtr(99)); return IntPtr.Zero; }
        return _referenceArrays[instance][index];
    }
}
