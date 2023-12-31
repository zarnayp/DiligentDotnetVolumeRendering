cbuffer Constants
{
    float4x4 g_WorldViewProj;
};


struct VSInput
{
    float4 Pos : ATTRIB0;
};

struct PSInput
{
    float4 pos : SV_POSITION;
};


void main(in VSInput VSIn,
          out PSInput PSIn)
{
    PSIn.pos = mul(float4(VSIn.Pos), g_WorldViewProj);
}