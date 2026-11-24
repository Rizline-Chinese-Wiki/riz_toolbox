using RizToolbox.Runtime;
using System.Buffers.Binary;
using System.IO.Compression;

namespace RizToolbox.Adapter;

/// <summary>Bounded PNG subset for test/UI icons; RGB/RGBA 8-bit, noninterlaced, max 64x64. Other formats fail explicitly.</summary>
internal static class PngDecoder
{
    internal sealed record Image(int Width,int Height,Rgba[] Pixels);
    internal static Image Decode(byte[] encoded) => Decode(encoded,64,64*64,64*64*16,1024*1024);
    /// <summary>Explicit caller budget; the legacy icon overload retains its 64-pixel limit.</summary>
    internal static Image Decode(byte[] encoded,int maximumSide,int maximumPixels,long maximumDecodedBytes,int maximumEncodedBytes) {
        if(maximumSide<1||maximumSide>4096||maximumPixels<1||maximumPixels>16*1024*1024||maximumDecodedBytes<16||maximumDecodedBytes>256L*1024*1024||maximumEncodedBytes<33||maximumEncodedBytes>32*1024*1024)throw new ArgumentOutOfRangeException(nameof(maximumSide),"PNG budgets exceed the decoder hard ceiling.");
        if(encoded is null||encoded.Length>maximumEncodedBytes||encoded.Length<33||!encoded.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new InvalidDataException("Expected a PNG within the encoded file budget.");
        var width=0;var height=0;var channels=0;var header=false;var end=false;var pixels=false;var dataClosed=false;
        using var compressed=new MemoryStream();
        for(var p=8;p+12<=encoded.Length;) {
            var length=BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(p,4));
            if(length<0||length>encoded.Length-p-12)throw new InvalidDataException("Invalid PNG chunk length.");
            var type=System.Text.Encoding.ASCII.GetString(encoded,p+4,4);var data=encoded.AsSpan(p+8,length);
            if(Crc32(encoded.AsSpan(p+4,length+4))!=BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(p+8+length,4)))throw new InvalidDataException("PNG checksum mismatch.");
            if(type.Any(c=>c is not(>='A' and <='Z')and not(>='a' and <='z'))||!header&&type!="IHDR")throw new InvalidDataException("Invalid PNG chunk order or type.");
            if(pixels&&type!="IDAT")dataClosed=true;
            if(type=="IHDR") {
                if(header||length!=13)throw new InvalidDataException("Invalid PNG header.");header=true;
                width=BinaryPrimitives.ReadInt32BigEndian(data);height=BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                if(width<1||width>maximumSide||height<1||height>maximumSide||(long)width*height>maximumPixels||(long)width*height*16>maximumDecodedBytes||data[8]!=8||data[9] is not(2 or 6)||data[10]!=0||data[11]!=0||data[12]!=0)throw new InvalidDataException("PNG requires RGB/RGBA8, noninterlaced pixels within the declared side, pixel and decoded-memory budgets.");
                channels=data[9]==6?4:3;
            } else if(type=="IDAT") {if(dataClosed)throw new InvalidDataException("PNG IDAT chunks must be consecutive.");pixels=true;compressed.Write(data);}
            else if(type=="IEND"){if(length!=0||!pixels||p+12!=encoded.Length)throw new InvalidDataException("Invalid PNG end chunk or trailing bytes.");end=true;break;}
            else if(type=="tRNS"||char.IsUpper(type[0]))throw new InvalidDataException("Unsupported PNG chunk: "+type);
            p+=length+12;
        }
        if(!header||!end)throw new InvalidDataException("Incomplete PNG.");
        compressed.Position=0;using var z=new ZLibStream(compressed,CompressionMode.Decompress);
        var stride=width*channels;var previous=new byte[stride];var current=new byte[stride];var colors=new Rgba[width*height];
        for(var y=0;y<height;y++) {
            var filter=z.ReadByte();if(filter is <0 or >4)throw new InvalidDataException("Invalid PNG filter.");
            var n=0;while(n<stride){var read=z.Read(current,n,stride-n);if(read==0)throw new InvalidDataException("Truncated PNG pixels.");n+=read;}
            for(var x=0;x<stride;x++) {
                var a=x>=channels?current[x-channels]:0;var b=previous[x];var c=x>=channels?previous[x-channels]:0;
                current[x]=unchecked((byte)(current[x]+(filter switch{0=>0,1=>a,2=>b,3=>(a+b)/2,4=>Paeth(a,b,c),_=>0})));
            }
            for(var x=0;x<width;x++){var p=x*channels;colors[y*width+x]=new(current[p]/255f,current[p+1]/255f,current[p+2]/255f,channels==4?current[p+3]/255f:1);}
            (previous,current)=(current,previous);
        }
        if(z.ReadByte()!=-1)throw new InvalidDataException("Unexpected PNG pixel data length.");
        return new(width,height,colors);
    }
    private static int Paeth(int a,int b,int c){var p=a+b-c;var pa=Math.Abs(p-a);var pb=Math.Abs(p-b);var pc=Math.Abs(p-c);return pa<=pb&&pa<=pc?a:pb<=pc?b:c;}
    private static uint Crc32(ReadOnlySpan<byte> bytes){var crc=uint.MaxValue;foreach(var b in bytes){crc^=b;for(var i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}return ~crc;}
}
