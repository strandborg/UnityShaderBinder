/*
 * C#-to-HLSL shader generator, originally from https://github.com/Unity-Technologies/Graphics/tree/master/Packages/com.unity.render-pipelines.core/Editor/ShaderGenerator
 * Licensed with Unity Companion License
 * 
 * */

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.CompilerServices;
using UnityEditor;
using Varjo.ShaderBinder;

namespace Varjo.ShaderBinding.Editor
{
    internal class ShaderKeywordGenerator
    {
        public FieldInfo field = null;
        public ShaderKeywordAttribute attribute = null;

        private string output;
        private List<string> errors;

        public ShaderKeywordGenerator(FieldInfo field, ShaderKeywordAttribute attr)
        {
            this.field = field;
            this.attribute = attr;
        }

        public string Emit()
        {
            return output;
        }
        public void PrintErrors()
        {
            if (errors != null)
            {
                foreach (var e in errors)
                {
                    Debug.LogError(e);
                }

            }
        }

        public bool Generate()
        {
            string mult = attribute.IsLocal ? "multi_compile_local" : "multi_compile";
            if (field.FieldType.IsEnum)
            {
                // Multi-value keyword, grab the names from the enum values
                output = "#pragma " + mult;
//                var names = new List<string>();
//                var values = new List<int>();
                var members = field.FieldType.GetMembers(BindingFlags.Public | BindingFlags.Static);
                foreach (var m in members)
                {
                    string name = m.Name;
                    var eAttr = m.GetCustomAttribute<ShaderKeywordAttribute>();
                    if (name.StartsWith("m_"))
                        name = name.Remove(0, 2);
                    if (eAttr != null && eAttr.Target != null)
                        name = eAttr.Target;

                    output += " " + name;
//                    names.Add(name);
//                    values.Add((int)Enum.Parse(field.FieldType, m.Name));
                }
                output += "\n";
            }
            else
            {
                string name = "";
                if (field != null)
                {
                    name = field.Name;
                }

                if (name.StartsWith("m_"))
                    name = name.Remove(0, 2);

                if (attribute.Target != null)
                    name = attribute.Target;
                output = $"#pragma {mult} __ {name}\n";

            }
            return true;
        }
    }

    internal class ShaderKernelGenerator
    {
        public FieldInfo field = null;
        public ShaderKernelAttribute attribute = null;

        private string output;
        private List<string> errors;

        public ShaderKernelGenerator(FieldInfo field, ShaderKernelAttribute attr)
        {
            this.field = field;
            this.attribute = attr;
        }

        public string Emit()
        {
            return output;
        }
        public void PrintErrors()
        {
            if (errors != null)
            {
                foreach (var e in errors)
                {
                    Debug.LogError(e);
                }

            }
        }

        public bool Generate()
        {
            string name = "";
            if (field != null)
            {
                name = field.Name;
            }

            if (name.StartsWith("m_"))
                name = name.Remove(0, 2);

            if (attribute.Target != null)
                name = attribute.Target;

            output = $"#pragma kernel {name}\n";
            return true;
        }
    }

    internal class ShaderBindingGenerator
    {
        public FieldInfo field = null;
        public PropertyInfo property = null;
        public ShaderValueAttribute attribute = null;

        private string output;
        private List<string> errors;

        public ShaderBindingGenerator(FieldInfo field, ShaderValueAttribute attr)
        {
            this.field = field;
            this.attribute = attr;
        }

        public ShaderBindingGenerator(PropertyInfo property, ShaderValueAttribute attr)
        {
            this.property = property;
            this.attribute = attr;
        }

        private string TypePrefix(Type t, ShaderValueAttribute attr)
        {
            if(attr != null && attr.HLSLType != null)
                return attr.HLSLType;

            bool isHalf = false;
            if (attr != null && attr.IsHalf)
                isHalf = true;

            if (t == typeof(int))
                return "int";
            else if (t == typeof(uint))
                return "uint";
            else if (t == typeof(bool))
                return "int";
            else if (t == typeof(float))
                return isHalf ? "half" : "float";
            else if (t == typeof(Vector2))
                return isHalf ? "half2" : "float2";
            else if (t == typeof(Vector3))
                return isHalf ? "half3" : "float3";
            else if (t == typeof(Vector4))
                return isHalf ? "half4" : "float4";
            else if (t == typeof(Vector3Int))
                return "uint3";
            else if (t == typeof(Vector2Int))
                return "uint2";
            else if (t == typeof(float[]))
                return "float";
            else if (t == typeof(Vector4[]))
                return isHalf ? "half4" : "float4";
            else if (t == typeof(Matrix4x4))
                return isHalf ? "half4x4" : "float4x4";
            else if (t == typeof(Matrix4x4[]))
                return isHalf ? "half4x4" : "float4x4";
            else if (typeof(Texture).IsAssignableFrom(t))
            {
                if (attr == null || attr.InnerType == null)
                    return "Texture2D<float4>";
                else
                {
                    string res = attr.IsUAV ? "RW" : "";
                    res += "Texture" + attr.TexDimension + "D";
                    if (attr.IsTexArray)
                        res += "Array";
                    return res + "<" + TypePrefix(attr.InnerType, attr) + ">";
                }
            }
            else if (t == typeof(ComputeBuffer) || t == typeof(GraphicsBuffer))
            {
                if(attr.InnerType == null)
                {
                    Error("Buffer inner type not specified for " + t.Name);
                    return "";
                }
                if (attr.IsUAV)
                    return "RWStructuredBuffer<" + TypePrefix(attr.InnerType, attr) + ">";
                else
                    return "StructuredBuffer<" + TypePrefix(attr.InnerType, attr) + ">";
            }
            else if(t.IsGenericType && t.GetGenericTypeDefinition() == typeof(StructuredBuffer<>))
            {
                var innerType = t.GenericTypeArguments[0];
                return "StructuredBuffer<"+ TypePrefix(innerType, attr) + ">";
            }
            else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(RWStructuredBuffer<>))
            {
                var innerType = t.GenericTypeArguments[0];
                if(!attr.IsUAV && attr.AddRW)
                    return "StructuredBuffer<" + TypePrefix(innerType, attr) + ">";
                else
                    return "RWStructuredBuffer<" + TypePrefix(innerType, attr) + ">";
            }
            else if (t.GetCustomAttributes(typeof(GenerateHLSL), false).Length > 0)
            {
                return t.Name;
            }
            else
            {
                Error("Unrecognized ShaderValue data type "+ t.Name);
                return "";
            }
        }

        private string TypePostfix(Type t, ShaderValueAttribute attr)
        {
            if (!t.IsArray)
                return "";
            if(attr.ArraySize == 0)
            {
                Error("Array size not specified for " + t.Name);
                return "";
            }
            return "["+attr.ArraySize+"]";
        }

        public bool Generate()
        {
            string name;
            Type t;
            if (field != null)
            {
                name = field.Name;
                t = field.FieldType;
            }
            else
            {
                name = property.Name;
                t = property.PropertyType;
            }

            if (name.StartsWith("m_"))
                name = name.Remove(0, 2);

            if (attribute.Target != null)
                name = attribute.Target;

            output = "";
            if (attribute.GloballyCoherent && attribute.IsUAV)
                output += "globallycoherent ";
            output += TypePrefix(t, attribute) + " " + name + TypePostfix(t, attribute) + ";\n";

            if(attribute.AddRW)
            {
                attribute.IsUAV = true;
                name += "RW";
                if (attribute.GloballyCoherent && attribute.IsUAV)
                    output += "globallycoherent ";
                output += TypePrefix(t, attribute) + " " + name + TypePostfix(t, attribute) + ";\n";

            }


            return errors == null;
        }

        public string Emit()
        {
            return output;
        }

        void Error(string error)
        {
            if (errors == null)
            {
                errors = new List<string>();
            }

            errors.Add("Failed to generate shader type for " + (field == null ? property.ToString() : field.ToString()) + ": " + error);
        }

        public void PrintErrors()
        {
            if (errors != null)
            {
                foreach (var e in errors)
                {
                    Debug.LogError(e);
                }

            }
        }
    }

    internal class ShaderTypeGenerator
    {
        public ShaderTypeGenerator(Type type, GenerateHLSL attr)
        {
            this.type = type;
            this.attr = attr;
            debugCounter = 0;
        }

        enum PrimitiveType
        {
            Float, Int, UInt, Bool, Half, Real, Struct // Okay struct is not really a primitive but just roll with it
        };

        static string PrimitiveToString(PrimitiveType type, string typeName, int rows, int cols)
        {
            string text = "";
            switch (type)
            {
                case PrimitiveType.Float:
                    text = "float";
                    break;
                case PrimitiveType.Int:
                    text = "int";
                    break;
                case PrimitiveType.UInt:
                    text = "uint";
                    break;
                case PrimitiveType.Bool:
                    text = "bool";
                    break;
                case PrimitiveType.Half:
                    text = "half";
                    break;
                case PrimitiveType.Real:
                    text = "real";
                    break;
                case PrimitiveType.Struct:
                    return typeName;
            }

            if (rows > 1)
            {
                text += rows.ToString();
                if (cols > 1)
                {
                    text += "x" + cols.ToString();
                }
            }

            return text;
        }

        class Accessor
        {
            public Accessor(PrimitiveType type, string name, string typeName, int rows, int cols)
            {
                this.name = name;
                this.fullType = PrimitiveToString(type, typeName, rows, cols);
                field = name;
            }

/*            Accessor(string name, string swizzle, string field, string fullType)
            {
                this.name = name;
                this.field = field;
                this.fullType = fullType;
            }*/

            public string name;
            public string field;
            public string fullType;
        };

        class ShaderFieldInfo : ICloneable
        {
            public ShaderFieldInfo(PrimitiveType type, string name, string typeName, int rows, int cols, int arraySize, string preprocessor)
            {
                this.type = type;
                this.name = originalName = name;
                this.rows = rows;
                this.cols = cols;
                this.arraySize = arraySize;
                this.comment = "";
                this.preprocessor = preprocessor;
                this.typeName = typeName;
                swizzleOffset = 0;
                packed = false;
                accessor = new Accessor(type, name, typeName, rows, cols);
            }

            public ShaderFieldInfo(PrimitiveType type, string name, string typeName, int rows, int cols, int arraySize, string comment, string preprocessor)
            {
                this.type = type;
                this.name = originalName = name;
                this.rows = rows;
                this.cols = cols;
                this.arraySize = arraySize;
                this.comment = comment;
                this.preprocessor = preprocessor;
                this.typeName = typeName;
                swizzleOffset = 0;
                packed = false;
                accessor = new Accessor(type, name, typeName, rows, cols);
            }

            public string typeString
            {
                get { return PrimitiveToString(type, typeName, rows, cols); }
            }

            public string DeclString()
            {
                string arrayText = (arraySize > 0) ? "[" + arraySize + "]" : "";
                return PrimitiveToString(type, typeName, rows, cols) + " " + name + arrayText;
            }

            public override string ToString()
            {
                string text = "";

                if (preprocessor.Length > 0)
                {
                    text += "#if " + preprocessor + "\n" + "    ";
                }

                text += DeclString() + ";";
                if (comment.Length > 0)
                {
                    text += " // " + comment;
                }

                if (preprocessor.Length > 0)
                {
                    text += "\n    #endif";
                }
                return text;
            }

            public int elementCount
            {
                get { return rows * cols * Mathf.Max(arraySize, 1); }
            }

            public object Clone()
            {
                ShaderFieldInfo info = new ShaderFieldInfo(type, name, typeName, rows, cols, arraySize, comment, preprocessor);
                info.swizzleOffset = swizzleOffset;
                info.packed = packed;
                info.accessor = accessor;
                return info;
            }

            public readonly PrimitiveType type;
            public readonly string typeName;
            public string name;
            public readonly string originalName;
            public readonly string comment;
            public readonly string preprocessor;
            public int rows;
            public int cols;
            public int arraySize;
            public int swizzleOffset;
            public bool packed;
            public Accessor accessor;
        };

        class DebugFieldInfo
        {
            public DebugFieldInfo(string defineName, string fieldName, Type fieldType, bool isDirection, bool isSRGB, bool checkIsNormalized, string displayName = "", string preprocessor = "")
            {
                this.defineName = defineName;
                this.fieldName = fieldName;
                this.fieldType = fieldType;
                this.isDirection = isDirection;
                this.isSRGB = isSRGB;
                this.displayName = displayName;
                this.checkIsNormalized = checkIsNormalized;
                this.preprocessor = preprocessor;
            }

            public string defineName;
            public string fieldName;
            public string displayName;
            public Type fieldType;
            public bool isDirection;
            public bool isSRGB;
            public bool checkIsNormalized;
            public string preprocessor;
        }

        class PackedFieldInfo
        {
            public PackedFieldInfo(PackingAttribute packingAttribute, Type fieldType, string fieldName)
            {
                this.packingAttribute = packingAttribute;
                this.fieldType = fieldType;
                this.fieldName = fieldName;
            }

            public PackingAttribute packingAttribute;
            public Type fieldType;
            public string fieldName;
        }

        void Error(string error)
        {
            if (errors == null)
            {
                errors = new List<string>();
            }
            errors.Add("Failed to generate shader type for " + type.ToString() + ": " + error);
        }

        public void PrintErrors()
        {
            if (errors != null)
            {
                foreach (var e in errors)
                {
                    Debug.LogError(e);
                }
            }
        }

        void EmitPrimitiveType(PrimitiveType type, string typeName, int elements, int arraySize, string name, string comment, string preprocessor, List<ShaderFieldInfo> fields)
        {
            fields.Add(new ShaderFieldInfo(type, name, typeName, elements, 1, arraySize, comment, preprocessor));
        }

        void EmitMatrixType(PrimitiveType type, int rows, int cols, int arraySize, string name, string comment, string preprocessor, List<ShaderFieldInfo> fields)
        {
            fields.Add(new ShaderFieldInfo(type, name, "", rows, cols, arraySize, comment, preprocessor));
        }

        bool ExtractComplex(FieldInfo field, string preprocessor, List<ShaderFieldInfo> shaderFields)
        {
            var floatFields = new List<FieldInfo>();
            var intFields = new List<FieldInfo>();
            var uintFields = new List<FieldInfo>();
            var boolFields = new List<FieldInfo>();
            var descs = new string[4] { "x: ", "y: ", "z: ", "w: " };
            int numFields = 0;

            // Check if this is a nested struct that's also HLSL-ized
            if(field.FieldType.GetCustomAttribute<GenerateHLSL>() != null)
            {
                EmitPrimitiveType(PrimitiveType.Struct, field.FieldType.Name, 1, 0, field.Name, "", preprocessor, shaderFields);
                return true;
            }

            string fieldName = "'" + field.FieldType.Name + " " + field.Name + "'";

            foreach (var subField in field.FieldType.GetFields())
            {
                if (subField.IsStatic)
                    continue;

                if (!subField.FieldType.IsPrimitive)
                {
                    Error("'" + fieldName + "' can not be packed into a register, since it contains a non-primitive field type '" + subField.FieldType + "'");
                    return false;
                }
                if (subField.FieldType == typeof(float))
                    floatFields.Add(subField);
                else if (subField.FieldType == typeof(int))
                    intFields.Add(subField);
                else if (subField.FieldType == typeof(uint))
                    uintFields.Add(subField);
                else if (subField.FieldType == typeof(bool))
                    boolFields.Add(subField);
                else
                {
                    Error("'" + fieldName + "' can not be packed into a register, since it contains an unsupported field type '" + subField.FieldType + "'");
                    return false;
                }

                if (numFields == 4)
                {
                    Error("'" + fieldName + "' can not be packed into a register because it contains more than 4 fields.");
                    return false;
                }

                descs[numFields] += subField.Name + " ";
                numFields++;
            }
            Array.Resize(ref descs, numFields);

            string comment = string.Concat(descs);
            string mismatchErrorMsg = "'" + fieldName + "' can not be packed into a single register because it contains mixed basic types.";

            if (floatFields.Count > 0)
            {
                if (intFields.Count + uintFields.Count + boolFields.Count > 0)
                {
                    Error(mismatchErrorMsg);
                    return false;
                }
                EmitPrimitiveType(PrimitiveType.Float, "", floatFields.Count, 0, field.Name, comment, preprocessor, shaderFields);
            }
            else if (intFields.Count > 0)
            {
                if (floatFields.Count + uintFields.Count + boolFields.Count > 0)
                {
                    Error(mismatchErrorMsg);
                    return false;
                }
                EmitPrimitiveType(PrimitiveType.Int, "", intFields.Count, 0, field.Name, "", preprocessor, shaderFields);
            }
            else if (uintFields.Count > 0)
            {
                if (floatFields.Count + intFields.Count + boolFields.Count > 0)
                {
                    Error(mismatchErrorMsg);
                    return false;
                }
                EmitPrimitiveType(PrimitiveType.UInt, "", uintFields.Count, 0, field.Name, "", preprocessor, shaderFields);
            }
            else if (boolFields.Count > 0)
            {
                if (floatFields.Count + intFields.Count + uintFields.Count > 0)
                {
                    Error(mismatchErrorMsg);
                    return false;
                }
                EmitPrimitiveType(PrimitiveType.Bool, "", boolFields.Count, 0, field.Name, "", preprocessor, shaderFields);
            }
            else
            {
                // Empty struct.
            }

            return true;
        }

        enum MergeResult
        {
            Merged,
            Full,
            Failed
        };

        MergeResult PackFields(ShaderFieldInfo info, ref ShaderFieldInfo merged)
        {
            if (merged.elementCount % 4 == 0)
            {
                return MergeResult.Full;
            }

            if (info.type != merged.type)
            {
                Error("can't merge '" + merged.DeclString() + "' and '" + info.DeclString() + "' into the same register because they have incompatible types.  Consider reordering the fields so that adjacent fields have the same primitive type.");
                return MergeResult.Failed;  // incompatible types
            }

            if (info.cols > 1 || merged.cols > 1)
            {
                Error("merging matrix types not yet supported ('" + merged.DeclString() + "' and '" + info.DeclString() + "').  Consider reordering the fields to place matrix-typed variables on four-component vector boundaries.");
                return MergeResult.Failed;  // don't merge matrix types
            }

            if (info.rows + merged.rows > 4)
            {
                // @TODO:  lift the restriction
                Error("can't merge '" + merged.DeclString() + "' and '" + info.DeclString() + "' because then " + info.name + " would cross register boundary.  Consider reordering the fields so that none of them cross four-component vector boundaries when packed.");
                return MergeResult.Failed;  // out of space
            }

            merged.rows += info.rows;
            merged.name += "_" + info.name;
            return MergeResult.Merged;
        }

        List<ShaderFieldInfo> Pack(List<ShaderFieldInfo> shaderFields)
        {
            List<ShaderFieldInfo> mergedFields = new List<ShaderFieldInfo>();

            using (var e = shaderFields.GetEnumerator())
            {
                if (!e.MoveNext())
                {
                    // Empty shader struct definition.
                    return shaderFields;
                }

                ShaderFieldInfo current = e.Current.Clone() as ShaderFieldInfo;

                while (e.MoveNext())
                {
                    while (true)
                    {
                        int offset = current.elementCount;
                        var result = PackFields(e.Current, ref current);

                        if (result == MergeResult.Failed)
                        {
                            return null;
                        }
                        else if (result == MergeResult.Full)
                        {
                            break;
                        }

                        // merge accessors
                        var acc = current.accessor;

                        acc.name = current.name;
                        e.Current.accessor = acc;
                        e.Current.swizzleOffset += offset;

                        current.packed = e.Current.packed = true;

                        if (!e.MoveNext())
                        {
                            mergedFields.Add(current);
                            return mergedFields;
                        }
                    }
                    mergedFields.Add(current);
                    current = e.Current.Clone() as ShaderFieldInfo;
                }
            }
            return mergedFields;
        }

        public string EmitTypeDecl()
        {
            string shaderText = string.Empty;

            shaderText += "// Generated from " + type.FullName + "\n";
            shaderText += "// PackingRules = " + attr.packingRules.ToString() + "\n";

            if (attr.generateCBuffer)
            {
                if (attr.constantRegister != -1)
                    shaderText += "GLOBAL_CBUFFER_START(" + type.Name + ", b" + attr.constantRegister + ")\n";
                else
                    shaderText += "CBUFFER_START(" + type.Name + ")\n";
            }
            else if (!attr.omitStructDeclaration)
            {
                shaderText += "struct " + type.Name + "\n";
                shaderText += "{\n";
            }

            foreach (var shaderFieldInfo in m_PackedFields)
            {
                shaderText += "    " + shaderFieldInfo.ToString() + "\n";
            }

            if (attr.generateCBuffer)
            {
                shaderText += "CBUFFER_END\n";
            }
            else if (!attr.omitStructDeclaration)
            {
                shaderText += "};\n";
            }

            return shaderText;
        }

        public string EmitSetters(bool generatingInitFunc = false)
        {
            string shaderText = string.Empty;

            if (!hasPackedInfo && attr.needSetters)
            {
                shaderText += "//\n";
                shaderText += "// Setters for " + type.FullName + "\n";
                shaderText += "//\n";
                foreach (var shaderField in m_ShaderFields)
                {
                    Accessor acc = shaderField.accessor;
                    string setterName = shaderField.originalName;
                    string setterOrInit = generatingInitFunc ? "Init" : "Set";
                    setterName = setterOrInit + char.ToUpper(setterName[0]) + setterName.Substring(1);

                    if (shaderField.arraySize > 0)
                        shaderText += "void " + setterName + "(int index, " + shaderField.typeString + " newValue, inout " + type.Name + " dest )\n";
                    else
                        shaderText += "void " + setterName + "(" + shaderField.typeString + " newValue, inout " + type.Name + " dest )\n";
                    shaderText += "{\n";

                    string arrayAccess = "";

                    if (shaderField.arraySize > 0)
                    {
                        arrayAccess = "[index]";
                    }

                    shaderText +=
                        //"\t"
                        "    "         // unity convention use space instead of tab...
                        + "dest." + acc.name + arrayAccess + " = newValue;\n";
                    shaderText += "}\n";
                }
            }

            return shaderText;
        }

        public string EmitAccessors()
        {
            string shaderText = string.Empty;

            if (!hasPackedInfo)
            {
                shaderText += "//\n";
                shaderText += "// Accessors for " + type.FullName + "\n";
                shaderText += "//\n";
                foreach (var shaderField in m_ShaderFields)
                {
                    Accessor acc = shaderField.accessor;
                    string accessorName = shaderField.originalName;
                    accessorName = "Get" + char.ToUpper(accessorName[0]) + accessorName.Substring(1);

                    if (shaderField.arraySize > 0)
                        shaderText += shaderField.typeString + " " + accessorName + "(" + type.Name + " value, int index)\n";
                    else
                        shaderText += shaderField.typeString + " " + accessorName + "(" + type.Name + " value)\n";
                    shaderText += "{\n";

                    string swizzle = "";
                    string arrayAccess = "";

                    // @TODO:  support matrix type packing?
                    if (shaderField.cols == 1) // @TEMP
                    {
                        // don't emit redundant swizzles
                        if (shaderField.originalName != acc.name)
                        {
                            swizzle = "." + "xyzw".Substring(shaderField.swizzleOffset, shaderField.elementCount);
                        }
                    }

                    if (shaderField.arraySize > 0)
                    {
                        arrayAccess = "[index]";
                    }

                    shaderText +=
                        //"\t"
                        "    "         // unity convention use space instead of tab...
                        + "return value." + acc.name + swizzle + arrayAccess + ";\n";
                    shaderText += "}\n";
                }
            }

            return shaderText;
        }

        public string EmitDefines()
        {
            string shaderText = string.Empty;

            shaderText += "//\n";
            shaderText += "// " + type.FullName + ":  static fields\n";
            shaderText += "//\n";
            foreach (var def in m_Statics)
            {
                shaderText += "#define " + def.Key + " (" + def.Value + ")\n";
            }

            return shaderText;
        }

        public string EmitFunctions()
        {
            string shaderText = string.Empty;

            // In case users ask for debug functions
            if (!attr.needParamDebug)
                return shaderText;

            // Specific to HDRenderPipeline
            string lowerStructName = type.Name.ToLower();

            shaderText += "//\n";
            shaderText += "// Debug functions\n";
            shaderText += "//\n";

            shaderText += "void GetGenerated" + type.Name + "Debug(uint paramId, " + type.Name + " " + lowerStructName + ", inout float3 result, inout bool needLinearToSRGB)\n";
            shaderText += "{\n";
            shaderText += "    switch (paramId)\n";
            shaderText += "    {\n";

            foreach (var debugField in m_DebugFields)
            {
                if (debugField.preprocessor.Length > 0)
                {
                    shaderText += "#if " + debugField.preprocessor + "\n";
                }

                shaderText += "        case " + debugField.defineName + ":\n";
                if (debugField.fieldType == typeof(float))
                {
                    if (debugField.isDirection)
                    {
                        shaderText += "            result = " + lowerStructName + "." + debugField.fieldName + ".xxx * 0.5 + 0.5;\n";
                    }
                    else
                    {
                        shaderText += "            result = " + lowerStructName + "." + debugField.fieldName + ".xxx;\n";
                    }
                }
                else if (debugField.fieldType == typeof(Vector2))
                {
                    shaderText += "            result = float3(" + lowerStructName + "." + debugField.fieldName + ", 0.0);\n";
                }
                else if (debugField.fieldType == typeof(Vector3))
                {
                    if (debugField.isDirection)
                    {
                        if (debugField.checkIsNormalized)
                        {
                            shaderText += "            result = IsNormalized(" + lowerStructName + "." + debugField.fieldName + ")? " + lowerStructName + "." + debugField.fieldName + " * 0.5 + 0.5 : float3(1.0, 0.0, 0.0);\n";
                        }
                        else
                        {
                            shaderText += "            result = " + lowerStructName + "." + debugField.fieldName + " * 0.5 + 0.5;\n";
                        }
                    }
                    else
                    {
                        shaderText += "            result = " + lowerStructName + "." + debugField.fieldName + ";\n";
                    }
                }
                else if (debugField.fieldType == typeof(Vector4))
                {
                    shaderText += "            result = " + lowerStructName + "." + debugField.fieldName + ".xyz;\n";
                }
                else if (debugField.fieldType == typeof(bool))
                {
                    shaderText += "            result = (" + lowerStructName + "." + debugField.fieldName + ") ? float3(1.0, 1.0, 1.0) : float3(0.0, 0.0, 0.0);\n";
                }
                else if (debugField.fieldType == typeof(uint) || debugField.fieldType == typeof(int))
                {
                    shaderText += "            result = GetIndexColor(" + lowerStructName + "." + debugField.fieldName + ");\n";
                }
                else // This case left is suppose to be a complex structure. Either we don't support it or it is an enum. Consider it is an enum with GetIndexColor, user can override it if he want.
                {
                    shaderText += "            result = GetIndexColor(" + lowerStructName + "." + debugField.fieldName + ");\n";
                }

                if (debugField.isSRGB)
                {
                    shaderText += "            needLinearToSRGB = true;\n";
                }

                shaderText += "            break;\n";

                if (debugField.preprocessor.Length > 0)
                {
                    shaderText += "#else\n";
                    shaderText += "        case " + debugField.defineName + ":\n";
                    shaderText += "            result = 0;\n";
                    shaderText += "            break;\n";
                    shaderText += "#endif\n";
                }
            }

            shaderText += "    }\n";
            shaderText += "}\n";

            return shaderText;
        }

        public string EmitFunctionsForPacked()
        {
            string shaderText = string.Empty;

            // In case users ask for debug functions
            if (!attr.needParamDebug)
                return shaderText;

            // Specific to HDRenderPipeline
            string lowerStructName = type.Name.ToLower();

            shaderText += "//\n";
            shaderText += "// Debug functions\n";
            shaderText += "//\n";

            shaderText += "void GetGenerated" + type.Name + "Debug(uint paramId, " + type.Name + " " + lowerStructName + ", inout float3 result, inout bool needLinearToSRGB)\n";
            shaderText += "{\n";
            shaderText += "    switch (paramId)\n";
            shaderText += "    {\n";

            foreach (var debugField in m_DebugFields)
            {
                shaderText += "        case " + debugField.defineName + ":\n";
                if (debugField.fieldType == typeof(float))
                {
                    if (debugField.isDirection)
                    {
                        shaderText += "            result = Get" + debugField.displayName + "(" + lowerStructName + ")" + ".xxx * 0.5 + 0.5;\n";
                    }
                    else
                    {
                        shaderText += "            result = Get" + debugField.displayName + "(" + lowerStructName + ")" + ".xxx;\n";
                    }
                }
                else if (debugField.fieldType == typeof(Vector2))
                {
                    shaderText += "            result = float3(" + "Get" + debugField.displayName + "(" + lowerStructName + "), 0.0);\n";
                }
                else if (debugField.fieldType == typeof(Vector3))
                {
                    if (debugField.isDirection)
                    {
                        shaderText += "            result = Get" + debugField.displayName + "(" + lowerStructName + ")" + " * 0.5 + 0.5;\n";
                    }
                    else
                    {
                        shaderText += "            result = Get" + debugField.displayName + "(" + lowerStructName + ")" + ";\n";
                    }
                }
                else if (debugField.fieldType == typeof(Vector4))
                {
                    shaderText += "            result = Get" + debugField.displayName + "(" + lowerStructName + ")" + ".xyz;\n";
                }
                else if (debugField.fieldType == typeof(bool))
                {
                    shaderText += "            result = (" + "Get" + debugField.displayName + "(" + lowerStructName + ") ? float3(1.0, 1.0, 1.0) : float3(0.0, 0.0, 0.0);\n";
                }
                else if (debugField.fieldType == typeof(uint) || debugField.fieldType == typeof(int))
                {
                    shaderText += "            result = GetIndexColor(" + "Get" + debugField.displayName + "(" + lowerStructName + "));\n";
                }
                else // This case left is suppose to be a complex structure. Either we don't support it or it is an enum. Consider it is an enum with GetIndexColor, user can override it if he want.
                {
                    shaderText += "            result = GetIndexColor(" + "Get" + debugField.displayName + "(" + lowerStructName + "));\n";
                }

                if (debugField.isSRGB)
                {
                    shaderText += "            needLinearToSRGB = true;\n";
                }

                shaderText += "            break;\n";
            }

            shaderText += "    }\n";
            shaderText += "}\n";

            return shaderText;
        }

        public string Emit()
        {
            return EmitDefines() + EmitTypeDecl() + EmitAccessors() + EmitSetters();
        }

        private string EmitPackedGetters()
        {
            string gettersString = "";

            gettersString += "//\n";
            gettersString += "// Accessors for packed fields\n";
            gettersString += "//\n";

            foreach (PackedFieldInfo packedInfo in m_PackedFieldsInfos)
            {
                string sourceName = type.Name.ToLower();
                string funcSignature = "Get" + packedInfo.packingAttribute.displayNames[0] + "(in " + type.Name + " " + sourceName + ")";
                string funcBody = "\n{\n    ";
                string funcBodyBeforeReturn = "";
                float minValue = packedInfo.packingAttribute.range[0];
                float maxValue = packedInfo.packingAttribute.range[1];
                bool renormalizedRange = (minValue != 0.0f || maxValue != 1.0f);
                string maxMinusMinString = (maxValue - minValue).ToString();
                string minValueString = minValue > 0 ? " +" : " -";
                minValueString += Math.Abs(minValue).ToString();

                switch (packedInfo.packingAttribute.packingScheme)
                {
                    case FieldPacking.PackedFloat:
                        funcSignature = "float " + funcSignature;
                        funcBodyBeforeReturn = "UnpackUIntToFloat(" + sourceName + "." + packedInfo.fieldName + ", " + packedInfo.packingAttribute.offsetInSource + ", " + packedInfo.packingAttribute.sizeInBits + ")";
                        if (renormalizedRange)
                        {
                            funcBodyBeforeReturn = "((" + funcBodyBeforeReturn + " * " + maxMinusMinString + ") " + minValueString + ")";
                        }

                        funcBody += "return " + funcBodyBeforeReturn + ";";
                        break;
                    case FieldPacking.PackedUint:
                        funcSignature = "uint " + funcSignature;
                        funcBodyBeforeReturn = "BitFieldExtract(" + sourceName + "." + packedInfo.fieldName + ", " + packedInfo.packingAttribute.offsetInSource + ", " + packedInfo.packingAttribute.sizeInBits + ")";
                        if (renormalizedRange)
                        {
                            funcBodyBeforeReturn = "((" + funcBodyBeforeReturn + " * " + maxMinusMinString + ") " + minValueString + ")";
                        }
                        funcBody += "return " + funcBodyBeforeReturn + ";";
                        break;
                    case FieldPacking.R11G11B10:
                        funcSignature = "float3 " + funcSignature;
                        funcBodyBeforeReturn = "UnpackFromR11G11B10f(" + sourceName + "." + packedInfo.fieldName + ")";
                        if (renormalizedRange)
                        {
                            funcBodyBeforeReturn = "((" + funcBodyBeforeReturn + " * " + maxMinusMinString + ") " + minValueString + ")";
                        }
                        funcBody += "return " + funcBodyBeforeReturn + ";";
                        break;
                    case FieldPacking.NoPacking:
                        if (packedInfo.fieldType == typeof(uint))
                        {
                            funcSignature = "uint " + funcSignature;
                        }
                        else if (packedInfo.fieldType == typeof(float))
                        {
                            funcSignature = "float " + funcSignature;
                        }
                        else if (packedInfo.fieldType == typeof(Vector3))
                        {
                            funcSignature = "float3 " + funcSignature;
                        }
                        else if (packedInfo.fieldType == typeof(Vector4))
                        {
                            funcSignature = "float4 " + funcSignature;
                        }
                        else if (packedInfo.fieldType == typeof(Vector2Int))
                        {
                            funcSignature = "int2 " + funcSignature;
                        }
                        funcBody += "return (" + sourceName + "." + packedInfo.fieldName + ");";
                        break;
                    default:
                        funcSignature = "ERROR_Packing_field_not_specified\n ";
                        break;
                }

                funcBody += "\n}\n";
                gettersString += funcSignature + funcBody;
            }
            return gettersString;
        }

        private string EmitPackedSetters()
        {
            string settersString = "";

            settersString += "//\n";
            settersString += "// Setters for packed fields\n";
            settersString += "//\n";

            foreach (PackedFieldInfo packedInfo in m_PackedFieldsInfos)
            {
                PackingAttribute attr = packedInfo.packingAttribute;
                string funcSignature = "void Set" + attr.displayNames[0] + "(";
                string newParamName = "new" + attr.displayNames[0];
                string funcBody = "{\n    ";
                string shiftString = attr.offsetInSource != 0 ? (" << " + attr.offsetInSource) : " ";
                string sourceName = type.Name.ToLower();
                uint mask = (1u << attr.sizeInBits) - 1u;
                string maskString = mask.ToString();
                float minValue = packedInfo.packingAttribute.range[0];
                float maxValue = packedInfo.packingAttribute.range[1];
                bool renormalizedRange = (minValue != 0.0f || maxValue != 1.0f);
                float maxMinusMinInv = (1.0f / (maxValue - minValue));
                float minOvermaxMinusMin = minValue * maxMinusMinInv;
                string minOvermaxMinusMinString = minOvermaxMinusMin > 0 ? " - " : " + ";
                minOvermaxMinusMinString += (Math.Abs(minOvermaxMinusMin)).ToString();
                string maxMinusMinInvString = maxMinusMinInv.ToString();

                string newParamCode;
                switch (attr.packingScheme)
                {
                    case FieldPacking.PackedFloat:
                        funcSignature += "float " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        newParamCode = newParamName;
                        if (renormalizedRange)
                        {
                            newParamCode = "((" + newParamCode + " * " + maxMinusMinInvString + ") " + minOvermaxMinusMinString + ")";
                        }
                        funcBody += sourceName + "." + packedInfo.fieldName + " = BitFieldInsert(" + maskString + shiftString + ", UnpackInt(" + newParamCode + ", " + attr.sizeInBits + ")" + shiftString + ", " + sourceName + "." + packedInfo.fieldName + ");";
                        break;
                    case FieldPacking.PackedUint:
                        funcSignature += "uint " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        newParamCode = newParamName;
                        if (renormalizedRange)
                        {
                            newParamCode = "((" + newParamCode + " * " + maxMinusMinInvString + ") " + minOvermaxMinusMinString + ")";
                        }
                        funcBody += sourceName + "." + packedInfo.fieldName + " = BitFieldInsert(" + maskString + shiftString + ", (" + newParamCode + ")" + shiftString + ", " + sourceName + "." + packedInfo.fieldName + ");";
                        break;
                    case FieldPacking.R11G11B10:
                        funcSignature += "float3 " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        newParamCode = newParamName;
                        if (renormalizedRange)
                        {
                            newParamCode = "((" + newParamCode + " * " + maxMinusMinInvString + ") " + minOvermaxMinusMinString + ")";
                        }
                        funcBody += sourceName + "." + packedInfo.fieldName + " = PackToR11G11B10f(" + newParamCode + ");";
                        break;
                    case FieldPacking.NoPacking:
                        if (packedInfo.fieldType == typeof(uint))
                        {
                            funcSignature += "uint " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        }
                        else if (packedInfo.fieldType == typeof(float))
                        {
                            funcSignature += "float " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        }
                        else if (packedInfo.fieldType == typeof(Vector3))
                        {
                            funcSignature += "float3 " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        }
                        else if (packedInfo.fieldType == typeof(Vector4))
                        {
                            funcSignature += "float4 " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        }
                        else if (packedInfo.fieldType == typeof(Vector2Int))
                        {
                            funcSignature += "int2 " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        }
                        funcBody += sourceName + "." + packedInfo.fieldName + " = " + newParamName + ";";
                        break;
                    default:
                        funcSignature = "ERROR_Packing_field_not_specified\n ";
                        break;
                }

                funcSignature += "\n";
                funcBody += "\n}\n";


                settersString += funcSignature + funcBody;
            }
            return settersString;
        }

        private string EmitPackedInit()
        {
            string initString = "";

            initString += "//\n";
            initString += "// Init functions for packed fields.\n";
            initString += "// Important: Init functions assume the field is filled with 0s, use setters otherwise. \n";
            initString += "//\n";

            foreach (PackedFieldInfo packedInfo in m_PackedFieldsInfos)
            {
                PackingAttribute attr = packedInfo.packingAttribute;
                string funcSignature = "void Init" + attr.displayNames[0] + "(";
                string newParamName = "new" + attr.displayNames[0];
                string funcBody = "{\n    ";
                string shiftString = attr.offsetInSource != 0 ? (" << " + attr.offsetInSource) : " ";
                string sourceName = type.Name.ToLower();
                float minValue = packedInfo.packingAttribute.range[0];
                float maxValue = packedInfo.packingAttribute.range[1];
                bool renormalizedRange = (minValue != 0.0f || maxValue != 1.0f);
                float maxMinusMinInv = (1.0f / (maxValue - minValue));
                float minOvermaxMinusMin = minValue * maxMinusMinInv;
                string minOvermaxMinusMinString = minOvermaxMinusMin > 0 ? " - " : " + ";
                minOvermaxMinusMinString += (Math.Abs(minOvermaxMinusMin)).ToString();
                string maxMinusMinInvString = maxMinusMinInv.ToString();


                string newParamCode;
                switch (attr.packingScheme)
                {
                    case FieldPacking.PackedFloat:
                        funcSignature += "float " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        newParamCode = newParamName;
                        if (renormalizedRange)
                        {
                            newParamCode = "((" + newParamCode + " * " + maxMinusMinInvString + ") " + minOvermaxMinusMinString + ")";
                        }
                        funcBody += sourceName + "." + packedInfo.fieldName + " |= UnpackInt(" + newParamCode + ", " + attr.sizeInBits + ")" + shiftString + "; ";
                        break;
                    case FieldPacking.PackedUint:
                        funcSignature += "uint " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        newParamCode = newParamName;
                        if (renormalizedRange)
                        {
                            newParamCode = "((" + newParamCode + " * " + maxMinusMinInvString + ") " + minOvermaxMinusMinString + ")";
                        }
                        funcBody += sourceName + "." + packedInfo.fieldName + " |= (" + newParamCode + ")" + shiftString + ";";
                        break;
                    case FieldPacking.R11G11B10:
                        funcSignature += "float3 " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        newParamCode = newParamName;
                        if (renormalizedRange)
                        {
                            newParamCode = "((" + newParamCode + " * " + maxMinusMinInvString + ") " + minOvermaxMinusMinString + ")";
                        }
                        funcBody += sourceName + "." + packedInfo.fieldName + " = PackToR11G11B10f(" + newParamCode + ");";
                        break;
                    case FieldPacking.NoPacking:
                        if (packedInfo.fieldType == typeof(uint))
                        {
                            funcSignature += "uint " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        }
                        else if (packedInfo.fieldType == typeof(float))
                        {
                            funcSignature += "float " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        }
                        else if (packedInfo.fieldType == typeof(Vector3))
                        {
                            funcSignature += "float3 " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        }
                        else if (packedInfo.fieldType == typeof(Vector4))
                        {
                            funcSignature += "float4 " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        }
                        else if (packedInfo.fieldType == typeof(Vector2Int))
                        {
                            funcSignature += "int2 " + newParamName + ", inout " + type.Name + " " + sourceName + ")";
                        }
                        funcBody += sourceName + "." + packedInfo.fieldName + " = " + newParamName + ";";
                        break;
                    default:
                        funcSignature = "\n\n ERROR_Packing_field_not_specified\n ";
                        break;
                }

                funcSignature += "\n";
                funcBody += "\n}\n";
                initString += funcSignature + funcBody;
            }
            return initString;
        }

        public string EmitPackedInfo()
        {
            string pathToPackingHeader = "#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl\"\n";
            string packedStructCode = pathToPackingHeader + EmitPackedGetters() + EmitPackedSetters() + EmitPackedInit() + "\n";
            if (attr.needParamDebug)
            {
                packedStructCode += EmitFunctionsForPacked();
            }
            return packedStructCode;
        }

        // This function is a helper to follow unity convention
        // when converting fooBar to FOO_BAR
        string InsertUnderscore(string name)
        {
            for (int i = 1; i < name.Length; i++)
            {
                if (char.IsLower(name[i - 1]) && char.IsUpper(name[i]))
                {
                    // case switch, insert underscore
                    name = name.Insert(i, "_");
                }
            }

            return name;
        }

        public bool Generate()
        {
            m_Statics = new Dictionary<string, string>();

            FieldInfo[] fields = type.GetFields();
            m_ShaderFields = new List<ShaderFieldInfo>();
            m_DebugFields = new List<DebugFieldInfo>();
            m_PackedFieldsInfos = new List<PackedFieldInfo>();

            if (type.IsEnum)
            {
                foreach (var field in fields)
                {
                    if (!field.IsSpecialName)
                    {
                        string name = field.Name;
                        name = InsertUnderscore(name);
                        m_Statics[(type.Name + "_" + name).ToUpper()] = field.GetRawConstantValue().ToString();
                    }
                }
                errors = null;
                return true;
            }

            foreach (var field in fields)
            {
                // Support array get getting array type
                Type fieldType = field.FieldType;
                int arraySize = -1;

                if (Attribute.IsDefined(field, typeof(FixedBufferAttribute)))
                {
                    var arrayInfos = (field.GetCustomAttributes(typeof(HLSLArray), false) as HLSLArray[]);
                    if (arrayInfos.Length != 0)
                    {
                        // For constant buffers, every element of the array needs to be aligned to a Vector4
                        if (attr.generateCBuffer &&
                            arrayInfos[0].elementType != typeof(Vector4) &&
                            arrayInfos[0].elementType != typeof(ShaderGenUInt4) &&
                            arrayInfos[0].elementType != typeof(Matrix4x4))
                        {
                            Error("Invalid HLSLArray target: '" + field.FieldType + "'" + ", only Vector4, Matrix4x4 and ShaderGenUInt4 are supported for arrays in constant buffers.");
                            return false;
                        }

                        arraySize = arrayInfos[0].arraySize;
                        fieldType = arrayInfos[0].elementType;
                    }
                    else
                    {
                        // We just ignore every array type not marked with the HLSLArray attribute
                        continue;
                    }
                }
                else if (Attribute.IsDefined(field, typeof(HLSLArray)))
                {
                    Error("Invalid HLSLArray target: '" + field.FieldType + "'" + ", this attribute can only be used on fixed array");
                    return false;
                }

                if (field.IsStatic)
                {
                    if (fieldType.IsPrimitive)
                    {
                        // Unity convention is to start static of constant with k_ or s_, remove this part
                        string name = InsertUnderscore(field.Name);
                        if (name.StartsWith("k_") || name.StartsWith("s_"))
                        {
                            name = name.Substring(2);
                        }
                        string defineName = name.ToUpper();
                        m_Statics[defineName] = field.GetValue(null).ToString();
                    }
                    continue;
                }

                if (attr.needParamDebug && !attr.containsPackedFields)
                {
                    List<string> displayNames = new List<string>();
                    displayNames.Add(field.Name);

                    bool isDirection = false;
                    bool sRGBDisplay = false;
                    bool checkIsNormalized = false;
                    string preprocessor = "";

                    // Check if the display name have been override by the users
                    if (Attribute.IsDefined(field, typeof(SurfaceDataAttributes)))
                    {
                        var propertyAttr = (SurfaceDataAttributes[])field.GetCustomAttributes(typeof(SurfaceDataAttributes), false);

                        // User can want to only override isDirection and sRGBDisplay
                        // in this case it can specify empty string
                        if (propertyAttr[0].displayNames[0] != "")
                        {
                            displayNames.Clear();
                            displayNames.AddRange(propertyAttr[0].displayNames);
                        }
                        isDirection = propertyAttr[0].isDirection;
                        sRGBDisplay = propertyAttr[0].sRGBDisplay;
                        checkIsNormalized = propertyAttr[0].checkIsNormalized;
                        preprocessor = propertyAttr[0].preprocessor;
                    }


                    if (!attr.containsPackedFields)
                    {
                        string className = type.FullName.Substring(type.FullName.LastIndexOf((".")) + 1); // ClassName include nested class
                        className = className.Replace('+', '_'); // FullName is Class+NestedClass replace by Class_NestedClass

                        foreach (string it in displayNames)
                        {
                            string fieldName = it.Replace(' ', '_');
                            string name = InsertUnderscore(fieldName);
                            string defineName = ("DEBUGVIEW_" + className + "_" + name).ToUpper();
                            m_Statics[defineName] = Convert.ToString(attr.paramDefinesStart + debugCounter++);

                            m_DebugFields.Add(new DebugFieldInfo(defineName, field.Name, fieldType, isDirection, sRGBDisplay, checkIsNormalized, preprocessor: preprocessor));
                        }
                    }
                }

                if (attr.containsPackedFields)
                {
                    // Define only once, it is safe to assume that colors and directions are not packed with something else
                    bool isDirection = false;
                    bool sRGBDisplay = false;
                    bool checkIsNormalized = false;
                    string preprocessor = "";

                    if (Attribute.IsDefined(field, typeof(PackingAttribute)))
                    {
                        var packingAttributes = (PackingAttribute[])field.GetCustomAttributes(typeof(PackingAttribute), false);
                        isDirection = packingAttributes[0].isDirection;
                        sRGBDisplay = packingAttributes[0].sRGBDisplay;
                        checkIsNormalized = packingAttributes[0].checkIsNormalized;
                        preprocessor = packingAttributes[0].preprocessor;

                        // Generate debug names
                        string className = type.FullName.Substring(type.FullName.LastIndexOf((".")) + 1); // ClassName include nested class
                        className = className.Replace('+', '_'); // FullName is Class+NestedClass replace by Class_NestedClass
                        foreach (PackingAttribute packAttr in packingAttributes)
                        {
                            List<string> displayNames = new List<string>();

                            displayNames.AddRange(packAttr.displayNames);
                            foreach (string it in displayNames)
                            {
                                string fieldName = it.Replace(' ', '_');
                                string name = InsertUnderscore(fieldName);
                                string defineName = ("DEBUGVIEW_" + className + "_" + name).ToUpper();
                                m_Statics[defineName] = Convert.ToString(attr.paramDefinesStart + debugCounter++);

                                Type typeForDebug = typeof(uint);
                                if (packAttr.packingScheme == FieldPacking.PackedFloat)
                                {
                                    typeForDebug = typeof(float);
                                }
                                else if (packAttr.packingScheme == FieldPacking.R11G11B10)
                                {
                                    typeForDebug = typeof(Vector3);
                                }
                                else if (packAttr.packingScheme == FieldPacking.NoPacking)
                                {
                                    typeForDebug = fieldType;
                                }

                                m_DebugFields.Add(new DebugFieldInfo(defineName, field.Name, typeForDebug, isDirection, sRGBDisplay, checkIsNormalized, packAttr.displayNames[0], preprocessor: preprocessor));
                            }

                            m_PackedFieldsInfos.Add(new PackedFieldInfo(packAttr, fieldType, field.Name));
                        }
                    }
                }

                // To control variable scope
                {
                    PrimitiveType floatPrecision = PrimitiveType.Float;
                    string preprocessor = "";
                    if (Attribute.IsDefined(field, typeof(SurfaceDataAttributes)))
                    {
                        var propertyAttr = (SurfaceDataAttributes[])field.GetCustomAttributes(typeof(SurfaceDataAttributes), false);
                        if (propertyAttr[0].precision == FieldPrecision.Half)
                            floatPrecision = PrimitiveType.Half;
                        else if (propertyAttr[0].precision == FieldPrecision.Real)
                            floatPrecision = PrimitiveType.Real;

                        preprocessor = propertyAttr[0].preprocessor;
                    }

                    if (fieldType.IsPrimitive)
                    {
                        if (fieldType == typeof(float))
                            EmitPrimitiveType(floatPrecision, "", 1, arraySize, field.Name, "", preprocessor, m_ShaderFields);
                        else if (fieldType == typeof(int))
                            EmitPrimitiveType(PrimitiveType.Int, "", 1, arraySize, field.Name, "", preprocessor, m_ShaderFields);
                        else if (fieldType == typeof(uint))
                            EmitPrimitiveType(PrimitiveType.UInt, "", 1, arraySize, field.Name, "", preprocessor, m_ShaderFields);
                        else if (fieldType == typeof(bool))
                            EmitPrimitiveType(PrimitiveType.Bool, "", 1, arraySize, field.Name, "", preprocessor, m_ShaderFields);
                        else
                        {
                            Error("unsupported field type '" + fieldType + "'");
                            return false;
                        }
                    }
                    else
                    {
                        // handle special types, otherwise try parsing the struct
                        if (fieldType == typeof(Vector2))
                            EmitPrimitiveType(floatPrecision, "", 2, arraySize, field.Name, "", preprocessor, m_ShaderFields);
                        else if (fieldType == typeof(Vector3))
                            EmitPrimitiveType(floatPrecision, "", 3, arraySize, field.Name, "", preprocessor, m_ShaderFields);
                        else if (fieldType == typeof(Vector4))
                            EmitPrimitiveType(floatPrecision, "", 4, arraySize, field.Name, "", preprocessor, m_ShaderFields);
                        else if (fieldType == typeof(Vector2Int))
                            EmitPrimitiveType(PrimitiveType.Int, "", 2, arraySize, field.Name, "", preprocessor, m_ShaderFields);
                        else if (fieldType == typeof(ShaderGenUInt4))
                            EmitPrimitiveType(PrimitiveType.UInt, "", 4, arraySize, field.Name, "", preprocessor, m_ShaderFields);
                        else if (fieldType == typeof(Matrix4x4))
                            EmitMatrixType(floatPrecision, 4, 4, arraySize, field.Name, "", preprocessor, m_ShaderFields);
                        else if (!ExtractComplex(field, preprocessor, m_ShaderFields))
                        {
                            // Error reporting done in ExtractComplex()
                            return false;
                        }
                    }
                }
            }

            m_PackedFields = m_ShaderFields;
            if (attr.packingRules == PackingRules.Aggressive)
            {
                m_PackedFields = Pack(m_ShaderFields);

                if (m_PackedFields == null)
                {
                    return false;
                }
            }

            errors = null;
            return true;
        }

        public bool hasFields
        {
            get { return m_ShaderFields.Count > 0; }
        }

        public bool hasPackedInfo
        {
            get { return m_PackedFieldsInfos.Count > 0; }
        }

        public bool hasStatics
        {
            get { return m_Statics.Count > 0; }
        }

        public bool needAccessors
        {
            get { return attr.needAccessors; }
        }
        public bool needParamDebug
        {
            get { return attr.needParamDebug; }
        }

        public Type type;
        public GenerateHLSL attr;
        public int debugCounter;
        public List<string> errors = null;

        Dictionary<string, string> m_Statics;
        List<ShaderFieldInfo> m_ShaderFields;
        List<ShaderFieldInfo> m_PackedFields;
        List<DebugFieldInfo> m_DebugFields;

        // Fields from PackingAttribute
        List<PackedFieldInfo> m_PackedFieldsInfos;
    }
}
