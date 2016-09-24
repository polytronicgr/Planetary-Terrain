﻿using System;
using SharpDX;
using SharpDX.Direct3D;
using System.Threading;
using D3D11 = SharpDX.Direct3D11;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace Planetary_Terrain {
    class QuadTree : IDisposable {
        public const int GridSize = 16;

        public double Size;
        public double ArcSize;
        public double VertexSpacing; // meters per vertex

        public Body Body;
        public QuadTree Parent;
        public int SiblingIndex;
        public QuadTree[] Children;

        /// <summary>
        /// The position on the cube, before being projected into a sphere
        /// </summary>
        public Vector3d CubePosition;

        /// <summary>
        /// The position of the mesh of which it is drawn at, relative to the planet
        /// </summary>
        public Vector3d MeshCenter;
        public Matrix3x3 Orientation;

        int[] vertexSamples;

        VertexNormalTexture[] verticies;
        short[] indicies;

        public int IndexCount { get; private set; }
        public int VertexCount { get; private set; }
        
        [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 128)]
        struct Constants {
            public Matrix World;
            public Matrix WorldInverseTranspose;
        }
        private Constants shaderConstants;

        public D3D11.Buffer vertexBuffer { get; private set; }
        public D3D11.Buffer indexBuffer { get; private set; }
        public D3D11.Buffer constantBuffer { get; private set; }

        bool dirty = false;
        bool generating = false;
        
        public QuadTree(Body body, int siblingIndex, double size, QuadTree parent, Vector3d cubePos, Matrix3x3 rot) {
            SiblingIndex = siblingIndex;
            Size = size;
            Body = body;
            Parent = parent;
            ArcSize = Body.ArcLength(Size);

            VertexSpacing = Size / GridSize;

            CubePosition = cubePos;
            Orientation = rot;

            shaderConstants = new Constants();
            shaderConstants.World = Matrix.Identity;
        }
        
        public void Generate() {
            if (generating) return;
            generating = true;

            ThreadPool.QueueUserWorkItem(new WaitCallback((object o) => {
                Vector3d posn = Vector3d.Normalize(CubePosition);
                MeshCenter = posn * Body.GetHeight(posn);

                double scale = Size / GridSize;
                double invScale = 1d / Size;

                int s = GridSize + 1;
                verticies = new VertexNormalTexture[s * s * 6];
                List<short> inds = new List<short>();

                Vector3 n;
                Vector3d p1d, p2d, p3d;
                Vector3d offset = new Vector3d(GridSize * .5, 0, GridSize * .5);

                int v = s - 1;
                vertexSamples = new int[] {
                    // x*s + v
                    0 * s + 0,       // 0, 0
                    v * s + 0,       // 1, 0
                    0 * s + v,       // 0, 1
                    v * s + v,       // 1, 1
                    (v/2) * s + 0,   // .5, 0
                    0 * s + v/2,     // 0, .5
                    (v/2) * s + v,   // .5, 1
                    v * s + v/2,     // 1, .5
                    (v/2) * s + v/2, // .5, .5
                };
                
                for (int x = 0; x < s; x++) {
                    for (int z = 0; z < s; z++) {
                        if (!generating)
                            break;

                        p1d = Vector3d.Normalize(CubePosition + Vector3d.Transform(scale * (new Vector3d(x, 0, z) - offset), Orientation));
                        p2d = Vector3d.Normalize(CubePosition + Vector3d.Transform(scale * (new Vector3d(x, 0, z + 1) - offset), Orientation));
                        p3d = Vector3d.Normalize(CubePosition + Vector3d.Transform(scale * (new Vector3d(x + 1, 0, z) - offset), Orientation));
                        
                        Vector2 t = Body.GetTemp(p1d);

                        p1d *= Body.GetHeight(p1d);
                        p2d *= Body.GetHeight(p2d);
                        p3d *= Body.GetHeight(p3d);

                        p1d -= MeshCenter;
                        p2d -= MeshCenter;
                        p3d -= MeshCenter;

                        n = Vector3.Cross(Vector3d.Normalize(p2d - p1d), Vector3d.Normalize(p3d - p1d));
                        
                        verticies[x * s + z] = new VertexNormalTexture(p1d * invScale, n, t);

                        if (x + 1 < s && z + 1 < s) {
                            // middle/no border
                            // TODO: Quad fanning to handle cracks
                            inds.Add((short)((x + 1) * s + z));
                            inds.Add((short)(x * s + z));
                            inds.Add((short)(x * s + z + 1));

                            inds.Add((short)((x + 1) * s + z + 1));
                            inds.Add((short)((x + 1) * s + z));
                            inds.Add((short)(x * s + z + 1));
                        }
                    }

                    if (!generating)
                        break;
                }
                if (!generating) {
                    dirty = false;
                    verticies = null;
                    vertexSamples = null;
                    return;
                }

                indicies = inds.ToArray();

                generating = false;
                dirty = true;
            }));
        }

        public void SetData(D3D11.Device device, D3D11.DeviceContext context) {
            vertexBuffer?.Dispose();
            vertexBuffer = D3D11.Buffer.Create(device, D3D11.BindFlags.VertexBuffer, verticies);

            indexBuffer?.Dispose();
            indexBuffer = D3D11.Buffer.Create(device, D3D11.BindFlags.IndexBuffer, indicies);

            VertexCount = verticies.Length;
            IndexCount = indicies.Length;

            dirty = false;
        }

        public Vector3d ClosestVertex(Vector3d pos) {
            if (vertexSamples == null) return MeshCenter + Body.Position;
            pos -= MeshCenter + Body.Position;

            int close = 1;
            double dist = double.MaxValue;
            for (int i = 0; i < vertexSamples.Length; i++) {
                double d = (pos - verticies[vertexSamples[i]].Position).LengthSquared();
                if (d < dist) {
                    dist = d;
                    close = vertexSamples[i];
                }
            }

            return (Vector3d)verticies[close].Position + MeshCenter + Body.Position;
        }

        public void Split(D3D11.Device device) {
            if (Children != null)
                return;

            if (generating) {
                generating = false;
                dirty = false;
            }
            double s = Size * .5;

            //  | 0 | 1 |
            //  | 2 | 3 |

            Vector3d right = Vector3.Transform(Vector3.Right, Orientation);
            Vector3d fwd = Vector3.Transform(Vector3.ForwardLH, Orientation);

            Vector3d p0 = (-right + fwd);
            Vector3d p1 = (right + fwd);
            Vector3d p2 = (-right + -fwd);
            Vector3d p3 = (right + -fwd);

            Children = new QuadTree[4];
            Children[0] = new QuadTree(Body, 0, s, this, CubePosition + s * .5 * p0, Orientation);
            Children[1] = new QuadTree(Body, 1, s, this, CubePosition + s * .5 * p1, Orientation);
            Children[2] = new QuadTree(Body, 2, s, this, CubePosition + s * .5 * p2, Orientation);
            Children[3] = new QuadTree(Body, 3, s, this, CubePosition + s * .5 * p3, Orientation);

            Children[0].Generate();
            Children[1].Generate();
            Children[2].Generate();
            Children[3].Generate();
        }
        public void UnSplit() {
            if (Children == null) return;

            for (int i = 0; i < Children.Length; i++)
                Children[i]?.Dispose();

            Children = null;
        }
        public void SplitDynamic(Vector3d pos, D3D11.Device device) {
            double d = (ClosestVertex(pos) - pos).LengthSquared();

            if (d < Size * Size || VertexSpacing > Body.MaxVertexSpacing) {
                if (Children != null) {
                    for (int i = 0; i < Children.Length; i++)
                        Children[i].SplitDynamic(pos, device);
                } else {
                    if ((Size * .5f) / GridSize > Body.MinVertexSpacing)
                        Split(device);
                }
            } else
                UnSplit();

        }

        public bool Ready() {
            return dirty || vertexBuffer != null;
        }

        public bool IsAboveHorizon(Vector3d camera) {
            return true;

            Vector3d planetToCam = Vector3d.Normalize(camera - Body.Position);
            Vector3d planetToMesh = Vector3d.Normalize(ClosestVertex(camera) - Body.Position);

            double horizonAngle = Math.Acos(Body.Radius * .99 / (Body.Position - camera).Length());
            double meshAngle = Math.Acos(Vector3.Dot(planetToCam, planetToMesh));

            return horizonAngle > meshAngle;
        }

        public void Draw(Renderer renderer, Matrix world) {
            shaderConstants.World = world;
            shaderConstants.WorldInverseTranspose = Matrix.Identity;

            if (constantBuffer == null)
                constantBuffer = D3D11.Buffer.Create(renderer.Device, D3D11.BindFlags.ConstantBuffer, ref shaderConstants);
            renderer.Context.UpdateSubresource(ref shaderConstants, constantBuffer);

            renderer.Context.VertexShader.SetConstantBuffer(1, constantBuffer);
            renderer.Context.PixelShader.SetConstantBuffer(1, constantBuffer);

            renderer.Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            renderer.Context.InputAssembler.SetVertexBuffers(0, new D3D11.VertexBufferBinding(vertexBuffer, Utilities.SizeOf<VertexNormalTexture>(), 0));
            renderer.Context.InputAssembler.SetIndexBuffer(indexBuffer, SharpDX.DXGI.Format.R16_UInt, 0);

            renderer.Context.DrawIndexed(indicies.Length, 0, 0);

            Debug.ChunksDrawn++;
        }
        
        public void Draw(Renderer renderer, Vector3d planetPos, double planetScale) {
            bool draw = true;

            if (Children != null) {
                draw = false;

                for (int i = 0; i < Children.Length; i++)
                    if (!Children[i].Ready())
                        draw = true;

                if (!draw)
                    for (int i = 0; i < Children.Length; i++)
                        Children[i].Draw(renderer, planetPos, planetScale);
            }

            if (draw) {
                if (dirty)
                    SetData(renderer.Device, renderer.Context);

                if (vertexBuffer != null) {
                    if (IsAboveHorizon(renderer.Camera.Position)) {
                        Vector3d pos = MeshCenter * planetScale + planetPos;
                        double scale = planetScale;

                        renderer.Camera.GetScaledSpace(MeshCenter + Body.Position, out pos, out scale);

                        scale *= Size;

                        Draw(renderer, Matrix.Scaling((float)scale) * Matrix.Translation(pos));

                        double d = (renderer.Camera.Position - (MeshCenter + Body.Position)).Length();
                        if (d < Debug.ClosestQuadTreeDistance) {
                            Debug.ClosestQuadTree = this;
                            Debug.ClosestQuadTreeDistance = d;
                            Debug.ClosestQuadTreeScale = scale;
                        }
                    }
                }
            }
        }

        public void Dispose() {
            vertexBuffer?.Dispose();
            constantBuffer?.Dispose();
            indexBuffer?.Dispose();

            if (Children != null)
                for (int i = 0; i < Children.Length; i++)
                    Children[i].Dispose();
        }
    }
}