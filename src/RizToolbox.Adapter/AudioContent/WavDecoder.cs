using System.Buffers.Binary;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record PcmAudio(int SampleFrames,int Channels,int Frequency,float[] Samples)
{ internal long EstimatedBytes=>(long)Samples.Length*sizeof(float); }
internal static class WavDecoder
{
    internal const int MaximumDecodedBytes=32*1024*1024;
    internal static PcmAudio Decode(byte[] bytes,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(bytes is null||bytes.Length<44||bytes.Length>ContentFileAccess.MaximumFileBytes||!Tag(bytes,0,"RIFF")||!Tag(bytes,8,"WAVE")||U32(bytes,4)!=(uint)(bytes.Length-8))
            throw new InvalidDataException("Only bounded RIFF/WAVE files with exact declared size are supported.");
        int channels=0,frequency=0,align=0,dataAt=-1,dataLength=0;bool format=false;
        for(var at=12;at<bytes.Length;)
        {
            token.ThrowIfCancellationRequested();if(bytes.Length-at<8)throw new InvalidDataException("Truncated WAV chunk header.");
            var length=U32(bytes,at+4);if(length>int.MaxValue||length>bytes.Length-at-8)throw new InvalidDataException("WAV chunk exceeds its file.");
            var size=(int)length;var payload=at+8;
            if(Tag(bytes,at,"fmt "))
            {
                if(format||size!=16)throw new InvalidDataException("Exactly one 16-byte PCM format chunk is required.");
                var code=U16(bytes,payload);channels=U16(bytes,payload+2);var hz=U32(bytes,payload+4);var rate=U32(bytes,payload+8);align=U16(bytes,payload+12);var bits=U16(bytes,payload+14);
                if(code!=1||bits!=16||channels is <1 or >2||hz is <8000 or >192000||align!=channels*2||rate!=hz*align)throw new InvalidDataException("Only PCM16 mono/stereo WAV at 8..192 kHz is supported.");
                frequency=(int)hz;format=true;
            }
            else if(Tag(bytes,at,"data")){if(dataAt!=-1)throw new InvalidDataException("Multiple WAV data chunks are unsupported.");dataAt=payload;dataLength=size;}
            var next=(long)payload+size+(size&1);if(next>bytes.Length)throw new InvalidDataException("Missing WAV chunk padding.");at=(int)next;
        }
        if(!format||dataAt<0||dataLength<=0||dataLength%align!=0||(long)dataLength*2>MaximumDecodedBytes)throw new InvalidDataException("WAV has missing, unaligned or excessive sample data.");
        var samples=new float[dataLength/2];
        for(var i=0;i<samples.Length;i++){if((i&16383)==0)token.ThrowIfCancellationRequested();samples[i]=BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(dataAt+i*2,2))/32768f;}
        token.ThrowIfCancellationRequested();return new(dataLength/align,channels,frequency,samples);
    }
    private static bool Tag(byte[] b,int at,string s)=>b[at]==s[0]&&b[at+1]==s[1]&&b[at+2]==s[2]&&b[at+3]==s[3];
    private static ushort U16(byte[] b,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at,2));
    private static uint U32(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at,4));
}
