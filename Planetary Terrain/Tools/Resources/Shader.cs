﻿using System;
using System.IO;
using SharpDX.D3DCompiler;
using D3D11 = SharpDX.Direct3D11;

namespace Planetary_Terrain {
    class Shader : IDisposable {
        public D3D11.VertexShader VertexShader { get; private set; }
        public D3D11.PixelShader PixelShader { get; private set; }
        public D3D11.GeometryShader GeometryShader { get; private set; }
        public ShaderSignature Signature { get; private set; }
        public D3D11.InputLayout InputLayout { get; private set; }
        
        public Shader(string file, D3D11.Device device, D3D11.DeviceContext context, params D3D11.InputElement[] inputElements) {
            if (File.Exists(file + "_vs.cso"))
                using (var byteCode = ShaderBytecode.FromFile(file + "_vs.cso")) {
                    Signature = ShaderSignature.GetInputSignature(byteCode);
                    VertexShader = new D3D11.VertexShader(device, byteCode);
                    InputLayout = new D3D11.InputLayout(device, Signature, inputElements);
                }

            if (File.Exists(file + "_ps.cso"))
                using (var byteCode = ShaderBytecode.FromFile(file + "_ps.cso"))
                    PixelShader = new D3D11.PixelShader(device, byteCode);

            if (File.Exists(file + "_gs.cso"))
                using (var byteCode = ShaderBytecode.FromFile(file + "_gs.cso"))
                    GeometryShader = new D3D11.GeometryShader(device, byteCode);
        }

        public void Set(Renderer renderer) {
            renderer.Context.InputAssembler.InputLayout = InputLayout;
            renderer.Context.VertexShader.Set(VertexShader);
            renderer.Context.PixelShader.Set(PixelShader);

            renderer.Context.VertexShader.SetConstantBuffer(0, renderer.constantBuffer);
            renderer.Context.PixelShader.SetConstantBuffer(0, renderer.constantBuffer);

            renderer.Context.PixelShader.SetSampler(0, renderer.AnisotropicSampler);
        }

        public void Dispose() {
            VertexShader?.Dispose();
            PixelShader?.Dispose();
            GeometryShader?.Dispose();
            Signature?.Dispose();
            InputLayout?.Dispose();
        }
    }
}
