"""Minimal reader/writer for Godot .tscn text scenes: keeps everything it doesn't understand verbatim."""
import re, numpy as np
class Section:
    def __init__(s, header, body):
        s.header=header; s.lines=body          # body: list of raw lines (without trailing blank)
        m=re.match(r'\[(\w+)(.*)\]$',header)
        s.kind=m.group(1); s.attrs=dict(re.findall(r'(\w+)=("(?:[^"\\]|\\.)*"|[^\s\]]+)',m.group(2)))
    def attr(s,k):
        v=s.attrs.get(k); return v.strip('"') if v is not None else None
    def get(s,k):
        for l in s.lines:
            if l.startswith(k+' = '): return l[len(k)+3:]
        return None
    def set(s,k,v):
        for i,l in enumerate(s.lines):
            if l.startswith(k+' = '): s.lines[i]=f'{k} = {v}'; return
        s.lines.append(f'{k} = {v}')
    def remove(s,k):
        s.lines=[l for l in s.lines if not l.startswith(k+' = ')]
    @property
    def path(s):
        p=s.attr('parent'); n=s.attr('name')
        if p is None: return '.'
        return n if p=='.' else p+'/'+n
    def text(s): return '\n'.join([s.header]+s.lines)
class Scene:
    def __init__(s,path):
        s.file=path; txt=open(path).read()
        parts=re.split(r'\n(?=\[)',txt.strip('\n'))
        s.sections=[]
        for p in parts:
            ls=p.split('\n'); 
            while ls and ls[-1]=='': ls.pop()
            s.sections.append(Section(ls[0],ls[1:]))
    def nodes(s): return [x for x in s.sections if x.kind=='node']
    def node(s,path): return next((x for x in s.nodes() if x.path==path),None)
    def children(s,path): return [x for x in s.nodes() if x.attr('parent')==path]
    def sub(s,id): return next((x for x in s.sections if x.kind=='sub_resource' and x.attr('id')==id),None)
    def ext(s,path,type):
        for x in s.sections:
            if x.kind=='ext_resource' and x.attr('path')==path: return x.attr('id')
        ids=[x.attr('id') for x in s.sections if x.kind=='ext_resource']
        n=1
        while f'x{n}' in ids: n+=1
        sec=Section(f'[ext_resource type="{type}" path="{path}" id="x{n}"]',[])
        i=max([k for k,x in enumerate(s.sections) if x.kind in('ext_resource','gd_scene')])
        s.sections.insert(i+1,sec); return f'x{n}'
    def add_sub(s,type,id,lines):
        sec=Section(f'[sub_resource type="{type}" id="{id}"]',lines)
        i=max([k for k,x in enumerate(s.sections) if x.kind in('ext_resource','gd_scene','sub_resource')])
        s.sections.insert(i+1,sec); return sec
    def add_node(s,name,type,parent,lines,after=None):
        sec=Section(f'[node name="{name}" type="{type}" parent="{parent}"]',lines)
        if after is None: s.sections.append(sec)
        else: s.sections.insert(s.sections.index(after)+1,sec)
        return sec
    def remove(s,sec): s.sections.remove(sec)
    def prune(s):
        """Drop sub/ext resources nothing refers to any more."""
        changed=True
        while changed:
            changed=False
            body='\n'.join(x.text() for x in s.sections)
            for x in list(s.sections):
                if x.kind=='sub_resource' and f'SubResource("{x.attr("id")}")' not in body: s.sections.remove(x); changed=True
                if x.kind=='ext_resource' and f'ExtResource("{x.attr("id")}")' not in body: s.sections.remove(x); changed=True
    def save(s):
        out=[]
        prev=None
        for x in s.sections:
            if prev is not None and not (prev.kind=='ext_resource' and x.kind=='ext_resource'): out.append('')
            out.append(x.text()); prev=x
        open(s.file,'w').write('\n'.join(out)+'\n')
def parse_xf(v):
    if v is None: return np.eye(3), np.zeros(3)
    a=[float(t) for t in re.search(r'\((.*)\)',v).group(1).split(',')]
    return np.array(a[:9]).reshape(3,3), np.array(a[9:])
def fmt_xf(M,o):
    return 'Transform3D('+', '.join('%.6g'%(0.0 if abs(x)<1e-9 else x) for x in list(M.reshape(-1))+list(o))+')'
def vec(v): return [float(t) for t in re.search(r'\((.*)\)',v).group(1).split(',')]
def rot_y(a):
    c,s=np.cos(a),np.sin(a); return np.array([[c,0,s],[0,1,0],[-s,0,c]])
def rot_x(a):
    c,s=np.cos(a),np.sin(a); return np.array([[1,0,0],[0,c,-s],[0,s,c]])
def rot_z(a):
    c,s=np.cos(a),np.sin(a); return np.array([[c,-s,0],[s,c,0],[0,0,1]])
