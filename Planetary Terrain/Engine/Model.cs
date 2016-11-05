﻿using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using SharpDX;
using D3D11 = SharpDX.Direct3D11;
using Assimp;
using System.Runtime.InteropServices;

namespace Planetary_Terrain {
    static class AssimpHelper {
        public static Vector3 ToVector3(this Vector3D v) {
            return new Vector3(v.X, v.Y, v.Z);
        }
        public static Matrix ToMatrix(this Assimp.Matrix4x4 v) {
            return Matrix.Transpose(new Matrix(
                v.A1, v.A2, v.A3, v.A4,
                v.B1, v.B2, v.B3, v.B4,
                v.C1, v.C2, v.C3, v.C4,
                v.D1, v.D2, v.D3, v.D4
                ));
        }
    }
    class Model {
        [StructLayout(LayoutKind.Explicit, Size = 176)]
        struct Constants {
            [FieldOffset(0)]
            public Matrix World;
            [FieldOffset(64)]
            public Matrix WorldInverseTranspose;
            
            [FieldOffset(128)]
            public Vector3 lightDirection;
            
            [FieldOffset(144)]
            public Vector3 SpecularColor;

            [FieldOffset(156)]
            public float Shininess;
            [FieldOffset(160)]
            public float SpecularIntensity;
        }
        Constants constants;
        D3D11.Buffer cbuffer;

        public float Shininess
        {
            get { return constants.Shininess; }
            set { constants.Shininess = value; }
        }
        public float SpecularIntensity
        {
            get { return constants.SpecularIntensity; }
            set { constants.SpecularIntensity = value; }
        }
        public Color SpecularColor
        {
            get { return new Color(constants.SpecularColor); }
            set { constants.SpecularColor = value.ToVector3(); }
        }
        
        public List<ModelMesh> Meshes;
        string modelPath;

        public Model(string file, D3D11.Device device) {
            AssimpContext ctx = new AssimpContext();
            if (!ctx.IsImportFormatSupported(Path.GetExtension(file)))
                return;

            modelPath = Path.GetDirectoryName(file);

            Scene scene = ctx.ImportFile(file);
            Node node = scene.RootNode;
            Matrix mat = Matrix.Identity;

            Meshes = new List<ModelMesh>();
            AddNode(scene, scene.RootNode, device, mat);

            constants = new Constants();
            SpecularColor = Color.White;
            Shininess = 200;
            SpecularIntensity = 1;
        }

        public void AddNode(Scene scene, Node node, D3D11.Device device, Matrix transform) {
            transform = transform * node.Transform.ToMatrix();

            Matrix invTranspose = Matrix.Transpose(Matrix.Invert(transform));
            if (node.HasMeshes) {
                foreach (int index in node.MeshIndices) {
                    Mesh mesh = scene.Meshes[index];

                    ModelMesh mm = new ModelMesh();
                    Meshes.Add(mm);

                    Material mat = scene.Materials[mesh.MaterialIndex];
                    if (mat != null) {
                        if (mat.GetMaterialTextureCount(TextureType.Diffuse) > 0)
                            mm.SetDiffuseTexture(device, modelPath + "/" + mat.TextureDiffuse.FilePath);
                        if (mat.GetMaterialTextureCount(TextureType.Emissive) > 0) 
                            mm.SetEmissiveTexture(device, modelPath + "/" + mat.TextureEmissive.FilePath);
                        if (mat.GetMaterialTextureCount(TextureType.Specular) > 0)
                            mm.SetSpecularTexture(device, modelPath + "/" + mat.TextureSpecular.FilePath);
                        if (mat.GetMaterialTextureCount(TextureType.Normals) > 0)
                            mm.SetNormalTexture(device, modelPath + "/" + mat.TextureNormal.FilePath);
                    }

                    //bool hasTexCoords = mesh.HasTextureCoords(0);
                    //bool hasColors = mesh.HasVertexColors(0);
                    //bool hasNormals = mesh.HasNormals;

                    //int ec = 1;
                    //if (hasTexCoords) ec++;
                    //if (hasColors) ec++;
                    //if (hasNormals) ec++;

                    //mm.InputElements = new D3D11.InputElement[ec];
                    //int e = 0;
                    //mm.InputElements[e++] = new D3D11.InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32_Float, 0, 0);
                    //
                    //if (hasNormals) {
                    //    mm.InputElements[e++] = new D3D11.InputElement("NORMAL", 0, SharpDX.DXGI.Format.R32G32B32_Float, mm.VertexSize, 0);
                    //    mm.VertexSize += Utilities.SizeOf<Vector3>();
                    //}
                    //if (hasColors) {
                    //    mm.InputElements[e++] = new D3D11.InputElement("COLOR", 0, SharpDX.DXGI.Format.R32G32B32A32_Float, mm.VertexSize, 0);
                    //    mm.VertexSize += Utilities.SizeOf<Vector4>();
                    //}
                    //if (hasTexCoords) {
                    //    mm.InputElements[e++] = new D3D11.InputElement("TEXCOORD", 0, SharpDX.DXGI.Format.R32G32B32_Float, mm.VertexSize, 0);
                    //    mm.VertexSize += Utilities.SizeOf<Vector3>();
                    //}
                    
                    Vector3D[] verts = mesh.Vertices.ToArray();
                    Vector3D[] texCoords = mesh.TextureCoordinateChannels[0].ToArray();
                    Vector3D[] normals = mesh.Normals.ToArray();
                    //Color4D[] colors = mesh.VertexColorChannels[0].ToArray();
                    
                    switch (mesh.PrimitiveType) {
                        case PrimitiveType.Point:
                            mm.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.PointList;
                            break;
                        case PrimitiveType.Line:
                            mm.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.LineList;
                            break;
                        case PrimitiveType.Triangle:
                            mm.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
                            break;
                        default:
                            break;
                    }

                    VertexNormalTexture[] verticies = new VertexNormalTexture[mesh.VertexCount];
                    for (int i = 0; i < mesh.VertexCount; i++) {
                        verticies[i] = new VertexNormalTexture(
                            (Vector3)Vector3.Transform(new Vector3(verts[i].X, verts[i].Y, verts[i].Z), transform),
                            (Vector3)Vector3.Transform(new Vector3(normals[i].X, normals[i].Y, normals[i].Z), invTranspose),
                            new Vector2(texCoords[i].X, 1f - texCoords[i].Y));
                    }

                    mm.VertexSize = Utilities.SizeOf<VertexNormalTexture>();

                    mm.VertexBuffer = D3D11.Buffer.Create(device, D3D11.BindFlags.VertexBuffer, verticies);
                    mm.VertexCount = mesh.VertexCount;

                    List<short> indicies = new List<short>();
                    foreach (Face f in mesh.Faces)
                        if (f.HasIndices)
                            for (int i = 2; i < f.Indices.Count; i++) {
                                indicies.Add((short)f.Indices[0]);
                                indicies.Add((short)f.Indices[i - 1]);
                                indicies.Add((short)f.Indices[i]);
                            }
                    mm.IndexBuffer = D3D11.Buffer.Create(device, D3D11.BindFlags.IndexBuffer, indicies.ToArray());
                    mm.IndexCount = indicies.Count;
                }
            }

            foreach (Node c in node.Children)
                AddNode(scene, c, device, transform);
        }

        public void SetResources(Renderer renderer, Vector3d lightDirection, Matrix world) {
            constants.World = world;
            constants.WorldInverseTranspose = Matrix.Invert(Matrix.Transpose(world));
            constants.lightDirection = lightDirection;

            // create/update constant buffer
            if (cbuffer == null)
                cbuffer = D3D11.Buffer.Create(renderer.Device, D3D11.BindFlags.ConstantBuffer, ref constants);
            else
                renderer.Context.UpdateSubresource(ref constants, cbuffer);
            
            renderer.Context.VertexShader.SetConstantBuffer(1, cbuffer);
            renderer.Context.PixelShader.SetConstantBuffer(1, cbuffer);
        }

        public void Draw(Renderer renderer) {
            foreach (ModelMesh m in Meshes)
                m.Draw(renderer);
        }

        public void Draw(Renderer renderer, Vector3d lightDirection, Matrix world) {
            SetResources(renderer, lightDirection, world);
            Draw(renderer);
        }
        
        public void DrawInstanced(Renderer renderer, Vector3d lightDirection, Matrix world, int instanceCount) {
            SetResources(renderer, lightDirection, world);

            foreach (ModelMesh m in Meshes)
                m.DrawInstanced(renderer, instanceCount);
        }

        public void Dispose() {
            if (Meshes != null)
                foreach (ModelMesh m in Meshes)
                    m.Dispose();
            cbuffer?.Dispose();
        }
    }
}
