# UnityShaderBinder
Utility library to bind C# fields and properties into Unity ShaderLab properties and keywords automatically.

# Introduction
A typical pattern in Unity shader development is to have a MonoBehavior object that contains various toggles and parameters that are then directly fed to ShaderLab. This ends up in quite a lot of boilerplate code. UnityShaderBinder helps the developer by automating setting ShaderLab properties and keywords.

# Usage
The usage is simple:
- Add 'using ShaderBinding;' to your C# file
- Add [ShaderValue] attribute to any int, Vector3/4, float, Matrix4x4, Texture, ComputeBuffer or GraphicsBuffer field or property to automatically expose them to the material/computeshader/materialpropertyblock
- Add [ShaderKeyword] to a bool field or property to turn it into a shader keyword
- Before rendering, call the ApplyShaderProps() extension method and pass it the Material/MaterialPropertyBlock/ComputeShader you want to apply the properties and keywords to.
- You're done!

# Notes
- Both [ShaderValue] and [ShaderKeyword] attributes take an optional Target string parameter that can be used to override the name of the property or keyword in ShaderLab, otherwise the name of the field/property is used. If the field/property name starts with 'm_', that part is removed, similar to how Unity inspector works.
- ApplyShaderProps is an extension method, so it has to be called using 'this.ApplyShaderProps(...)'. This is a C# limitation.
- ApplyShaderProps for ComputeShaders takes a list of kernel indices as variable-length parameter list. This is useful if you want to set the parameters to multiple kernels at once.
- The ShaderIDs are automatically calculated and cached on first call into ApplyShaderProps()

# Example
```
using ShaderBinding; // Pull in the extension methods and the attributes

public class MyComponent : MonoBehavior
{
  public Material m_Material;
  
  // This causes the shader keyword "DO_MAGIC" to be enabled or disabled based on the value of this field.
  // If the Target property is omitted, the keyword name would be "DoMagic" instead.
  [ShaderKeyword(Target="DO_MAGIC"] 
  public bool m_DoMagic = false;

  [ShaderValue] // ShaderLab property "AmountOfMagic" will be set to the value of this field
  public float m_AmountOfMagic = 1.0;

  [ShaderValue] // Also works on properties, and on Vector3/4's
  public Vector3 MagicDirection { get; set; }

  void OnUpdate()
  {
    // This call is equivalent to the following commented-out code:
    this.ApplyShaderProps(m_Material);
    //
    // if(m_DoMagic)
    //     m_Material.EnableKeyword("DO_MAGIC");
    // else
    //     m_Material.DisableKeyword("DO_MAGIC");
    //
    // // This bit is actually missing the caching of ShaderIDs for "AmountOfMagic" etc
    // m_Material.SetFloat("AmountOfMagic", m_AmountOfMagic);
    // m_Material.SetVector("MagicDirection", MagicDirection);

    Graphics.Blit(m_Material, ...);
  }
}

```

# Copyright & License
Copyright 2022 Mikko Strandborg. This software is licensed under Boost Software License 1.0, See LICENSE.
