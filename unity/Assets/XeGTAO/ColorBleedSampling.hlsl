#ifndef FTFX_COLOR_BLEED_SAMPLING
#define FTFX_COLOR_BLEED_SAMPLING
uint BleedHash(uint value)
{
 value ^= value >> 16; value *= 0x7feb352du;
 value ^= value >> 15; value *= 0x846ca68bu;
 return value ^ (value >> 16);
}
float BleedUnit(uint value) { return (value >> 8) * (1.0 / 16777216.0); }
uint BleedSampleSeed(int2 pixel, uint frame, bool temporal)
{
 if(temporal)
  return BleedHash(BleedHash((uint)pixel.x ^ 0x68bc21ebu) ^ BleedHash((uint)pixel.y ^ 0x02e5be93u) ^ frame);
 // Preserve the original fixed pattern when temporal accumulation is disabled.
 uint seed=(uint)(pixel.x*1973+pixel.y*9277+89173);
 return (seed^(seed>>13))*1274126177u;
}
float2 BleedDiskSample(uint seed, uint tap, uint samples, bool temporal)
{
 float angle, radius;
 if(temporal)
 {
  uint sampleSeed=BleedHash(seed ^ ((tap+1)*0x9e3779b9u));
  angle=BleedUnit(sampleSeed)*6.28318530718;
  // One independently jittered sample per equal-area annulus. Independent
  // angles avoid a rigid rotating spiral; radius jitter removes moving rings.
  radius=sqrt((tap+BleedUnit(BleedHash(sampleSeed^0xb5297a4du)))/samples);
 }
 else
 {
  angle=(seed&65535)*(6.28318530718/65536.0)+tap*2.39996323;
  radius=sqrt((tap+.5)/samples);
 }
 return float2(cos(angle),sin(angle))*radius;
}
#endif
