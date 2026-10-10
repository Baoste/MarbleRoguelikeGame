# MRes Packer Workflow — Unity Editor 单文件资源打包需求（xLua 版）

## 1. 项目目标

实现一个**独立 Unity Editor 工具项目**，将一个静态交互物体 Prefab 打包为单个 `.mres` 文件。

`.mres` 不保存 Unity 原始 `.prefab`、OBJ、FBX、`.mat` 或 Shader 源文件，而是保存足以在另一个 Unity 项目中**动态重建该物体**的结构化数据。

目标流程：

```text
Unity Project A

Prefab
├─ Transform Hierarchy
├─ MeshFilter
├─ MeshRenderer
├─ Collider
├─ Rigidbody
├─ Material
├─ Texture
├─ Lua
└─ Metadata
        ↓
   MRes Packer
        ↓
BouncePad.mres
```

Packer **只负责生成 `.mres`**，不负责解析或实例化。

---

# 2. MRes v1 定位

MRes v1 是：

> 一个自包含、可动态读取、可通过 xLua 执行行为脚本的静态交互物体资源格式。

要求：

- 单文件。
- 不依赖源项目 GUID。
- 不依赖 AssetDatabase 路径。
- 不保存 `.meta`。
- 不保存 OBJ / FBX。
- 不保存 Unity `.prefab` 原文件。
- 不保存 Unity `.mat` 原文件。
- 不保存 Shader/HLSL 源码。
- Mesh 转换为自定义二进制数据。
- Material 转换为 `shaderId + properties`。
- Texture 嵌入 `.mres`。
- Prefab 层级和组件转换为 MRes Prefab Schema。
- Lua 以 UTF-8 文本嵌入。
- Metadata 以固定 Schema 嵌入。

---

# 3. Packer 与 Loader 完全解耦

Packer 与 Loader 唯一共享：

```text
MRes v1 Binary Format
MRes v1 Prefab Schema
MRes Material Schema
MRes Metadata Schema
MRes Shader ID Contract
MRes Lua/xLua Behaviour Contract
```

禁止：

- Packer 引用 Loader Assembly。
- Packer 调用 Loader 代码。
- 保存目标项目 GUID。
- 保存源项目绝对路径。
- 共享运行时对象实例。

两边可以分别实现同一套 DTO / enum。

---

# 4. 当前支持范围

MRes v1 只支持**无骨骼静态物体**。

## 支持组件

```text
Transform
MeshFilter
MeshRenderer
BoxCollider
SphereCollider
CapsuleCollider
MeshCollider
Rigidbody
```

源 Prefab **不需要、也不允许依赖任何 MRes/xLua 专用 MonoBehaviour**。

Lua 是 `.mres` Package 级行为，由 Packer UI 单独指定。

## 支持资源

```text
Mesh
Material
Texture2D
PhysicsMaterial
Lua
Metadata
```

## 暂不支持

```text
FBX / OBJ 原文件
SkinnedMeshRenderer
Bone
Animation
Avatar
Animator
BlendShape
ParticleSystem
TrailRenderer
LineRenderer
Terrain
Prefab Variant
任意自定义 MonoBehaviour
Shader Graph 动态打包
Shader/HLSL 源码动态编译
AssetBundle
Addressables
```

---

# 5. xLua 约束

目标项目运行 Lua 时固定使用：

```text
xLua
```

Packer 本身不执行 Lua，也不需要创建 `LuaEnv`。

Lua 与 Prefab 完全分离：

```text
普通 Unity Prefab
        +
      Lua 文件
        +
     Metadata
        ↓
      Packer
        ↓
      .mres
```

Packer 只负责：

```text
读取 .lua 文本
↓
以 UTF-8 保存进 .mres
↓
在 manifest.json 中记录 Lua Entry
```

源 Prefab 不需要挂：

```text
MResLuaBehaviour
MResRuntimeBehaviour
任何 xLua 组件
```

Prefab Schema 中也不记录 Lua 组件。

Lua 属于整个 `.mres` Package，而不是某个 Prefab Node。

---

# 6. Metadata Schema

Metadata 固定为以下四个字段：

```json
{
  "name": "弹射装置",
  "description": "碰撞后将目标向上弹射。",
  "value": 120,
  "type": "device"
}
```

字段定义：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| `name` | string | 是 | 物品名称 |
| `description` | string | 是 | 物品描述 |
| `value` | number | 是 | 物品价值 |
| `type` | string | 是 | 仅允许 `medicine` 或 `device` |

对应中文含义：

```text
medicine = 药物
device   = 装置
```

推荐 C#：

```csharp
public enum MResItemType
{
    Medicine,
    Device
}

[Serializable]
public class MResMetadataData
{
    public string name;
    public string description;
    public float value;
    public string type;
}
```

打包时必须验证：

```text
name 非空
description 非空
value >= 0
type == "medicine" 或 "device"
```

Metadata 不允许携带任意额外业务字段。

---

# 7. Shader 方案

MRes v1 不携带 Shader 源码。

Material 中只保存：

```text
shaderId
```

例如：

```text
MRes/PBR
MRes/Unlit
MRes/Transparent
MRes/Emission
MRes/Hologram
```

Packer 项目维护：

```text
Unity Shader -> shaderId
```

映射。

例如：

```text
Universal Render Pipeline/Lit
    -> MRes/PBR

Custom/Hologram
    -> MRes/Hologram
```

如果 Material 使用的 Shader 没有注册：

```text
Build 必须失败。
```

禁止自动替换。

---

# 8. 用户工作流

实现：

```text
Tools > MRes > Packer
```

UI：

```text
Source Prefab
Lua Script

Metadata
├─ Name
├─ Description
├─ Value
└─ Type [Medicine / Device]

Output Path

[Analyze]
[Build MRes]
```

流程：

```text
选择 Prefab
↓
递归扫描 Hierarchy
↓
检查支持组件，并拒绝不在 v1 白名单内的组件
↓
收集 Mesh
↓
收集 Material
↓
Shader -> shaderId
↓
收集 Texture
↓
读取 Lua
↓
生成 Metadata
↓
生成 Prefab Schema
↓
Build .mres
```

---

# 9. `.mres v1` 文件结构

MRes v1 **不使用 SHA、CRC 或其他 Hash 字段**。完整性检查只做文件结构、范围和内部引用合法性校验。

所有整数：

```text
Little Endian
```

所有字符串：

```text
UTF-8，无 BOM
```

结构：

```text
Header

Data Region
├─ manifest.json
├─ prefab/root.json
├─ meshes/*.meshbin
├─ materials/*.json
├─ textures/*
├─ scripts/main.lua
└─ metadata/metadata.json

TOC
```

以上均为 `.mres` 内部虚拟路径。

---

# 10. Header

固定 28 bytes：

| 字段 | 类型 | 说明 |
|---|---|---|
| Magic | 4 bytes | ASCII `MRES` |
| Version | UInt16 | 固定为 1 |
| Flags | UInt16 | v1 固定为 0 |
| EntryCount | UInt32 | Entry 数量 |
| TocOffset | UInt64 | TOC 绝对偏移 |
| TocSize | UInt64 | TOC 大小 |

---

# 11. TOC Entry

| 字段 | 类型 |
|---|---|
| PathLength | UInt32 |
| Path | UTF-8 byte[] |
| Type | Byte |
| Compression | Byte |
| Reserved | UInt16 |
| Offset | UInt64 |
| Size | UInt64 |

v1：

```text
Compression = 0
Reserved = 0
```

Entry Type：

```csharp
public enum MResEntryType : byte
{
    Manifest = 1,
    Prefab = 2,
    Mesh = 3,
    Material = 4,
    Texture = 5,
    Lua = 6,
    Metadata = 7,
    PhysicsMaterial = 8,
    Other = 255
}
```

---

# 12. Manifest

示例：

```json
{
  "format": "mres",
  "version": 1,
  "packageName": "BouncePad",

  "prefab": "prefab/root.json",

  "meshes": [
    "meshes/0.meshbin"
  ],

  "materials": [
    "materials/0.json"
  ],

  "textures": [
    "textures/0.png"
  ],

  "physicsMaterials": [
    "physics_materials/0.json"
  ],

  "lua": "scripts/main.lua",
  "metadata": "metadata/metadata.json"
}
```

禁止出现：

```text
Unity GUID
AssetDatabase Path
绝对路径
```

---

# 13. Prefab Schema

“Prefab” 指 MRes 自定义层级描述，不是 Unity `.prefab` 文件。

示例：

```json
{
  "nodes": [
    {
      "id": 0,
      "parentId": -1,
      "name": "BouncePad",
      "active": true,

      "transform": {
        "localPosition": [0, 0, 0],
        "localRotation": [0, 0, 0, 1],
        "localScale": [1, 1, 1]
      },

      "components": [
        {
          "type": "BoxCollider",
          "center": [0, 0.2, 0],
          "size": [2, 0.4, 2],
          "isTrigger": false,
          "enabled": true
        },

        {
          "type": "Rigidbody",
          "mass": 1.0,
          "drag": 0.0,
          "angularDrag": 0.05,
          "useGravity": true,
          "isKinematic": true
        }
      ]
    },

    {
      "id": 1,
      "parentId": 0,
      "name": "Model",
      "active": true,

      "transform": {
        "localPosition": [0, 0, 0],
        "localRotation": [0, 0, 0, 1],
        "localScale": [1, 1, 1]
      },

      "components": [
        {
          "type": "MeshFilter",
          "mesh": "meshes/0.meshbin"
        },

        {
          "type": "MeshRenderer",
          "materials": [
            "materials/0.json"
          ]
        }
      ]
    }
  ]
}
```

---

# 14. Transform

每个 Node 保存：

```text
name
active
parentId
localPosition
localRotation
localScale
```

Quaternion 顺序：

```text
x, y, z, w
```

---

# 15. Mesh 序列化

禁止保存 OBJ / FBX。

直接保存 Unity Mesh 数据。

至少保存：

```text
name
indexFormat

vertices
normals
tangents
uv0
uv1
colors

subMeshCount

每个 SubMesh：
    topology
    indices

bounds
```

建议使用自定义 `.meshbin`。

Mesh 不可读时：

```text
Build 失败，并提示具体 Mesh。
```

---

# 16. Material Schema

示例：

```json
{
  "name": "BouncePadMaterial",
  "shaderId": "MRes/PBR",
  "renderQueue": 2000,
  "enableInstancing": false,
  "doubleSidedGI": false,

  "keywords": [
    "_NORMALMAP"
  ],

  "properties": [
    {
      "name": "_BaseColor",
      "type": "Color",
      "value": [1, 0.2, 0.1, 1]
    },
    {
      "name": "_Smoothness",
      "type": "Float",
      "value": 0.5
    },
    {
      "name": "_BaseMap",
      "type": "Texture",
      "texture": "textures/0.png",
      "scale": [1, 1],
      "offset": [0, 0]
    }
  ]
}
```

支持：

```text
Float
Range
Int
Color
Vector
Texture
```

---

# 17. Texture

Texture 嵌入 `.mres`。

v1 推荐统一转换为：

```text
PNG
RGBA32
```

不可读 Texture 通过：

```text
Texture
↓
RenderTexture
↓
ReadPixels
↓
Texture2D
↓
EncodeToPNG
```

无需用户手动打开 Read/Write。

每张 Texture 保存：

```json
{
  "name": "BouncePad_BaseColor",
  "linear": false,
  "wrapModeU": "Repeat",
  "wrapModeV": "Repeat",
  "filterMode": "Bilinear",
  "anisoLevel": 1,
  "mipmap": true
}
```

---

# 18. Collider

Collider 的目标不是机械序列化所有 public property，而是：

> 保存所有“可配置、会影响物理行为、且可以在目标项目中恢复”的属性。

以下运行时派生属性不序列化：

```text
bounds
attachedRigidbody
attachedArticulationBody
```

这些由 Unity 在重建组件后自动计算或建立引用。

## 18.1 Collider 公共属性

所有 Collider 都必须保存以下公共属性：

```text
enabled
isTrigger
contactOffset
physicsMaterial
includeLayers
excludeLayers
layerOverridePriority
providesContacts
hasModifiableContacts
```

其中：

```text
physicsMaterial
```

引用 `.mres` 内部：

```text
physics_materials/*.json
```

如果原 Collider 没有 Physics Material，则保存：

```text
physicsMaterial = null
```

注意：

Unity 不同版本中部分 Layer Override / Contact 相关 API 可能不存在。实现时使用 Unity 版本宏做兼容，但 MRes Schema 字段保持稳定。

## 18.2 BoxCollider

额外保存：

```text
center
size
```

示例：

```json
{
  "type": "BoxCollider",
  "enabled": true,
  "isTrigger": false,
  "contactOffset": 0.01,
  "physicsMaterial": "physics_materials/0.json",
  "includeLayers": 0,
  "excludeLayers": 0,
  "layerOverridePriority": 0,
  "providesContacts": false,
  "hasModifiableContacts": false,
  "center": [0, 0.2, 0],
  "size": [2, 0.4, 2]
}
```

## 18.3 SphereCollider

额外保存：

```text
center
radius
```

## 18.4 CapsuleCollider

额外保存：

```text
center
radius
height
direction
```

## 18.5 MeshCollider

额外保存：

```text
mesh
convex
```

其中：

```text
mesh
```

必须引用 `.mres` 内部 Mesh。

## 18.6 PhysicsMaterial

Physics Material 必须作为独立资源打包。

内部路径：

```text
physics_materials/<id>.json
```

至少保存：

```json
{
  "name": "Bouncy",
  "dynamicFriction": 0.6,
  "staticFriction": 0.6,
  "bounciness": 0.8,
  "frictionCombine": "Average",
  "bounceCombine": "Maximum"
}
```

字段：

```text
name
dynamicFriction
staticFriction
bounciness
frictionCombine
bounceCombine
```

同一个 Physics Material 只打包一次。

---

# 19. Rigidbody

至少保存：

```text
mass
drag
angularDrag
useGravity
isKinematic
interpolation
collisionDetectionMode
constraints
```

Schema 字段固定，不随 Unity API 名称变化。

---

# 20. Lua

Lua 作为 `.mres` Package 级资源保存，不属于 Prefab Schema。

保存路径：

```text
scripts/main.lua
```

Lua 直接保存 UTF-8 源码。

示例：

```lua
function Start()
    print("MRes object started")
end

function OnCollisionEnter(other)
    other:AddForce(0, 10, 0)
end
```

`manifest.json` 中通过：

```json
{
  "lua": "scripts/main.lua"
}
```

建立关联。

Packer 不执行 Lua，不创建 xLua 环境，也不要求源 Prefab 存在任何 Lua 组件。

---

# 21. Metadata

固定输出：

```text
metadata/metadata.json
```

示例：

```json
{
  "name": "弹射装置",
  "description": "碰撞后将目标向上弹射。",
  "value": 120,
  "type": "device"
}
```

---

# 22. 资源去重

同一个：

```text
Mesh
Material
Texture
PhysicsMaterial
```

只打包一次.

建议：

```text
Mesh InstanceID -> MRes Path
Material InstanceID -> MRes Path
Texture InstanceID -> MRes Path
PhysicsMaterial InstanceID -> MRes Path
```

---

# 23. Writer 流程

```text
创建 .tmp
↓
写 Header 占位
↓
写所有 Entry
↓
记录每个 Entry 的 Path / Type / Offset / Size
↓
写 TOC
↓
回填 Header
↓
Flush
↓
重新读取 Header / TOC 做结构自校验
↓
Rename 为 .mres
```

失败时不能留下最终文件。

结构自校验至少检查：

```text
Magic
Version
EntryCount
TocOffset / TocSize
每个 Entry 的 Offset / Size 是否越界
虚拟路径是否重复
内部引用是否存在
```

---

# 24. Analyze

必须显示：

```text
Node Count
Mesh Count
Material Count
Texture Count
PhysicsMaterial Count
Collider Count
Rigidbody Count
Lua
Metadata
Shader IDs
Unsupported Components
Estimated Size
```

存在不支持组件时，v1 默认禁止 Build。

特别注意：源 Prefab 不需要任何 MRes/xLua 专用 MonoBehaviour；如果存在此类组件，应作为 Unsupported Component 处理。

---

# 25. 推荐代码结构

```text
Assets/
  MResPacker/
    Editor/
      MResPackerWindow.cs
      MResBuildPipeline.cs

      Format/
        MResFormat.cs
        MResWriter.cs

      Prefab/
        MResPrefabSerializer.cs
        MResNodeSerializer.cs

      Resources/
        MResMeshSerializer.cs
        MResMaterialSerializer.cs
        MResTextureSerializer.cs
        MResPhysicsMaterialSerializer.cs

      Components/
        ColliderSerializer.cs
        RigidbodySerializer.cs

      Shader/
        MResShaderRegistry.cs

      Metadata/
        MResMetadataData.cs
        MResMetadataValidator.cs

      Validation/
        MResPrefabValidator.cs
```

---

# 26. 验收标准

必须至少通过：

### Test A
静态 Mesh + Material Prefab 成功打包。

### Test B
多层 Hierarchy 正确。

### Test C
多 SubMesh / 多 Material Slot 顺序保持。

### Test D
Box / Sphere / Capsule / MeshCollider 正确序列化，并包含所有 MRes v1 规定的公共 Collider 属性。

### Test E
PhysicsMaterial 的摩擦、弹性和 Combine Mode 正确序列化，并能被多个 Collider 共享引用。

### Test F
Rigidbody 参数正确.

### Test G
Lua 源码正确嵌入，并且 Prefab Schema 中不存在任何 LuaBehaviour / RuntimeBehaviour 组件记录。

### Test H
Metadata 四字段正确写入。

### Test I
`type` 非 `medicine/device` 时拒绝 Build。

### Test J
Shader 未注册 shaderId 时拒绝 Build。

### Test K
修改 TOC 中某个 Entry 的 `Offset` 或 `Size` 使其越界后，结构校验必须失败。

---

# 27. 完成定义

Codex 最终交付：

- 独立 Unity Editor Packer。
- Packer EditorWindow。
- MRes v1 Writer。
- Prefab Schema Serializer。
- Mesh Serializer。
- Material Serializer。
- Texture Serializer。
- PhysicsMaterial Serializer。
- Collider / Rigidbody Serializer。
- Shader Registry。
- Lua 打包。
- Metadata 固定 Schema。
- Header / TOC 结构校验。
- Analyze + Validation。
- 示例 Prefab。
- README。

**不要实现 Loader。**
