using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Gameplay;

// Implemented by the same retained LineRingReader that owns the original
// LineControl and all source roots. These observations describe the physical
// scratch array, including its unused tail; they are not live authorizations.
internal interface IRetainedSeekLineBufferAccess
{
    NativeRootedVector3Buffer ReadOriginalLinePositions();
    NativeVector3BufferWriteObservation WriteOriginalLinePositionsPrefix(
        NativeRootedVector3Buffer expected, IReadOnlyList<NativeVector3BufferBits> prefix,
        Action beforeStore);
}
