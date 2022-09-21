
/*
 * C#-to-HLSL shader generator, originally from https://github.com/Unity-Technologies/Graphics/tree/master/Packages/com.unity.render-pipelines.core/Editor/ShaderGenerator
 * Licensed with Unity Companion License
 * 
 * */

using System.Threading.Tasks;
using UnityEngine.Rendering;
using UnityEditor;

namespace Varjo.ShaderBinding.Editor
{
    class ShaderGeneratorMenu
    {
        [MenuItem("Edit/ShaderUtils/Generate Shader Includes")]
        async static Task GenerateShaderIncludes()
        {
            await CSharpToHLSL.GenerateAll();
            AssetDatabase.Refresh();
        }
    }
}
