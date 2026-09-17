"""Execute an unchanged precompiled consumer through replacement type-forwarding facades."""
import json
import sys
import shutil


def register(h):
    h.record('linking/type-forwarding',lambda:run(h))
    h.record('compiler/structural-identities',lambda:checks(h))


def run(h):
    d=h.OUT/'forwarding';d.mkdir(parents=True,exist_ok=True)
    old=d/'old';old.mkdir(exist_ok=True)
    body='''namespace Api {
public class Box<T>{public T Value;public Box(T value){Value=value;}public virtual T Get()=>Value;}
public class Outer{public class Inner{public int N;public Inner(int n){N=n;}}}
public struct Pair{public int X;public Pair(int x){X=x;}}
public static class Functions{public static T Echo<T>(T x)=>x;public static void Set(ref Pair p){p.X=99;}}
}'''
    (old/'Contracts.cs').write_text(body)
    h.cli('emit-pe',old/'Contracts.cs','--library','--out',old/'Contracts.dll')
    (d/'App.cs').write_text('''using System;using Api;
public class Derived:Box<long>{public Derived(long n):base(n){}public override long Get()=>base.Get()+7;}
public static class Program{public static void Main(){Box<long> value=new Derived(9007199254740993L);
Console.WriteLine(value.Get());Console.WriteLine(new Outer.Inner(17).N);
var p=new Pair(1);Functions.Set(ref p);Console.WriteLine(p.X);
var a=new Box<int>[2];a[1]=new Box<int>(42);Console.WriteLine(Functions.Echo(a)[1].Value);
Console.WriteLine(typeof(Box<int>)==new Box<int>(0).GetType());}}''')
    h.cli('emit-pe',d/'App.cs','--reference',old/'Contracts.dll','--out',d/'App.dll')
    (d/'Destination.cs').write_text(body)
    h.cli('emit-pe',d/'Destination.cs','--library','--out',d/'Destination.dll')
    (d/'Contracts.cs').write_text('''using System.Runtime.CompilerServices;
[assembly:TypeForwardedTo(typeof(Api.Box<>))]
[assembly:TypeForwardedTo(typeof(Api.Outer))]
[assembly:TypeForwardedTo(typeof(Api.Pair))]
[assembly:TypeForwardedTo(typeof(Api.Functions))]
''')
    h.cli('emit-pe',d/'Contracts.cs','--library','--reference',d/'Destination.dll','--out',d/'Contracts.dll')
    expected=h.run('dotnet',d/'App.dll').stdout
    assert expected=='9007199254741000\n17\n99\n42\nTrue\n',expected
    counts=[]
    for target,ext,host in [('js','mjs','node'),('py','py',sys.executable)]:
        output=d/('app.'+ext);manifest=d/(target+'.json')
        h.cli('compile',d/'App.dll','--reference',d/'Contracts.dll','--reference',d/'Destination.dll','--target',target,'--out',output,'--manifest',manifest)
        assert h.run(host,output).stdout==expected
        data=json.loads(manifest.read_text());counts.append(len(data['forwardings']))
        assert any('Outer+Inner' in x['source'] for x in data['forwardings'])
        repeat=d/('repeat.'+ext)
        h.cli('compile',d/'App.dll','--reference',d/'Destination.dll','--reference',d/'Contracts.dll','--target',target,'--out',repeat)
        assert output.read_bytes()==repeat.read_bytes()
        missing=d/('missing.'+ext);missing.unlink(missing_ok=True)
        result=h.cli('compile',d/'App.dll','--reference',d/'Contracts.dll','--target',target,'--out',missing,expected=None)
        assert result.returncode==1 and 'TR3012' in result.stderr and not missing.exists(),result.stderr
    return dict(consumerRecompiled=False,forwardedTypes=counts,coreClrStdout=expected,missingImplementationRejected=True)


def checks(h):
    output=h.run('dotnet',h.ROOT/'tests/CompilerChecks/bin/Release/net10.0/CompilerChecks.dll','--identity').stdout
    assert output == 'Identity checks: 25 passed\n', output
    return dict(structuralAndForwarderAssertions=25)
