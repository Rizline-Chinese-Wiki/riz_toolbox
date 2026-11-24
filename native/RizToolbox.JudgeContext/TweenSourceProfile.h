#pragma once
#include <cstdint>

// Fixed GameAssembly image; exact replay spans. No caller-supplied profile.
// Raw stores include their original write barrier as one uninterrupted unit.
namespace RizTweenSourceProfile {
inline constexpr std::uint32_t UpdateRva=0x398d60, UpdateEndRva=0x39a194, UpdateUnwindRva=0x1ce66ec;
inline constexpr std::uint32_t SiteCount=22;
struct Site { std::uint32_t id, beginRva, endRva, deadContinuationRva; const unsigned char* bytes; std::uint32_t size; };
// SHA256 ae7a091c46741a0fbc817dc70ad8b32e758794597540cf1f6418801bf06ea133
inline constexpr unsigned char Site0Bytes[]{ 0xe8, 0x43, 0xb3, 0x76, 0x00 };
// SHA256 a1f693ac13833ecd872325adc55f1754d3b7b51dfef5c9ea013c3124b280ed22
inline constexpr unsigned char Site1Bytes[]{ 0xe8, 0xa3, 0xab, 0x76, 0x00 };
// SHA256 5863143b344a90b84f32cfdf0212cc1f9c0cc2f928a720da651a49e1b9146ea0
inline constexpr unsigned char Site2Bytes[]{ 0x48, 0x89, 0x7c, 0xc3, 0x20, 0x48, 0x63, 0xc6, 0x48, 0x83, 0xc0, 0x04, 0x48, 0x8d, 0x0c, 0xc3, 0xe8, 0x90, 0xa9, 0xec, 0xff };
// SHA256 f0036671203a54021411cee74be0c40a0ade518eda5cb8b4af189252f32b2c47
inline constexpr unsigned char Site3Bytes[]{ 0x48, 0x89, 0x7c, 0xc3, 0x20, 0x48, 0x63, 0xc6, 0x48, 0x83, 0xc0, 0x04, 0x48, 0x8d, 0x0c, 0xc3, 0xe8, 0xb4, 0xa1, 0xec, 0xff };
// SHA256 af1dda583c38ce1118b3d8384831ffc6c4294301490603e4d2c1d0ae2a0b869c
inline constexpr unsigned char Site4Bytes[]{ 0x48, 0x89, 0x7c, 0xc3, 0x20, 0x48, 0x63, 0xc6, 0x48, 0x83, 0xc0, 0x04, 0x48, 0x8d, 0x0c, 0xc3, 0xe8, 0x6a, 0xa7, 0xec, 0xff };
// SHA256 f689d46f01af579291935bc56fbf3cfb79a3bb062562d3d7c48b33b26b5073c4
inline constexpr unsigned char Site5Bytes[]{ 0x4c, 0x89, 0x6c, 0xca, 0x20, 0x48, 0x83, 0xc2, 0x20, 0x48, 0x8d, 0x0c, 0xca, 0x33, 0xd2, 0xe8, 0x37, 0xa7, 0xec, 0xff };
// SHA256 cbadafd509b1966ecb37ab70f7cbe1546a1a0e3afd6542ab64543a481164d056
inline constexpr unsigned char Site6Bytes[]{ 0x4c, 0x89, 0x6c, 0xc1, 0x20, 0x49, 0x63, 0xc7, 0x48, 0x8d, 0x0c, 0xc1, 0x48, 0x83, 0xc1, 0x20, 0xe8, 0xe1, 0xa8, 0xec, 0xff };
// SHA256 0a0e7333882a1a96755877747bf161c47b2a48eb1eb880e963bbe39e148c2051
inline constexpr unsigned char Site7Bytes[]{ 0xe8, 0x04, 0x8f, 0x76, 0x00 };
// SHA256 9370f7c5de9bda059ba513deeb4d86e6e5a7c776a13b69256faba075604aa7cc
inline constexpr unsigned char Site8Bytes[]{ 0xe8, 0x1e, 0x8e, 0x76, 0x00 };
// SHA256 6a01b68e241e565c60694f5d9ae829b38c9ffe55364c6049c3e5ffd8ac99debe
inline constexpr unsigned char Site9Bytes[]{ 0xe8, 0xa3, 0xa2, 0x64, 0x00 };
// SHA256 7f85842c7409f33899a85c845746f0ab6bd4afe2fbe4031689f3bb1a09cb7d7a
inline constexpr unsigned char Site10Bytes[]{ 0xe8, 0x33, 0x2f, 0x64, 0x00 };
// SHA256 374a11449ab65684f38125f0e8b8ea8a82a3090abd7fc59bac1839709e6f401f
inline constexpr unsigned char Site11Bytes[]{ 0xe8, 0xd1, 0x2e, 0x64, 0x00 };
// SHA256 98b7391b989f4f9d0df47b15209b85f86da47e5fc6e4de5d51e2c3d361ba961d
inline constexpr unsigned char Site12Bytes[]{ 0xe8, 0x9f, 0x2e, 0x64, 0x00 };
// SHA256 8cf16ed9f1c9b7d7c55795c160b3550a3bf47842f15db69fddf431232166e3f1
inline constexpr unsigned char Site13Bytes[]{ 0x84, 0xc0, 0x75, 0x47, 0x48, 0x85, 0xdb, 0x0f, 0x84, 0x7b, 0x0a, 0x00, 0x00 };
// SHA256 b8d57feea95a612da50c148fdddf41c7e30aef314288b43bf6ea23515e1f6e2f
inline constexpr unsigned char Site14Bytes[]{ 0xe8, 0x61, 0xd8, 0xff, 0xff };
// SHA256 7335bca3a6868b68312f4ab87e9424c4d099ed22d8a8ef1f0ace9d7f277499d4
inline constexpr unsigned char Site15Bytes[]{ 0xe8, 0xb6, 0xd6, 0xff, 0xff };
// SHA256 8f2d6acb911eecd541c579602ef1c725dc56a2ccbc77f276fdfb6e85a0602c3f
inline constexpr unsigned char Site16Bytes[]{ 0xe8, 0x85, 0xd0, 0xff, 0xff };
// SHA256 e97b76a0e1018804ab59ee2923bcc391839eaebbc60d9fa3645f1bb8ca9d541e
inline constexpr unsigned char Site17Bytes[]{ 0xe8, 0x76, 0xad, 0x1c, 0x01 };
// SHA256 63c4cca9463d0079efea6a0ea94ea18f76fe4d1d029468e685c2c1a26467ec00
inline constexpr unsigned char Site18Bytes[]{ 0xe8, 0x9a, 0xa5, 0x1c, 0x01 };
// SHA256 38462920991a2ded32796ec6519e2b99b63a32d44e32ee44d303821fbe178d84
inline constexpr unsigned char Site19Bytes[]{ 0xe8, 0xf3, 0xea, 0x1c, 0x01 };
// SHA256 032834ae89643a1187af36f28521edee78f4ab36f02a39948557dd764c8ee0f2
inline constexpr unsigned char Site20Bytes[]{ 0xe8, 0x8b, 0xe5, 0x1c, 0x01 };
// SHA256 58900686bfcc40ff44d5c395330caba50d32d999b880b58fa408c18e8cb688d3
inline constexpr unsigned char Site21Bytes[]{ 0xe8, 0x19, 0xbe, 0x1c, 0x01 };
inline constexpr Site Sites[SiteCount]{
    { 0,0x398f18,0x398f1d,0x398f00,Site0Bytes,sizeof(Site0Bytes) },
    { 1,0x3996b8,0x3996bd,0x3996a0,Site1Bytes,sizeof(Site1Bytes) },
    { 2,0x3995bb,0x3995d0,0x0,Site2Bytes,sizeof(Site2Bytes) },
    { 3,0x399d97,0x399dac,0x0,Site3Bytes,sizeof(Site3Bytes) },
    { 4,0x3997e1,0x3997f6,0x0,Site4Bytes,sizeof(Site4Bytes) },
    { 5,0x399815,0x399829,0x0,Site5Bytes,sizeof(Site5Bytes) },
    { 6,0x39966a,0x39967f,0x0,Site6Bytes,sizeof(Site6Bytes) },
    { 7,0x399e97,0x399e9c,0x0,Site7Bytes,sizeof(Site7Bytes) },
    { 8,0x399f7d,0x399f82,0x0,Site8Bytes,sizeof(Site8Bytes) },
    { 9,0x399fd8,0x399fdd,0x0,Site9Bytes,sizeof(Site9Bytes) },
    { 10,0x399f98,0x399f9d,0x0,Site10Bytes,sizeof(Site10Bytes) },
    { 11,0x399ffa,0x399fff,0x0,Site11Bytes,sizeof(Site11Bytes) },
    { 12,0x39a02c,0x39a031,0x0,Site12Bytes,sizeof(Site12Bytes) },
    { 13,0x399634,0x399641,0x39964e,Site13Bytes,sizeof(Site13Bytes) },
    { 14,0x3995da,0x3995df,0x0,Site14Bytes,sizeof(Site14Bytes) },
    { 15,0x399785,0x39978a,0x0,Site15Bytes,sizeof(Site15Bytes) },
    { 16,0x399db6,0x399dbb,0x0,Site16Bytes,sizeof(Site16Bytes) },
    { 17,0x399565,0x39956a,0x0,Site17Bytes,sizeof(Site17Bytes) },
    { 18,0x399d41,0x399d46,0x0,Site18Bytes,sizeof(Site18Bytes) },
    { 19,0x3957e8,0x3957ed,0x0,Site19Bytes,sizeof(Site19Bytes) },
    { 20,0x395d50,0x395d55,0x0,Site20Bytes,sizeof(Site20Bytes) },
    { 21,0x3984c2,0x3984c7,0x0,Site21Bytes,sizeof(Site21Bytes) },
};
}
