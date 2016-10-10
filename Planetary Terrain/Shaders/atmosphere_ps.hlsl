#include "atmosphere.hlsl"

float4 main(v2f i) : SV_TARGET
{
	float fCos = dot(LightDirection, normalize(i.rd));
	float fCos2 = fCos*fCos;
	float4 color = getRayleighPhase(fCos2) * i.C0 + getMiePhase(fCos, fCos2, g, g*g) * i.C1;
	
	return float4(color.rgb, length(color.rgb));
}

