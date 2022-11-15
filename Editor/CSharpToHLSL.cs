
/*
 * C#-to-HLSL shader generator, originally from https://github.com/Unity-Technologies/Graphics/tree/master/Packages/com.unity.render-pipelines.core/Editor/ShaderGenerator
 * Licensed with Unity Companion License
 * 
 * */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System.Reflection;
using Varjo.ShaderBinder;

namespace Varjo.ShaderBinding.Editor
{
    internal class CSharpToHLSL
    {
        /// <summary>
        ///     Generate all shader code from <see cref="GenerateHLSL" /> attribute.
        /// </summary>
        /// <returns>An awaitable task.</returns>
        public static async Task GenerateAll()
        {
            try
            {
                // Store per source file path the generator definitions
                var sourceGenerators = new Dictionary<string, (List<ShaderTypeGenerator>, List<ShaderBindingGenerator>)>();
                var keywordGenerators = new Dictionary<string, List<ShaderKeywordGenerator>>();

                // Extract all types with the GenerateHLSL tag
                foreach (var type in TypeCache.GetTypesWithAttribute<GenerateHLSL>())
                {
                    var attr = type.GetCustomAttributes(typeof(GenerateHLSL), false).First() as GenerateHLSL;
                    if (!sourceGenerators.TryGetValue(attr.sourcePath, out var generators))
                    {
                        generators = (new List<ShaderTypeGenerator>(), new List<ShaderBindingGenerator>());
                        sourceGenerators.Add(attr.sourcePath, generators);
                    }

                    generators.Item1.Add(new ShaderTypeGenerator(type, attr));
                }
                // GetTypes() may throw so we need to open the query up
                // Extract all types with the ShaderValue tag
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach(var ass in assemblies)
                {
                    try
                    {
                        var types = ass.GetTypes();

                        var fieldsWithAttr = from type in types
                                             where (type.IsClass || (type.IsValueType && !type.IsEnum && !type.IsPrimitive))
                                             from field in type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                                             from attr in field.GetCustomAttributes(typeof(ShaderValueAttribute), false).Cast<ShaderValueAttribute>()
                                             where attr.GenerateHLSL == true
                                             select (attr, field);

                        foreach (var (attr, field) in fieldsWithAttr)
                        {
                            if (!sourceGenerators.TryGetValue(attr.sourcePath, out var generators))
                            {
                                generators = (new(), new());
                                sourceGenerators.Add(attr.sourcePath, generators);
                            }

                            generators.Item2.Add(new ShaderBindingGenerator(field, attr));
                        }
                        // And properties
                        var propsWithAttr = from type in types
                                            where (type.IsClass || (type.IsValueType && !type.IsEnum && !type.IsPrimitive))
                                            from prop in type.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                                            from attr in prop.GetCustomAttributes(typeof(ShaderValueAttribute), false).Cast<ShaderValueAttribute>()
                                            where attr.GenerateHLSL == true
                                            select (attr, prop);

                        foreach (var (attr, prop) in propsWithAttr)
                        {
                            if (!sourceGenerators.TryGetValue(attr.sourcePath, out var generators))
                            {
                                generators = (new(), new());
                                sourceGenerators.Add(attr.sourcePath, generators);
                            }

                            generators.Item2.Add(new ShaderBindingGenerator(prop, attr));

                        }

                        // Extract all types with the ShaderKernel tag
                        var keywordAttrs = from type in types
                                           where (type.IsClass || (type.IsValueType && !type.IsEnum && !type.IsPrimitive))
                                           from field in type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                                           from attr in field.GetCustomAttributes(typeof(ShaderKeywordAttribute), false).Cast<ShaderKeywordAttribute>()
                                           select (attr, field);

                        foreach (var (attr, field) in keywordAttrs)
                        {
                            if (!keywordGenerators.TryGetValue(attr.sourcePath, out var generators))
                            {
                                generators = new();
                                keywordGenerators.Add(attr.sourcePath, generators);
                            }

                            generators.Add(new ShaderKeywordGenerator(field, attr));
                        }

                    }
                    catch (ReflectionTypeLoadException e) { /* Ignore typeload exception */ }
                }

                // Generate all files
                await Task.WhenAll(sourceGenerators.Select(async it =>
                    await GenerateAsync($"{it.Key}.hlsl", $"{Path.ChangeExtension(it.Key, "custom")}.hlsl", it.Value.Item1, it.Value.Item2)));

                await Task.WhenAll(keywordGenerators.Select(async it =>
                    await GenerateKeywordsAsync($"{it.Key}.pragmas.hlsl", $"{Path.ChangeExtension(it.Key, "custom")}.hlsl", it.Value)));
            }
            catch(Exception ex) { Debug.Log(ex); }

            Debug.Log("Generation completed");
        }

        /// <summary>
        ///     Generate all shader code from <paramref name="generators" /> into <paramref name="targetFilename" />.
        /// </summary>
        /// <param name="targetFilename">Path of the file to generate.</param>
        /// <param name="targetCustomFilename">Path of the custom file to include. (If it exists)</param>
        /// <param name="generators">Generators to execute.</param>
        /// <returns>Awaitable task.</returns>
        private static async Task GenerateAsync(string targetFilename, string targetCustomFilename,
            List<ShaderTypeGenerator> generators, List<ShaderBindingGenerator> bindGenerators)
        {
            var skipFile = false;

            // Emit atomic element for all generators
            foreach (var gen in generators.Where(gen => !gen.Generate()))
            {
                // Error reporting will be done by the generator.  Skip this file.
                Debug.LogError("Error converting " + targetFilename);
                gen.PrintErrors();
                skipFile = true;
                break;
            }
            foreach (var bgen in bindGenerators)
            {
                if(!bgen.Generate())
                {
                    // Error reporting will be done by the generator.  Skip this file.
                    Debug.LogError("Error converting " + targetFilename);
                    bgen.PrintErrors();
                    skipFile = true;
                    break;
                }
            }

            // If an error occured during generation, we abort this file
            if (skipFile)
                return;

            // Check access to the file
            if (File.Exists(targetFilename))
            {
                FileInfo info = null;
                try
                {
                    info = new FileInfo(targetFilename);
                }
                catch (UnauthorizedAccessException)
                {
                    Debug.Log("Access to " + targetFilename + " is denied. Skipping it.");
                    return;
                }
                catch (SecurityException)
                {
                    Debug.Log("You do not have permission to access " + targetFilename + ". Skipping it.");
                    return;
                }

                if (info?.IsReadOnly ?? false)
                {
                    Debug.Log(targetFilename + " is ReadOnly. Skipping it.");
                    return;
                }
            }

            // Generate content
            using var writer = File.CreateText(targetFilename);
            writer.NewLine = Environment.NewLine;

            // Include guard name
            var guard = Path.GetFileName(targetFilename).Replace(".", "_").ToUpper();
            if (!char.IsLetter(guard[0]))
                guard = "_" + guard;

            await writer.WriteLineAsync("//");
            await writer.WriteLineAsync("// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > ShaderUtils > Generate Shader Includes ] instead");
            await writer.WriteLineAsync("//");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("#ifndef " + guard);
            await writer.WriteLineAsync("#define " + guard);

            foreach (var gen in generators.Where(gen => gen.hasStatics))
                await writer.WriteLineAsync(gen.EmitDefines().Replace("\n", writer.NewLine));

            foreach (var gen in generators.Where(gen => gen.hasFields))
                await writer.WriteLineAsync(gen.EmitTypeDecl().Replace("\n", writer.NewLine));

            foreach (var gen in generators.Where(gen => gen.hasFields && gen.needAccessors && !gen.hasPackedInfo))
            {
                await writer.WriteAsync(gen.EmitAccessors().Replace("\n", writer.NewLine));
                await writer.WriteAsync(gen.EmitSetters().Replace("\n", writer.NewLine));
                const bool emitInitters = true;
                await writer.WriteAsync(gen.EmitSetters(emitInitters).Replace("\n", writer.NewLine));
            }

            foreach (var gen in generators.Where(gen =>
                gen.hasStatics && gen.hasFields && gen.needParamDebug && !gen.hasPackedInfo))
                await writer.WriteLineAsync(gen.EmitFunctions().Replace("\n", writer.NewLine));

            foreach (var gen in generators.Where(gen => gen.hasPackedInfo))
                await writer.WriteLineAsync(gen.EmitPackedInfo().Replace("\n", writer.NewLine));

            await writer.WriteLineAsync();

            foreach (var gen in bindGenerators)
                await writer.WriteAsync(gen.Emit().Replace("\n", writer.NewLine));

            await writer.WriteLineAsync();

            await writer.WriteLineAsync("#endif");

            if (File.Exists(targetCustomFilename))
                await writer.WriteAsync($"#include \"{Path.GetFileName(targetCustomFilename)}\"");
        }

        /// <summary>
        ///     Generate all shader code from <paramref name="generators" /> into <paramref name="targetFilename" />.
        /// </summary>
        /// <param name="targetFilename">Path of the file to generate.</param>
        /// <param name="targetCustomFilename">Path of the custom file to include. (If it exists)</param>
        /// <param name="generators">Generators to execute.</param>
        /// <returns>Awaitable task.</returns>
        private static async Task GenerateKeywordsAsync(string targetFilename, string targetCustomFilename,
            List<ShaderKeywordGenerator> generators)
        {
            var skipFile = false;

            // Emit atomic element for all generators
            foreach (var gen in generators.Where(gen => !gen.Generate()))
            {
                // Error reporting will be done by the generator.  Skip this file.
                Debug.LogError("Error converting " + targetFilename);
                gen.PrintErrors();
                skipFile = true;
                break;
            }

            // If an error occured during generation, we abort this file
            if (skipFile)
                return;

            // Check access to the file
            if (File.Exists(targetFilename))
            {
                FileInfo info = null;
                try
                {
                    info = new FileInfo(targetFilename);
                }
                catch (UnauthorizedAccessException)
                {
                    Debug.Log("Access to " + targetFilename + " is denied. Skipping it.");
                    return;
                }
                catch (SecurityException)
                {
                    Debug.Log("You do not have permission to access " + targetFilename + ". Skipping it.");
                    return;
                }

                if (info?.IsReadOnly ?? false)
                {
                    Debug.Log(targetFilename + " is ReadOnly. Skipping it.");
                    return;
                }
            }

            // Generate content
            using var writer = File.CreateText(targetFilename);
            writer.NewLine = Environment.NewLine;

            // Include guard name
            var guard = Path.GetFileName(targetFilename).Replace(".", "_").ToUpper();
            if (!char.IsLetter(guard[0]))
                guard = "_" + guard;

            await writer.WriteLineAsync("//");
            await writer.WriteLineAsync("// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > ShaderUtils > Generate Shader Includes ] instead");
            await writer.WriteLineAsync("//");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("#ifndef " + guard);
            await writer.WriteLineAsync("#define " + guard);
            await writer.WriteLineAsync();

            foreach (var gen in generators)
                await writer.WriteAsync(gen.Emit().Replace("\n", writer.NewLine));

            await writer.WriteLineAsync();

            await writer.WriteLineAsync("#endif");

            if (File.Exists(targetCustomFilename))
                await writer.WriteAsync($"#include \"{Path.GetFileName(targetCustomFilename)}\"");
        }


    }
}
