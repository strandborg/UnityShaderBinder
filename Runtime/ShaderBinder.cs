/* 
 * ShaderBinder utility library
 * (c) 2021 Mikko Strandborg
 * 
 * Licensed under the Boost Software License 1.0
 * 
 * Boost Software License - Version 1.0 - August 17th, 2003
 * 
 * Permission is hereby granted, free of charge, to any person or organization
 * obtaining a copy of the software and accompanying documentation covered by
 * this license (the "Software") to use, reproduce, display, distribute,
 * execute, and transmit the Software, and to prepare derivative works of the
 * Software, and to permit third-parties to whom the Software is furnished to
 * do so, all subject to the following:
 * 
 * The copyright notices in the Software and this entire statement, including
 * the above license grant, this restriction and the following disclaimer,
 * must be included in all copies of the Software, in whole or in part, and
 * all derivative works of the Software, unless such copies or derivative
 * works are solely in the form of machine-executable object code generated by
 * a source language processor.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT
 * SHALL THE COPYRIGHT HOLDERS OR ANYONE DISTRIBUTING THE SOFTWARE BE LIABLE
 * FOR ANY DAMAGES OR OTHER LIABILITY, WHETHER IN CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * 
 */



using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Varjo.ShaderBinder
{

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class ShaderValueAttribute : Attribute
    {
        public ShaderValueAttribute(string Target = null,
                                    bool GenerateHLSL = true,
                                    string HLSLType = null,
                                    Type InnerType = null,
                                    bool GloballyCoherent = false,
                                    bool AddRW = false,
                                    bool IsUAV = false,
                                    int ArraySize = 0,
                                    bool IsTexArray = false,
                                    int TexDimension = 2,
                                    [CallerFilePath] string SourcePath = null)
        {
            this.Target = Target;
            this.GenerateHLSL = GenerateHLSL;
            this.HLSLType = HLSLType;
            this.InnerType = InnerType ?? typeof(Vector4);
            this.GloballyCoherent = GloballyCoherent;
            this.ArraySize = ArraySize;
            this.IsUAV = IsUAV;
            this.AddRW = AddRW;
            this.IsTexArray = IsTexArray;
            this.TexDimension = TexDimension;
            this.sourcePath = SourcePath;
        }

        // If set, can be used to override constant name in HLSL
        public string Target { get; set; }

        // If set to false, no HLSL counterpart will be generated. Defaults to true.
        public bool GenerateHLSL { get; set; }

        // Optional override for HLSL side type
        public string HLSLType { get; set; }

        // For Textures and buffers the inner type, eg. StructuredBuffer<MyStructType>
        public Type InnerType { get; set; }

        // If set, adds globallycoherent to the UAV declaration
        public bool GloballyCoherent { get; set; }

        // For array fields the size of the array
        public int ArraySize { get; set; }

        // Set if the field is UAV (RWTexture2D instead of Texture2D etc)
        public bool IsUAV { get; set; }
        
        // If set, creates additional UAV variant on HLSL side with 'RW' suffix.
        public bool AddRW { get; set; }

        public bool IsTexArray { get; set; }

        public int TexDimension { get; set; }

        public string sourcePath;
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class ShaderKeywordAttribute : Attribute
    {
        public ShaderKeywordAttribute() { Target = null; }

        public string Target { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class ShaderKernelAttribute : Attribute
    {
        public ShaderKernelAttribute() { Target = null; }

        public string Target { get; set; }
    }

    public struct ComputeKernel
    {
        private ComputeShader m_CS;
        private int m_KernelIdx;
        public string m_Name;

        public ComputeKernel(string name = null)
        {
            m_Name = name;
            m_CS = null;
            m_KernelIdx = 0;
        }

        public void Connect(ComputeShader cs)
        {
            m_CS = cs;
            m_KernelIdx = m_CS.FindKernel(m_Name);
        }

        public void Dispatch(uint x, uint y, uint z)
        {
            m_CS.Dispatch(m_KernelIdx, (int)x, (int)y, (int)z);
        }
        public void Dispatch(int x, int y, int z)
        {
            m_CS.Dispatch(m_KernelIdx, x, y, z);
        }
    }

    public class TypedBufferBase
    {
        public GraphicsBuffer Buffer { get; set; }

        public int count => Buffer.count;
        public int stride => Buffer.stride;

        public GraphicsBuffer.Target target => Buffer.target;

        public static implicit operator GraphicsBuffer(TypedBufferBase b) => b.Buffer;
    }
    public class StructuredBuffer<T> : TypedBufferBase
    {
        public StructuredBuffer(int count, GraphicsBuffer.Target extraTargets = 0 )
        {
            Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | extraTargets, count, Marshal.SizeOf<T>());
        }

        public void Release()
        {
            Buffer?.Release();
        }
    }

    public class RWStructuredBuffer<T> : TypedBufferBase
    {
        public RWStructuredBuffer(int count, GraphicsBuffer.Target extraTargets = 0)
        {
            Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | extraTargets, count, Marshal.SizeOf<T>());
        }

        public void Release()
        {
            Buffer?.Release();
        }
    }


    internal class ShaderBinder
    {
        private struct ValInfo
        {
            public enum Type
            {
                Invalid,
                Float,
                Int,
                Uint,
                BoolAsInt,
                Vector3,
                Vector4,
                Vector3Int,
                FloatArray,
                VectorArray,
                Matrix,
                MatrixArray,
                Texture,
                ComputeBuffer,
                GraphicsBuffer,
                TypedBuffer,
            };

            public Type type;
            public int shaderID;
        };

        private struct KeywordEntry
        {
            public FieldInfo fieldInfo;
            public PropertyInfo propInfo;
            public string name;
            public Dictionary<LocalKeywordSpace, LocalKeyword> keywords;

            public LocalKeyword GetLocalKeyword(LocalKeywordSpace space)
            {
                if (keywords.TryGetValue(space, out var kw))
                {
                    return kw;
                }
                var res = space.FindKeyword(name);
                keywords.Add(space, res);
                return res;

            }
            public LocalKeyword GetLocalKeyword(Material mat) => GetLocalKeyword(mat.shader.keywordSpace);
            public LocalKeyword GetLocalKeyword(ComputeShader cs) => GetLocalKeyword(cs.keywordSpace);
        }
        private struct EnumKeywordEntry
        {
            public FieldInfo fieldInfo;
            public PropertyInfo propInfo;
            public string[] names;
            public int[] values;
            public Dictionary<LocalKeywordSpace, LocalKeyword[]> keywords;

            public LocalKeyword[] GetLocalKeywords(LocalKeywordSpace space)
            {
                if (keywords.TryGetValue(space, out var key))
                    return key;

                var res = names.Select(name => space.FindKeyword(name)).ToArray();
                keywords.Add(space, res);
                return res;
            }

            public LocalKeyword[] GetLocalKeywords(Material mat) => GetLocalKeywords(mat.shader.keywordSpace);
            public LocalKeyword[] GetLocalKeywords(ComputeShader cs) => GetLocalKeywords(cs.keywordSpace);

        }

        private List<(FieldInfo, ValInfo)> m_Values = new ();
        private List<(PropertyInfo, ValInfo)> m_PropValues = new();
        private List<KeywordEntry> m_Keywords = new ();
        private List<KeywordEntry> m_PropKeywords = new ();
        private List<EnumKeywordEntry> m_EnumKeywords = new ();
        private List<EnumKeywordEntry> m_PropEnumKeywords = new();

        private static ValInfo.Type ConvertType(Type t)
        {
            if (t == typeof(int))
                return ValInfo.Type.Int;
            else if (t == typeof(uint))
                return ValInfo.Type.Uint;
            else if (t == typeof(bool))
                return ValInfo.Type.BoolAsInt;
            else if (t == typeof(float))
                return ValInfo.Type.Float;
            else if (t == typeof(Vector3))
                return ValInfo.Type.Vector3;
            else if (t == typeof(Vector4))
                return ValInfo.Type.Vector4;
            else if (t == typeof(Vector3Int))
                return ValInfo.Type.Vector3Int;
            else if (t == typeof(float[]))
                return ValInfo.Type.FloatArray;
            else if (t == typeof(Vector4[]))
                return ValInfo.Type.VectorArray;
            else if (t == typeof(Matrix4x4))
                return ValInfo.Type.Matrix;
            else if (t == typeof(Matrix4x4[]))
                return ValInfo.Type.MatrixArray;
            else if (typeof(Texture).IsAssignableFrom(t))
                return ValInfo.Type.Texture;
            else if (t == typeof(ComputeBuffer))
                return ValInfo.Type.ComputeBuffer;
            else if (t == typeof(GraphicsBuffer))
                return ValInfo.Type.GraphicsBuffer;
            else if (t.IsSubclassOf(typeof(TypedBufferBase)))
                return ValInfo.Type.TypedBuffer;
            else
                return ValInfo.Type.Invalid;

        }

        public void Init(Type t)
        {
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            foreach (var field in fields)
            {
                var attrs = field.GetCustomAttributes(typeof(ShaderValueAttribute), true);
                foreach (var _attr in attrs)
                {
                    var attr = _attr as ShaderValueAttribute;

                    string name = field.Name;
                    if (name.StartsWith("m_"))
                        name = name.Remove(0, 2); 
                    if (attr.Target != null)
                        name = attr.Target;

                    var vi = new ValInfo() { type = ConvertType(field.FieldType), shaderID = Shader.PropertyToID(name) };

                    if (vi.type != ValInfo.Type.Invalid)
                        m_Values.Add((field, vi));

                    // Add the RW variant too
                    if (attr.AddRW)
                    {
                        vi.shaderID = Shader.PropertyToID(name + "RW");
                        if (vi.type != ValInfo.Type.Invalid)
                            m_Values.Add((field, vi));
                    }
                }

                attrs = field.GetCustomAttributes(typeof(ShaderKeywordAttribute), true);
                foreach (var _attr in attrs)
                {
                    if (field.FieldType.IsEnum)
                    {
                        // Multi-value keyword, grab the names from the enum values
                        var names = new List<string>();
                        var values = new List<int>();
                        var members = field.FieldType.GetMembers(BindingFlags.Public | BindingFlags.Static);
                        foreach(var m in members)
                        {
                            string name = m.Name;
                            var eAttr = m.GetCustomAttribute<ShaderKeywordAttribute>();
                            if (name.StartsWith("m_"))
                                name = name.Remove(0, 2);
                            if (eAttr != null && eAttr.Target != null)
                                name = eAttr.Target;

                            names.Add(name);
                            values.Add((int)Enum.Parse(field.FieldType, m.Name));
                        }

                        m_EnumKeywords.Add(new EnumKeywordEntry() { fieldInfo = field, propInfo = null, names = names.ToArray(), values = values.ToArray(), keywords = new() });
                    }
                    else
                    {
                        var attr = _attr as ShaderKeywordAttribute;
                        string name = field.Name;
                        if (name.StartsWith("m_"))
                            name = name.Remove(0, 2);
                        if (attr.Target != null)
                            name = attr.Target;

                        if (field.FieldType == typeof(bool))
                        {
                            m_Keywords.Add(new KeywordEntry() { fieldInfo = field, name = name, keywords = new()});
                        }
                    }
                }
            }

            var props = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var prop in props)
            {
                var attrs = prop.GetCustomAttributes(typeof(ShaderValueAttribute), true);
                foreach (var _attr in attrs)
                {
                    var attr = _attr as ShaderValueAttribute;

                    string name = prop.Name;
                    if (name.StartsWith("m_"))
                        name = name.Remove(0, 2);
                    if (attr.Target != null)
                        name = attr.Target;

                    var vi = new ValInfo() { type = ConvertType(prop.PropertyType), shaderID = Shader.PropertyToID(name) };

                    if (vi.type != ValInfo.Type.Invalid)
                        m_PropValues.Add((prop, vi));

                    // Add the RW variant too
                    if (attr.AddRW)
                    {
                        vi.shaderID = Shader.PropertyToID(name + "RW");
                        if (vi.type != ValInfo.Type.Invalid)
                            m_PropValues.Add((prop, vi));
                    }

                }

                attrs = prop.GetCustomAttributes(typeof(ShaderKeywordAttribute), true);
                foreach (var _attr in attrs)
                {
                    if (prop.PropertyType.IsEnum)
                    {
                        // Multi-value keyword, grab the names from the enum values
                        var names = new List<string>();
                        var values = new List<int>();
                        var members = prop.PropertyType.GetMembers(BindingFlags.Public | BindingFlags.Static);
                        foreach (var m in members)
                        {
                            string name = m.Name;
                            var eAttr = m.GetCustomAttribute<ShaderKeywordAttribute>();
                            if (name.StartsWith("m_"))
                                name = name.Remove(0, 2);
                            if (eAttr != null && eAttr.Target != null)
                                name = eAttr.Target;

                            names.Add(name);
                            values.Add((int)Enum.Parse(prop.PropertyType, m.Name));
                        }

                        m_PropEnumKeywords.Add(new EnumKeywordEntry() { fieldInfo = null, propInfo = prop, names = names.ToArray(), values = values.ToArray(), keywords = new() });
                    }
                    else
                    {
                        var attr = _attr as ShaderKeywordAttribute;
                        string name = prop.Name;
                        if (name.StartsWith("m_"))
                            name = name.Remove(0, 2);
                        if (attr.Target != null)
                            name = attr.Target;

                        if (prop.PropertyType == typeof(bool))
                        {
                            m_PropKeywords.Add(new KeywordEntry() { fieldInfo = null, propInfo = prop, name = name, keywords = new() });
                        }
                    }
                }
            }
        }

        private void Apply(Material mat, ValInfo vi, object val)
        {
            switch (vi.type)
            {
                case ValInfo.Type.Int:
                    mat.SetInt(vi.shaderID, (int)val);
                    break;
                case ValInfo.Type.Uint:
                    mat.SetInt(vi.shaderID, (int)(uint)val);
                    break;
                case ValInfo.Type.BoolAsInt:
                    mat.SetInt(vi.shaderID, ((bool)val) ? 1 : 0);
                    break;
                case ValInfo.Type.Float:
                    mat.SetFloat(vi.shaderID, (float)val);
                    break;
                case ValInfo.Type.Vector3:
                    mat.SetVector(vi.shaderID, (Vector3)val);
                    break;
                case ValInfo.Type.Vector4:
                    mat.SetVector(vi.shaderID, (Vector4)val);
                    break;
                case ValInfo.Type.Vector3Int:
                    var iv = (Vector3Int)val;
                    mat.SetVector(vi.shaderID, new Vector4(iv.x, iv.y, iv.z, 0));
                    break;
                case ValInfo.Type.FloatArray:
                    mat.SetFloatArray(vi.shaderID, (float[])val);
                    break;
                case ValInfo.Type.VectorArray:
                    mat.SetVectorArray(vi.shaderID, (Vector4[])val);
                    break;
                case ValInfo.Type.Matrix:
                    mat.SetMatrix(vi.shaderID, (Matrix4x4)val);
                    break;
                case ValInfo.Type.MatrixArray:
                    mat.SetMatrixArray(vi.shaderID, (Matrix4x4[])val);
                    break;
                case ValInfo.Type.Texture:
                    mat.SetTexture(vi.shaderID, (Texture)val);
                    break;
                case ValInfo.Type.ComputeBuffer:
                    mat.SetBuffer(vi.shaderID, (ComputeBuffer)val);
                    break;
                case ValInfo.Type.GraphicsBuffer:
                    mat.SetBuffer(vi.shaderID, (GraphicsBuffer)val);
                    break;
                case ValInfo.Type.TypedBuffer:
                    mat.SetBuffer(vi.shaderID, ((TypedBufferBase)val).Buffer);
                    break;
                case ValInfo.Type.Invalid:
                default:
                    break;
            }
        }
        private void Apply(MaterialPropertyBlock mat, ValInfo vi, object val)
        {
            switch (vi.type)
            {
                case ValInfo.Type.Int:
                    mat.SetInt(vi.shaderID, (int)val);
                    break;
                case ValInfo.Type.Uint:
                    mat.SetInt(vi.shaderID, (int)(uint)val);
                    break;
                case ValInfo.Type.BoolAsInt:
                    mat.SetInt(vi.shaderID, ((bool)val) ? 1 : 0);
                    break;
                case ValInfo.Type.Float:
                    mat.SetFloat(vi.shaderID, (float)val);
                    break;
                case ValInfo.Type.Vector3:
                    mat.SetVector(vi.shaderID, (Vector3)val);
                    break;
                case ValInfo.Type.Vector4:
                    mat.SetVector(vi.shaderID, (Vector4)val);
                    break;
                case ValInfo.Type.Vector3Int:
                    var iv = (Vector3Int)val;
                    mat.SetVector(vi.shaderID, new Vector4(iv.x, iv.y, iv.z, 0));
                    break;
                case ValInfo.Type.FloatArray:
                    mat.SetFloatArray(vi.shaderID, (float[])val);
                    break;
                case ValInfo.Type.VectorArray:
                    mat.SetVectorArray(vi.shaderID, (Vector4[])val);
                    break;
                case ValInfo.Type.Matrix:
                    mat.SetMatrix(vi.shaderID, (Matrix4x4)val);
                    break;
                case ValInfo.Type.MatrixArray:
                    mat.SetMatrixArray(vi.shaderID, (Matrix4x4[])val);
                    break;
                case ValInfo.Type.Texture:
                    mat.SetTexture(vi.shaderID, (Texture)val);
                    break;
                case ValInfo.Type.ComputeBuffer:
                    mat.SetBuffer(vi.shaderID, (ComputeBuffer)val);
                    break;
                case ValInfo.Type.GraphicsBuffer:
                    mat.SetBuffer(vi.shaderID, (GraphicsBuffer)val);
                    break;
                case ValInfo.Type.TypedBuffer:
                    mat.SetBuffer(vi.shaderID, ((TypedBufferBase)val).Buffer);
                    break;
                case ValInfo.Type.Invalid:
                default:
                    break;
            }
        }
        private void Apply(ComputeShader mat, int[] kernelIndices, ValInfo vi, object val)
        {
            switch (vi.type)
            {
                case ValInfo.Type.Int:
                    mat.SetInt(vi.shaderID, (int)val);
                    break;
                case ValInfo.Type.Uint:
                    mat.SetInt(vi.shaderID, (int)(uint)val);
                    break;
                case ValInfo.Type.BoolAsInt:
                    mat.SetInt(vi.shaderID, ((bool)val) ? 1 : 0);
                    break;
                case ValInfo.Type.Float:
                    mat.SetFloat(vi.shaderID, (float)val);
                    break;
                case ValInfo.Type.Vector3:
                    mat.SetVector(vi.shaderID, (Vector3)val);
                    break;
                case ValInfo.Type.Vector4:
                    mat.SetVector(vi.shaderID, (Vector4)val);
                    break;
                case ValInfo.Type.Vector3Int:
                    var iv = (Vector3Int)val;
                    // SetVector doesn't work for ints in computeshader
//                    mat.SetVector(vi.shaderID, new Vector4(iv.x, iv.y, iv.z, 0));
                    mat.SetInts(vi.shaderID, new int[] { iv.x, iv.y, iv.z });
                    break;
                case ValInfo.Type.FloatArray:
                    //                mat.SetFloatArray(vi.shaderID, (float[])val);
                    break;
                case ValInfo.Type.VectorArray:
                    mat.SetVectorArray(vi.shaderID, (Vector4[])val);
                    break;
                case ValInfo.Type.Matrix:
                    mat.SetMatrix(vi.shaderID, (Matrix4x4)val);
                    break;
                case ValInfo.Type.MatrixArray:
                    mat.SetMatrixArray(vi.shaderID, (Matrix4x4[])val);
                    break;
                case ValInfo.Type.Texture:
                    foreach (int k in kernelIndices)
                        mat.SetTexture(k, vi.shaderID, (Texture)val);
                    break;
                case ValInfo.Type.ComputeBuffer:
                    foreach (int k in kernelIndices)
                        mat.SetBuffer(k, vi.shaderID, (ComputeBuffer)val);
                    break;
                case ValInfo.Type.GraphicsBuffer:
                    foreach (int k in kernelIndices)
                        mat.SetBuffer(k, vi.shaderID, (GraphicsBuffer)val);
                    break;
                case ValInfo.Type.TypedBuffer:
                    foreach (int k in kernelIndices)
                        mat.SetBuffer(k, vi.shaderID, ((TypedBufferBase)val).Buffer);
                    break;
                case ValInfo.Type.Invalid:
                default:
                    break;
            }
        }
        private void Apply(ComputeShader mat, CommandBuffer cb, int[] kernelIndices, ValInfo vi, object val)
        {
            switch (vi.type)
            {
                case ValInfo.Type.Int:
                    cb.SetComputeIntParam(mat, vi.shaderID, (int)val);
                    break;
                case ValInfo.Type.Uint:
                    cb.SetComputeIntParam(mat, vi.shaderID, (int)(uint)val);
                    break;
                case ValInfo.Type.BoolAsInt:
                    cb.SetComputeIntParam(mat, vi.shaderID, ((bool)val) ? 1 : 0);
                    break;
                case ValInfo.Type.Float:
                    cb.SetComputeFloatParam(mat, vi.shaderID, (float)val);
                    break;
                case ValInfo.Type.Vector3:
                    cb.SetComputeVectorParam(mat, vi.shaderID, (Vector3)val);
                    break;
                case ValInfo.Type.Vector4:
                    cb.SetComputeVectorParam(mat, vi.shaderID, (Vector4)val);
                    break;
                case ValInfo.Type.Vector3Int:
                    var iv = (Vector3Int)val;
                    // SetVector doesn't work for ints in computeshader
                    //                    mat.SetVector(vi.shaderID, new Vector4(iv.x, iv.y, iv.z, 0));
                    cb.SetComputeIntParams(mat, vi.shaderID, new int[] { iv.x, iv.y, iv.z });
                    break;
                case ValInfo.Type.FloatArray:
                    //                mat.SetFloatArray(vi.shaderID, (float[])val);
                    break;
                case ValInfo.Type.VectorArray:
                    cb.SetComputeVectorArrayParam(mat, vi.shaderID, (Vector4[])val);
                    break;
                case ValInfo.Type.Matrix:
                    cb.SetComputeMatrixParam(mat, vi.shaderID, (Matrix4x4)val);
                    break;
                case ValInfo.Type.MatrixArray:
                    cb.SetComputeMatrixArrayParam(mat, vi.shaderID, (Matrix4x4[])val);
                    break;
                case ValInfo.Type.Texture:
                    foreach (int k in kernelIndices)
                        cb.SetComputeTextureParam(mat, k, vi.shaderID, (Texture)val);
                    break;
                case ValInfo.Type.ComputeBuffer:
                    foreach (int k in kernelIndices)
                        cb.SetComputeBufferParam(mat, k, vi.shaderID, (ComputeBuffer)val);
                    break;
                case ValInfo.Type.GraphicsBuffer:
                    foreach (int k in kernelIndices)
                        cb.SetComputeBufferParam(mat, k, vi.shaderID, (GraphicsBuffer)val);
                    break;
                case ValInfo.Type.TypedBuffer:
                    foreach (int k in kernelIndices)
                        cb.SetComputeBufferParam(mat, k, vi.shaderID, ((TypedBufferBase)val).Buffer);
                    break;
                case ValInfo.Type.Invalid:
                default:
                    break;
            }
        }

        public void Apply(object parent, Material mat)
        {
            foreach ((FieldInfo f, ValInfo vi) in m_Values)
            {
                Apply(mat, vi, f.GetValue(parent));
            }
            foreach ((PropertyInfo p, ValInfo vi) in m_PropValues)
            {
                Apply(mat, vi, p.GetValue(parent));
            }

            foreach (var k in m_Keywords)
            {
                var kw = k.GetLocalKeyword(mat);
                if (!kw.isValid) continue;
                var isEnabled = (bool)k.fieldInfo.GetValue(parent);
                if (isEnabled)
                    mat.EnableKeyword(kw);
                else
                    mat.DisableKeyword(kw);
            }
            foreach (var k in m_PropKeywords)
            {
                var kw = k.GetLocalKeyword(mat);
                if (!kw.isValid) continue;
                var isEnabled = (bool)k.propInfo.GetValue(parent);
                if (isEnabled)
                    mat.EnableKeyword(kw);
                else
                    mat.DisableKeyword(kw);
            }
            foreach (var k in m_EnumKeywords)
            {
                var kws = k.GetLocalKeywords(mat);
                var enabledIdx = (int)k.fieldInfo.GetValue(parent);
                for(int i = 0; i < k.names.Length; i++)
                {
                    if (!kws[i].isValid) continue;
                    if (k.values[i] == enabledIdx)
                        mat.EnableKeyword(kws[i]);
                    else
                        mat.DisableKeyword(kws[i]);
                }
            }
            foreach (var k in m_PropEnumKeywords)
            {
                var kws = k.GetLocalKeywords(mat);
                var enabledIdx = (int)k.propInfo.GetValue(parent);
                for (int i = 0; i < k.names.Length; i++)
                {
                    if (!kws[i].isValid) continue;
                    if (k.values[i] == enabledIdx)
                        mat.EnableKeyword(kws[i]);
                    else
                        mat.DisableKeyword(kws[i]);
                }
            }
        }

        public void Apply(object parent, MaterialPropertyBlock mat)
        {
            foreach ((FieldInfo f, ValInfo vi) in m_Values)
            {
                Apply(mat, vi, f.GetValue(parent));
            }
            foreach ((PropertyInfo p, ValInfo vi) in m_PropValues)
            {
                Apply(mat, vi, p.GetValue(parent));
            }
        }

        public void Apply(object parent, ComputeShader mat, params int[] kernelIndices)
        {
            foreach ((FieldInfo f, ValInfo vi) in m_Values)
            {
                Apply(mat, kernelIndices, vi, f.GetValue(parent));
            }
            foreach ((PropertyInfo p, ValInfo vi) in m_PropValues)
            {
                Apply(mat, kernelIndices, vi, p.GetValue(parent));
            }

            foreach (var k in m_Keywords)
            {
                var kw = k.GetLocalKeyword(mat);
                if (!kw.isValid) continue;
                var isEnabled = (bool)k.fieldInfo.GetValue(parent);
                if (isEnabled)
                    mat.EnableKeyword(kw);
                else
                    mat.DisableKeyword(kw);
            }
            foreach (var k in m_PropKeywords)
            {
                var kw = k.GetLocalKeyword(mat);
                if (!kw.isValid) continue;
                var isEnabled = (bool)k.propInfo.GetValue(parent);
                if (isEnabled)
                    mat.EnableKeyword(kw);
                else
                    mat.DisableKeyword(kw);
            }
            foreach (var k in m_EnumKeywords)
            {
                var kws = k.GetLocalKeywords(mat);
                var enabledIdx = (int)k.fieldInfo.GetValue(parent);
                for (int i = 0; i < k.names.Length; i++)
                {
                    if (!kws[i].isValid) continue;
                    if (k.values[i] == enabledIdx)
                        mat.EnableKeyword(kws[i]);
                    else
                        mat.DisableKeyword(kws[i]);
                }
            }
            foreach (var k in m_PropEnumKeywords)
            {
                var kws = k.GetLocalKeywords(mat);
                var enabledIdx = (int)k.propInfo.GetValue(parent);
                for (int i = 0; i < k.names.Length; i++)
                {
                    if (!kws[i].isValid) continue;
                    if (k.values[i] == enabledIdx)
                        mat.EnableKeyword(kws[i]);
                    else
                        mat.DisableKeyword(kws[i]);
                }
            }

        }

        public void Apply(object parent, ComputeShader mat, CommandBuffer cb, params int[] kernelIndices)
        {
            foreach ((FieldInfo f, ValInfo vi) in m_Values)
            {
                Apply(mat, cb, kernelIndices, vi, f.GetValue(parent));
            }
            foreach ((PropertyInfo p, ValInfo vi) in m_PropValues)
            {
                Apply(mat, cb, kernelIndices, vi, p.GetValue(parent));
            }

            foreach (var k in m_Keywords)
            {
                var kw = k.GetLocalKeyword(mat);
                var isEnabled = (bool)k.fieldInfo.GetValue(parent);
                if (isEnabled)
                    cb.EnableKeyword(mat, kw);
                else
                    cb.DisableKeyword(mat, kw);
            }
            foreach (var k in m_PropKeywords)
            {
                var kw = k.GetLocalKeyword(mat);
                var isEnabled = (bool)k.propInfo.GetValue(parent);
                if (isEnabled)
                    cb.EnableKeyword(mat, kw);
                else
                    cb.DisableKeyword(mat, kw);
            }
            foreach (var k in m_EnumKeywords)
            {
                var kws = k.GetLocalKeywords(mat);
                var enabledIdx = (int)k.fieldInfo.GetValue(parent);
                for (int i = 0; i < k.names.Length; i++)
                {
                    if (k.values[i] == enabledIdx)
                        cb.EnableKeyword(mat, kws[i]);
                    else
                        cb.DisableKeyword(mat, kws[i]);
                }
            }
            foreach (var k in m_PropEnumKeywords)
            {
                var kws = k.GetLocalKeywords(mat);
                var enabledIdx = (int)k.propInfo.GetValue(parent);
                for (int i = 0; i < k.names.Length; i++)
                {
                    if (k.values[i] == enabledIdx)
                        cb.EnableKeyword(mat, kws[i]);
                    else
                        cb.DisableKeyword(mat, kws[i]);
                }
            }

        }

    }

    public static class ShaderBinderExtensions
    {
        private static Dictionary<Type, ShaderBinder> m_Binders = new Dictionary<Type, ShaderBinder>();

        private static ShaderBinder FindOrCreateBinder(Type t)
        {
            if (m_Binders.TryGetValue(t, out var res))
                return res;

            res = new ShaderBinder();
            res.Init(t);
            m_Binders.Add(t, res);
            return res;
        }

        public static void ApplyShaderProps<T>(this T me, Material mat)
        {
            var b = FindOrCreateBinder(typeof(T));
            b.Apply(me, mat);
        }
        public static void ApplyShaderProps<T>(this T me, MaterialPropertyBlock mat)
        {
            var b = FindOrCreateBinder(typeof(T));
            b.Apply(me, mat);
        }
        public static void ApplyShaderProps<T>(this T me, ComputeShader mat, params int[] kernelIndices)
        {
            var b = FindOrCreateBinder(typeof(T));
            b.Apply(me, mat, kernelIndices);
        }
        public static void ApplyShaderProps<T>(this T me, ComputeShader mat, CommandBuffer cb, params int[] kernelIndices)
        {
            var b = FindOrCreateBinder(typeof(T));
            b.Apply(me, mat, cb, kernelIndices);
        }

        public static void ConnectKernels<T>(this T me, ComputeShader cs)
        {
            var kernels = me.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(fi => fi.FieldType == typeof(ComputeKernel));
            foreach(var k in kernels)
            {
                var kernel = (ComputeKernel)k.GetValue(me);
                if(kernel.m_Name == null)
                {
                    string name = null;
                    var attr = k.GetCustomAttribute<ShaderKernelAttribute>();
                    if (attr != null && attr.Target != null)
                    {
                        name = attr.Target;
                    }
                    if(name == null)
                    {
                        name = k.Name;
                        if (name.StartsWith("m_"))
                            name = name[2..];
                    }
                    kernel.m_Name = name;
                }

                kernel.Connect(cs);
                k.SetValue(me, kernel);
            }
        }
        public static void ConnectKernels<T>(this T me)
        {
            var shaders = me.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(fi => fi.FieldType == typeof(ComputeShader));

            if(shaders.Count() != 1)
            {
                Debug.LogError("ConnectKernels called with no (or multiple) ComputeShader fields in the calling class!");
            }
            var cs = shaders.First().GetValue(me) as ComputeShader;
            ConnectKernels(me, cs);

        }
    }

}