# -*- coding: utf-8 -*-
"""Restore the Rukha93 URP character shader set + repoint materials/prefabs.

Why: the pack copy inside the project was imported without the original .meta files,
so Unity generated new GUIDs for every shader -> materials (which reference
ShaderGraph_CharacterLit 7e0b5bad...) could not resolve their shader, and the
character graphs could not resolve SubGraphs / CustomLighting.hlsl.
A previous workaround swapped everything to URP/Lit + used the RGB *mask* as
albedo, which is why the body renders as a flat red blob.
"""
import tarfile, os, re, glob

PROJ   = r"D:\workspace\unity project\干预\3D剧情项目"
ASSETS = os.path.join(PROJ, "Assets")
PKG    = r"C:\Users\Administrator\Downloads\干预素材\角色套件\Update to URP.unitypackage"
SHADER_DIR = os.path.join(ASSETS, "assets", "11_着色器_Shaders")

CHAR_LIT_GUID = "7e0b5bad5c3d5464fa13085f40bcdbdb"   # ShaderGraph_CharacterLit (what the pack materials reference)
OLD_TOON_GUID = "53d69bbe7cd9707439d02b03a78c58f6"   # CharacterToon+FakeOutline copy made by previous workaround

SHADER_MAP = {
    "ShaderGraph_CharacterLit.shadergraph": "主着色器_ShaderGraph",
    "ShaderGraph_CharacterToon.shadergraph": "主着色器_ShaderGraph",
    "ShaderGraph_CharacterToon+FakeOutline.shadergraph": "主着色器_ShaderGraph",
    "ShaderGraph_ToonBasic.shadergraph": "主着色器_ShaderGraph",
    "SubGraph_ColorCustomization.shadersubgraph": "子图_SubGraph",
    "SubGraph_Rimlight.shadersubgraph": "子图_SubGraph",
    "SubGraph_ToonShading.shadersubgraph": "子图_SubGraph",
    "CustomLighting.hlsl": "支持文件",
    "GetMainLightDataSRP.hlsl": "支持文件",
    "ColorRamp.png": "支持文件",
}

log = []

# ---------------------------------------------------------------- 1) shaders
t = tarfile.open(PKG, "r:gz")
g2p = {}
for n in t.getnames():
    if n.endswith("/pathname"):
        g2p[n.split("/")[0]] = t.extractfile(n).read().decode().strip()

n_sh = 0
for g, p in g2p.items():
    base = os.path.basename(p)
    if "/Assets/Shaders/" in p and base in SHADER_MAP:
        dst = os.path.join(SHADER_DIR, SHADER_MAP[base], base)
        with open(dst, "wb") as f:
            f.write(t.extractfile(g + "/asset").read())
        with open(dst + ".meta", "wb") as f:
            f.write(t.extractfile(g + "/asset.meta").read())
        n_sh += 1
        log.append("shader  %-46s guid=%s" % (base, g))
log.append("shaders restored: %d" % n_sh)

# ------------------------------------------------- 2) repoint all pack materials
n_mat = 0
for mat in glob.glob(os.path.join(ASSETS, "assets", "Materials", "**", "*.mat"), recursive=True):
    txt = open(mat, encoding="utf-8", errors="ignore").read()
    if OLD_TOON_GUID in txt:
        txt = txt.replace("guid: " + OLD_TOON_GUID, "guid: " + CHAR_LIT_GUID)
        open(mat, "w", encoding="utf-8", newline="").write(txt)
        n_mat += 1
log.append("materials repointed to CharacterLit: %d" % n_mat)

# ------------------------------------------------- 3) material guid lookup
def guid_of(meta_path):
    m = re.search(r"guid: ([0-9a-f]{32})", open(meta_path, encoding="utf-8", errors="ignore").read())
    return m.group(1) if m else None

repo_by_norm = {}
for meta in glob.glob(os.path.join(ASSETS, "assets", "Materials", "**", "*.mat.meta"), recursive=True):
    g = guid_of(meta)
    name = os.path.basename(meta)[:-9]          # strip .mat.meta
    norm = re.sub(r"[^a-z0-9]", "", name.lower())
    repo_by_norm[norm] = (name, g)

def norm(s):
    return re.sub(r"[^a-z0-9]", "", s.lower())

# ------------------------------------------------- 4) FBX importers -> pack materials
n_fbx = 0
for meta in glob.glob(os.path.join(ASSETS, "assets", "组合角色", "*", "*.fbx.meta")):
    fbx = meta[:-5]
    if not os.path.exists(fbx):
        continue
    names = sorted({x.decode() for x in re.findall(rb"Material::([A-Za-z0-9_.\- ]+)", open(fbx, "rb").read())})
    entries = []
    for nm in names:
        hit = repo_by_norm.get(norm(nm))
        if hit:
            entries.append("  - first:\n"
                           "      type: UnityEngine:Material\n"
                           "      assembly: UnityEngine.CoreModule\n"
                           "      name: %s\n"
                           "    second: {fileID: 2100000, guid: %s, type: 2}" % (nm, hit[1]))
    block = "  externalObjects:\n" + "\n".join(entries) + "\n"
    txt = open(meta, encoding="utf-8", errors="ignore").read()
    new, cnt = re.subn(r"  externalObjects:.*?\n  materials:", block + "  materials:", txt, flags=re.S)
    if cnt:
        open(meta, "w", encoding="utf-8", newline="").write(new)
        n_fbx += 1
        log.append("fbx     %-20s -> %d materials" % (os.path.basename(fbx), len(entries)))
log.append("fbx metrics patched: %d" % n_fbx)

# ------------------------------------------------- 5) character prefabs -> pack materials
# map Materials_URPLit guid -> Materials guid (same relative path + file name)
map_guids = {}
for meta in glob.glob(os.path.join(ASSETS, "assets", "Materials_URPLit", "**", "*.mat.meta"), recursive=True):
    rel = os.path.relpath(meta, os.path.join(ASSETS, "assets", "Materials_URPLit"))
    rel = rel[:-9]                                # strip .mat.meta
    toon = os.path.join(ASSETS, "assets", "Materials", rel) + ".meta"
    if os.path.exists(toon):
        map_guids[guid_of(meta)] = guid_of(toon)

n_pre = 0
for pre in glob.glob(os.path.join(ASSETS, "assets", "角色_URP", "*_可动.prefab")):
    txt = open(pre, encoding="utf-8", errors="ignore").read()
    orig = txt
    for a, b in map_guids.items():
        txt = txt.replace("guid: " + a, "guid: " + b)
    if txt != orig:
        open(pre, "w", encoding="utf-8", newline="").write(txt)
        n_pre += 1
log.append("prefabs repointed to pack materials: %d (map size %d)" % (n_pre, len(map_guids)))

print("\n".join(log))
