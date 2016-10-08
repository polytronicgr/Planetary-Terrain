#include "model.hlsl"

float4 main(v2f i) : SV_TARGET
{
	float3 col = DiffuseTexture.Sample(DiffuseSampler, i.uv).rgb;
	if (length(LightDirection) > 0)
		col *= clamp(dot(LightDirection, -i.normal), 0, 1);
	return float4(col, 1);
}