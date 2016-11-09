#define PI 3.14159265359
#define PI4 12.56637061436
#define G 9.8

// BUFFERS //
// b0: Camera (View, Projection)
// b1: Model (Quadnode constants, model constants)
// b2: Planet constants

// SAMPLERS //
// s0: Anisotropic sampler

cbuffer WorldConstants : register(b0) {
	float4x4 View;
	float4x4 Projection;
	float C;
	float FC;
};

SamplerState AnisotropicSampler : register(s0);

float LogDepth(float w) {
	return max(log2(C*w + 1),1e-6)*FC*w;
}

float3 BumpNormal(float3 normal, float3 tangent, float3 mapSample) {
	return normalize(mul(float3x3(normalize(tangent - dot(tangent, normal) * normal), cross(tangent, normal), normal), 2 * mapSample - 1));
}
