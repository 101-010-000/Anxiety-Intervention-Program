# -*- coding: utf-8 -*-
"""Minimal binary FBX reader: extract Geometry vertex bounds for each part,
so we know sleeve / leg coverage without opening Unity."""
import struct, sys, os, glob

class Node:
    __slots__ = ("name","props","children")
    def __init__(self,name,props,children):
        self.name=name; self.props=props; self.children=children

def read_node(f, wide):
    off = struct.unpack("<Q" if wide else "<I", f.read(8 if wide else 4))[0]
    if off == 0:
        f.read(17 if wide else 9)   # rest of the 25/13 byte null record
        return None
    nprops, plen = struct.unpack("<II", f.read(8))
    nlen = struct.unpack("<B", f.read(1))[0]
    name = f.read(nlen).decode("utf-8", "ignore")
    props=[]
    end = off  # absolute end offset
    # parse properties
    for _ in range(nprops):
        t = f.read(1)
        if t == b"Y": props.append(struct.unpack("<h", f.read(2))[0])
        elif t == b"C": props.append(struct.unpack("<b", f.read(1))[0])
        elif t == b"I": props.append(struct.unpack("<i", f.read(4))[0])
        elif t == b"F": props.append(struct.unpack("<f", f.read(4))[0])
        elif t == b"D": props.append(struct.unpack("<d", f.read(8))[0])
        elif t == b"L": props.append(struct.unpack("<q", f.read(8))[0])
        elif t in (b"f", b"d", b"l", b"i", b"b"):
            alen, enc, clen = struct.unpack("<III", f.read(12))
            raw = f.read(clen)
            if enc: raw = __import__("zlib").decompress(raw)
            fmt = {b"f":"f",b"d":"d",b"l":"q",b"i":"i",b"b":"b"}[t]
            sz = struct.calcsize(fmt)
            props.append(list(struct.unpack("<%d%s" % (alen, fmt), raw[:alen*sz])))
        elif t in (b"S", b"R"):
            ln = struct.unpack("<I", f.read(4))[0]
            props.append(f.read(ln))
        else:
            raise ValueError("unknown prop type %r at %d" % (t, f.tell()))
    children=[]
    while f.tell() < end:
        c = read_node(f, wide)
        if c is None: break
        children.append(c)
    return Node(name, props, children)

def load(path):
    with open(path,"rb") as f:
        head = f.read(21)
        assert head.startswith(b"Kaydara"), head
        f.read(2)
        ver = struct.unpack("<I", f.read(4))[0]
        wide = ver >= 7500
        root = Node("", [], [])
        while True:
            n = read_node(f, wide)
            if n is None: break
            root.children.append(n)
        return root

def find(node, name, out=None):
    if out is None: out=[]
    for c in node.children:
        if c.name == name: out.append(c)
        find(c, name, out)
    return out

def vertex_bounds(path):
    root = load(path)
    geos = find(root, "Geometry")
    if not geos: return None
    verts=[]
    for g in geos:
        for c in g.children:
            if c.name == "Vertices" and c.props:
                verts += c.props[0]
    if not verts: return None
    xs=verts[0::3]; ys=verts[1::3]; zs=verts[2::3]
    return (min(xs),max(xs),min(ys),max(ys),min(zs),max(zs)), len(xs)

if __name__ == "__main__":
    roots = sys.argv[1:] or ["."]
    files=[]
    for r in roots:
        files += glob.glob(os.path.join(r,"**","*.fbx"), recursive=True)
    for p in sorted(files):
        try:
            r = vertex_bounds(p)
        except Exception as e:
            print("%-60s ERR %s" % (p, e)); continue
        if not r: print("%-60s (no geometry)" % p); continue
        (x0,x1,y0,y1,z0,z1), n = r
        print("%-58s verts=%-6d X[%6.3f,%6.3f] Y[%6.3f,%6.3f] Z[%6.3f,%6.3f]  spanX=%.3f" %
              (os.path.basename(p), n, x0,x1,y0,y1,z0,z1, x1-x0))
