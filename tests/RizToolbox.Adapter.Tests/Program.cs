using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using RizToolbox.Adapter;

var checks=0;
void Check(bool valid,string message){if(!valid)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
void Reject(byte[] bytes,string message){try{PngDecoder.Decode(bytes);}catch(InvalidDataException){Check(true,message);return;}throw new Exception("Expected rejection: "+message);}
byte[] Build(int filter) {
    using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10});
    void Chunk(string type,byte[] data){
        var raw=Encoding.ASCII.GetBytes(type).Concat(data).ToArray();var length=new byte[4];BinaryPrimitives.WriteInt32BigEndian(length,data.Length);output.Write(length);output.Write(raw);
        var crc=uint.MaxValue;foreach(var b in raw){crc^=b;for(var i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}BinaryPrimitives.WriteUInt32BigEndian(length,~crc);output.Write(length);
    }
    var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,1);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),2);header[8]=8;header[9]=6;Chunk("IHDR",header);
    using var pixels=new MemoryStream();
    // Two identical RGBA rows; all filters have known independent first-pixel predictions.
    var row=new byte[]{255,64,160,255};
    for(var y=0;y<2;y++){pixels.WriteByte((byte)filter);for(var i=0;i<4;i++){
        var prediction=y==0?0:filter switch{0=>0,1=>0,2=>row[i],3=>row[i]/2,4=>row[i],_=>0};pixels.WriteByte(unchecked((byte)(row[i]-prediction)));
    }}
    using var packed=new MemoryStream();using(var z=new ZLibStream(packed,CompressionLevel.Optimal,true))z.Write(pixels.ToArray());
    Chunk("IDAT",packed.ToArray());Chunk("IEND",Array.Empty<byte>());return output.ToArray();
}
for(var filter=0;filter<=4;filter++){
    var image=PngDecoder.Decode(Build(filter));
    Check(image.Width==1&&image.Height==2&&image.Pixels.All(p=>Math.Abs(p.R-1)<.0001&&Math.Abs(p.G-64/255f)<.0001&&Math.Abs(p.B-160/255f)<.0001&&Math.Abs(p.Alpha-1)<.0001),$"PNG filter {filter} preserves known pixels");
}
var corrupt=Build(0);corrupt[30]^=1;Reject(corrupt,"corrupt PNG checksum rejected");
Reject(Build(0)[..^12],"incomplete PNG rejected");Reject(new byte[1_048_577],"oversized icon rejected");Reject(new byte[33],"non-PNG input rejected");
var example=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFUlEQVR4nGP877DgPwMDAwMTiABhACx3AuIpINvjAAAAAElFTkSuQmCC");
Check(PngDecoder.Decode(example).Pixels.Length==4,"example icon decodes with integrity validation");
Console.WriteLine($"{checks}/{checks} adapter checks passed.");


