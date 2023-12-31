cbuffer Constants
{
    float4x4 g_WorldViewProj;
};

struct VSInput
{
    float3 Pos : ATTRIB0;
};


struct PSInput
{
    float4 pos : SV_POSITION;
    float4 tex : TEXCOORD0; // Position in model coordinates
};

void main(in  VSInput VSIn,
          out PSInput PSIn) 
{
    PSIn.pos = mul(float4(VSIn.Pos, 1.0), g_WorldViewProj);
    PSIn.tex = 0.5 * (float4(VSIn.Pos, 1.0) + 1);
}
