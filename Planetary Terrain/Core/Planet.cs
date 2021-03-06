﻿using System;
using SharpDX;
using SharpDX.Mathematics.Interop;
using D2D1 = SharpDX.Direct2D1;
using DWrite = SharpDX.DirectWrite;
using D3D11 = SharpDX.Direct3D11;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using SharpDX.Direct3D;

namespace Planetary_Terrain {
    class Planet : CelestialBody, IDisposable {
        /// <summary>
        /// The total possible terrain displacement is Radius +/- TerrainHeight
        /// </summary>
        public double TerrainHeight;

        /// <summary>
        /// The planet's atmosphere
        /// </summary>
        public Atmosphere Atmosphere;
        public double SurfaceTemperature; // in Celsuis
        public double TemperatureRange; // in Celsuis

        public bool HasOcean = false;
        public bool HasTrees = false;
        public double OceanHeight;
        public Color OceanColor;
        
        /// <summary>
        /// The map of temperature-humidity to color
        /// </summary>
        D3D11.Texture2D colorMap;
        /// <summary>
        /// The map of temperature-humidity to color
        /// </summary>
        D3D11.ShaderResourceView colorMapView;

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        struct Constants {
            [FieldOffset(0)]
            public Vector3 oceanColor;
            [FieldOffset(12)]
            public float oceanLevel;
        }
        Constants constants;
        public D3D11.Buffer constBuffer { get; private set; }
        
        public Planet(string name, Vector3d pos, double radius, double mass, double terrainHeight, Atmosphere atmosphere = null) : base(pos, radius, mass) {
            Name = name;
            Radius = radius;
            TerrainHeight = terrainHeight;
            Atmosphere = atmosphere;

            BoundingRadius = radius + terrainHeight;

            if (atmosphere != null) atmosphere.Planet = this;
            
            OceanHeight = .5;
            OceanColor = new Color(35, 90, 200);
        }

        public static double min=1, max=-1;
        double height(Vector3d direction) {
            double total = 0;
            
            double rough = Noise.Ridged(direction * 50 + new Vector3d(-5000), 5, .2, .7) * .5 + .5;

            double mntn = Noise.Fractal(direction * 1000 + new Vector3d(2000), 11, .03, .5);
            double flat = Noise.SmoothSimplex(direction * 100 + new Vector3d(1000), 2, .01, .45) * .5 + .5;

            rough = rough * rough;

            flat *= 1.0 - rough;
            mntn *= rough;
            
            total = mntn + flat;
            
            total += (Noise.SmoothSimplex(direction * 50 + new Vector3d(-100), 7, .3, .2) * .5 + .5);

            total = (total + 1) / 3;
            
            min = Math.Min(min, total);
            max = Math.Max(max, total);

            return total;
        }
        public override double GetHeight(Vector3d direction, bool transformDirection = true) {
            if (transformDirection)
                direction = Vector3d.Transform(direction, Matrix3x3.Invert((Matrix3x3)Rotation)); // TODO: This is way off

            double h = Radius + height(direction) * TerrainHeight;

            if (transformDirection) {
                direction = Vector3d.Transform(direction, (Matrix3x3)Rotation);
                Debug.DrawLine(Color.Black, Position + direction * h, Position + direction * (h + 100));
            }

            return h;
        }

        double temperature(Vector3d dir) {
            return Noise.SmoothSimplex(dir * 100, 5, .3f, .8f);
        }
        public double GetTemperature(Vector3d direction) {
            return SurfaceTemperature + TemperatureRange * temperature(direction);
        }
        public double GetHumidity(Vector3d direction) {
            return Noise.SmoothSimplex(direction * 200, 4, .1f, .8f) * .5 + .5;
        }

        public override void GetSurfaceInfo(Vector3d direction, out Vector2 data, out double h) {
            h = height(direction);
            data = new Vector2((float)temperature(direction) * .5f + .5f, (float)GetHumidity(direction));
        }

        public void SetColormap(string file, D3D11.Device device) {
            colorMap?.Dispose();
            colorMapView?.Dispose();

            D3D11.Resource rsrc;
            ResourceUtil.LoadFromFile(device, file, out colorMapView, out rsrc);
            colorMap = rsrc as D3D11.Texture2D;
        }

        public override void UpdateLOD(D3D11.Device device, Camera camera) {
            base.UpdateLOD(device, camera);
            Atmosphere?.UpdateLOD(device, camera);
        }
        
        public override void Draw(Renderer renderer) {
            Profiler.Begin(Name + " Draw");
            WasDrawnLastFrame = false;
            renderer.Context.Rasterizer.State = renderer.DrawWireframe ? renderer.rasterizerStateWireframeCullBack : renderer.rasterizerStateSolidCullBack;

            // Get the entire planet's scale and scaled position
            Vector3d pos;
            double scale;
            double dist;
            renderer.ActiveCamera.GetScaledSpace(Position, out pos, out scale, out dist);
            if (scale * Radius < 1) { Profiler.End(); return; }

            BoundingSphere bs = new BoundingSphere(pos, (float)(BoundingRadius * scale));
            if (!renderer.ActiveCamera.Frustum.Intersects(ref bs)) { Profiler.End(); return; }

            constants.oceanLevel = (float)OceanHeight;
            constants.oceanColor = OceanColor.ToVector3();

            // create/update constant buffer
            if (constBuffer == null)
                constBuffer = D3D11.Buffer.Create(renderer.Device, D3D11.BindFlags.ConstantBuffer, ref constants);
            else
                renderer.Context.UpdateSubresource(ref constants, constBuffer);

            if (Atmosphere != null){
                Profiler.Begin("Atmosphere Draw");
                // draw atmosphere behind planet
                Atmosphere?.Draw(renderer, pos, scale);
                Profiler.End();
            }

            Profiler.Begin("Resource/Buffer Set");
            
            Shaders.Planet.Set(renderer);

            // atmosphere constants
            if (Atmosphere != null) {
                renderer.Context.VertexShader.SetConstantBuffers(3, Atmosphere.constBuffer);
                renderer.Context.PixelShader.SetConstantBuffers(3, Atmosphere.constBuffer);
            } else {
                renderer.Context.VertexShader.SetConstantBuffer(3, null);
                renderer.Context.PixelShader.SetConstantBuffer(3, null);
            }

            // set constant buffer
            renderer.Context.VertexShader.SetConstantBuffers(2, constBuffer);
            renderer.Context.PixelShader.SetConstantBuffers(2, constBuffer);

            // color map
            renderer.Context.PixelShader.SetShaderResource(1, colorMapView);

            renderer.Context.OutputMerger.SetBlendState(renderer.blendStateTransparent);
            Profiler.End();

            Profiler.Begin("Node Prepare");
            foreach (QuadNode n in VisibleNodes)
                n.PrepareDraw(renderer);
            Profiler.End();

            Profiler.Begin("Ground Draw");
            foreach (QuadNode n in VisibleNodes)
                n.Draw(renderer, pos, scale, dist);
            Profiler.End();

            if (HasOcean) {
                Profiler.Begin(Name + " Water Draw");
                // set water shader
                Shaders.Water.Set(renderer);

                renderer.Context.VertexShader.SetConstantBuffers(2, constBuffer);
                renderer.Context.PixelShader.SetConstantBuffers(2, constBuffer);
                
                // atmosphere constants
                if (Atmosphere != null) {
                    renderer.Context.VertexShader.SetConstantBuffers(3, Atmosphere.constBuffer);
                    renderer.Context.PixelShader.SetConstantBuffers(3, Atmosphere.constBuffer);
                }

                foreach (QuadNode n in VisibleNodes)
                    n.DrawWater(renderer, pos, scale, dist);

                Profiler.End();
            }
            if (HasTrees && Properties.Settings.Default.DrawTrees) {
                // tree pass
                Profiler.Begin("Draw Trees");
                
                List<QuadNode> trees = new List<QuadNode>();
                List<QuadNode> imposters = new List<QuadNode>();
                for (int i = 0; i < BaseNodes.Length; i++)
                    BaseNodes[i].GetTreeNodes(renderer, ref trees, ref imposters);

                if (trees.Count > 0) {
                    Shaders.ModelInstanced.Set(renderer);
                    foreach (QuadNode n in trees)
                        n.DrawTrees(renderer);
                }
                if (imposters.Count > 0) {
                    renderer.Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                    renderer.Context.InputAssembler.SetVertexBuffers(0, new D3D11.VertexBufferBinding(Resources.QuadVertexBuffer, sizeof(float) * 5, 0));
                    renderer.Context.InputAssembler.SetIndexBuffer(Resources.QuadIndexBuffer, SharpDX.DXGI.Format.R16_UInt, 0);
                
                    Shaders.Imposter.Set(renderer);
                    renderer.Context.PixelShader.SetShaderResource(1, Resources.TreeModelImposterDiffuse);
                    renderer.Context.PixelShader.SetShaderResource(2, Resources.TreeModelImposterNormals);
                
                    foreach (QuadNode n in imposters)
                        n.DrawImposters(renderer);
                }

                Profiler.End();
            }

            WasDrawnLastFrame = true;
            Profiler.End();
        }

        public override void Dispose() {
            colorMap?.Dispose();
            colorMapView?.Dispose();

            constBuffer?.Dispose();
            
            for (int i = 0; i < BaseNodes.Length; i++)
                BaseNodes[i].Dispose();
            
            Atmosphere?.Dispose();
        }
    }
}
