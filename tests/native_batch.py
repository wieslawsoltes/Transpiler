"""C++20 integral libraries: the same PE is the CoreCLR and native compiler input."""
import json
import itertools
import random
import shutil


def register(h):
    for debug in (False, True):
        h.record('native/scalar-' + ('debug' if debug else 'release'), lambda debug=debug: differential(h, debug))
    h.record('native/raw-ssa', lambda: raw(h))
    h.record('native/rejections', lambda: rejections(h))
    h.record('native/linked-library', lambda: linked(h))


def fixture():
    methods, calls = [], []
    types = [('int', -2147483648, 2147483647), ('uint', 0, 4294967295),
             ('long', -9223372036854775808, 9223372036854775807), ('ulong', 0, 18446744073709551615)]
    def add(name, result, parameters, body, vectors):
        methods.append(f'public static {result} {name}({", ".join(t + " " + n for t, n in parameters)}) {{ {body} }}')
        calls.extend((name, result, parameters, tuple(v)) for v in vectors)
    for t, lo, hi in types:
        values = sorted(set([lo, hi, 0, 1, 2, 17, hi // 2] + ([-1, -2, -17] if lo < 0 else [])))
        pairs = [(a, b) for a in values for b in values]
        for name, op in [('Add','+'), ('Sub','-'), ('Mul','*')]:
            for check in (False, True):
                add(t + name + str(int(check)), t, [(t,'a'),(t,'b')],
                    f'return {"checked" if check else "unchecked"}(a {op} b);', pairs)
        for name, op in [('Div','/'),('Rem','%'),('And','&'),('Or','|'),('Xor','^')]:
            add(t + name, t, [(t,'a'),(t,'b')], f'return a {op} b;', pairs)
        for name, op in [('Less','<'),('Equal','=='),('Greater','>'),('LessEqual','<='),('GreaterEqual','>='),('NotEqual','!=')]:
            add(t + name, 'bool', [(t,'a'),(t,'b')], f'return a {op} b;', pairs[::5])
        for name, op in [('Left','<<'),('Right','>>'),('Logical','>>>')]:
            add(t + name, t, [(t,'a'),('int','b')], f'return a {op} b;', [(a,b) for a in values for b in [-129,-65,-33,-1,0,1,31,32,63,64,129]])
        add(t + 'Not', t, [(t,'a')], 'return ~a;', [(a,) for a in values])
        if lo < 0: add(t + 'Neg', t, [(t,'a')], 'return unchecked(-a);', [(a,) for a in values])
        for destination in ['sbyte','byte','short','ushort','int','uint','long','ulong','char']:
            for check in (False, True):
                add(t + 'To' + destination + str(int(check)), destination, [(t,'a')],
                    f'return {"checked" if check else "unchecked"}(({destination})a);', [(a,) for a in values])
        add(t + 'UnusedChecked', 'void', [(t,'a'),(t,'b')], '_ = checked(a + b);', pairs[::7])
        add(t + 'UnusedDivision', 'void', [(t,'a'),(t,'b')], '_ = a / b;', pairs[::7])
    add('Gcd', 'int', [('int','a'),('int','b')], 'while(b!=0){ int r=a%b; a=b; b=r; } return a;', [(84,30),(17,5),(0,9)])
    add('Recursive', 'long', [('int','n')], 'return n < 2 ? 1L : n * Recursive(n - 1);', [(0,),(1,),(12,),(20,)])
    add('Select', 'int', [('int','n')], 'switch(n){case 0:return 11; case 1:return 17; case 2:return 23; case 3:return 31; default:return -9;}', [(i,) for i in [-1,0,1,2,3,4,2147483647]])
    add('Ternary', 'long', [('bool','flag'),('long','a'),('long','b')], 'return flag ? a : b;', [(True,-9,17),(False,-9,17)])
    add('Forward', 'int', [('int','a')], 'return Gcd(a,30);', [(84,),(17,)])
    add('Ignored', 'int', [('int','unused')], 'return 37;', [(1,)])
    add('Α__symbol_', 'int', [('int','a')], 'return a;', [(9,)])
    randomizer = random.Random(0xC1120)
    for number in range(32):
        body = 'return unchecked('
        expression = 'a'
        for step in range(10):
            op = randomizer.choice(['+','-','*','&','|','^','<<','>>','>>>'])
            b = randomizer.randrange(-130,130) if '<' in op or '>' in op else randomizer.randrange(-2147483648,2147483648)
            expression = f'({expression} {op} ({b}))'
        body += expression + ');'
        add('Random' + str(number), 'int', [('int','a')], body, [(v,) for v in [-2147483648,-1,0,1,2147483647]])
    return 'public static class Kernels {\n' + '\n'.join(methods) + '\n}', calls


def cs_literal(value, type):
    if type == 'bool': return 'true' if value else 'false'
    suffix = {'int':'', 'uint':'U', 'long':'L', 'ulong':'UL'}[type]
    return str(value) + suffix


def cpp_literal(value, type):
    if type == 'bool': return 'true' if value else 'false'
    native = {'int':'std::int32_t','uint':'std::uint32_t','long':'std::int64_t','ulong':'std::uint64_t'}[type]
    if value == -9223372036854775808: return 'std::numeric_limits<std::int64_t>::min()'
    return native + '{' + str(value) + ('ULL' if type == 'ulong' else 'LL' if type == 'long' else 'U' if type == 'uint' else '') + '}'


def differential(h, debug):
    d = h.OUT / ('native-debug' if debug else 'native-release'); d.mkdir(parents=True, exist_ok=True)
    source, calls = fixture(); cs = d / 'NativeKernels.cs'; cs.write_text(source, encoding='utf-8')
    pe = d / 'NativeKernels.dll'; h.cli('emit-pe', cs, '--library', '--out', pe, *(['--debug'] if debug else []))
    header, manifest = d / 'kernels.hpp', d / 'native.json'
    response = json.loads(h.cli('compile', pe, '--target', 'native-std', '--out', header, '--manifest', manifest).stdout)
    assert response['profile'] == 'native-std-scalar-v1' and not response['SsaFallbacks']
    h.cli('compile', pe, '--target', 'cpp', '--out', d / 'repeat.hpp')
    assert header.read_bytes() == (d / 'repeat.hpp').read_bytes()
    assert 'CliRuntime' not in header.read_text() and 'std::vector' not in header.read_text()
    exports = {e['method'].split('::')[-1].split('(')[0]: e['symbol'] for e in json.loads(manifest.read_text())['nativeExports']}
    cs_tests, cpp_calls = [], []
    for group_index, (_, grouped) in enumerate(itertools.groupby(calls, key=lambda c: c[0])):
        group = list(grouped); name, result, parameters, _ = group[0]
        cs_arrays, cpp_arrays = [], []
        for slot, (type, _) in enumerate(parameters):
            values = [c[3][slot] for c in group]
            cs_arrays.append(f'{type}[] a{slot} = new {type}[] {{' + ','.join(cs_literal(v,type) for v in values) + '};')
            native = {'int':'std::int32_t','uint':'std::uint32_t','long':'std::int64_t','ulong':'std::uint64_t','bool':'bool'}[type]
            cpp_arrays.append(f'const {native} a{slot}[] = {{' + ','.join(cpp_literal(v,type) for v in values) + '};')
        arguments = ','.join(f'a{slot}[i]' for slot in range(len(parameters)))
        call = 'Kernels.' + name + '(' + arguments + ')'
        if result == 'void': invoke = call + '; System.Console.WriteLine("ok");'
        else: invoke = 'System.Console.WriteLine(' + ('(int)' if result == 'char' else '') + call + ');'
        cs_tests.append(f'static void Test{group_index}() {{' + '\n'.join(cs_arrays) + f'for(int i=0;i<{len(group)};i++)' +
            '{try { ' + invoke + ' } catch(System.Exception error){ System.Console.WriteLine(error.GetType().Name); }}}')
        cpp_calls.append('{' + '\n'.join(cpp_arrays) + f'for(int i=0;i<{len(group)};i++) check([&](){{return ' + exports[name] + '(' + arguments + ');});}')
    oracle_source = d / 'Oracle.cs'; oracle_source.write_text('public static class Oracle { public static void Main(){' +
        ''.join(f'Test{i}();' for i in range(len(cs_tests))) + '}\n' + '\n'.join(cs_tests) + '\n}')
    oracle_pe = d / 'Oracle.dll'; h.cli('emit-pe', oracle_source, '--reference', pe, '--out', oracle_pe)
    oracle = h.run('dotnet', oracle_pe).stdout; assert len(oracle.splitlines()) == len(calls)
    driver = d / 'driver.cpp'; driver.write_text('''#include "kernels.hpp"
#include <iostream>
template<class F> void check(F f) {
    try {
        if constexpr(std::is_void_v<decltype(f())>) { f(); std::cout << "ok\\n"; }
        else { const auto value = f();
            if constexpr(std::is_same_v<decltype(f()), bool>) std::cout << (value ? "True" : "False") << '\\n';
            else std::cout << +value << '\\n';
        }
    } catch(const std::overflow_error&) { std::cout << "OverflowException\\n"; }
      catch(const std::domain_error&) { std::cout << "DivideByZeroException\\n"; }
}
int main() {
''' + '\n'.join(cpp_calls) + '\n}\n', encoding='utf-8')
    executions = []
    for compiler in ('g++', 'clang++'):
        assert shutil.which(compiler), 'Native conformance requires ' + compiler
        # C++ O0 and O2 across the Debug/Release PE cases; UBSan is a hard gate in both.
        binary = d / (compiler.replace('+','p') + '-driver')
        h.run(compiler, '-std=c++20', '-O0' if debug else '-O2', '-Wall', '-Wextra', '-Werror', '-fsanitize=undefined',
              '-fno-sanitize-recover=all', driver, '-o', binary)
        actual = h.run(binary)
        if actual.stdout != oracle:
            expected_lines, actual_lines = oracle.splitlines(), actual.stdout.splitlines()
            mismatch = next((i for i,(a,b) in enumerate(zip(expected_lines,actual_lines)) if a!=b), min(len(expected_lines),len(actual_lines)))
            raise AssertionError(f'{compiler}: case {mismatch} {calls[mismatch]}: {expected_lines[mismatch:mismatch+1]} != {actual_lines[mismatch:mismatch+1]}')
        assert not actual.stderr, actual.stderr
        executions.append(dict(compiler=compiler, version=h.run(compiler,'--version').stdout.splitlines()[0], cases=len(calls), undefinedBehaviorSanitizer=True))
    return dict(executions=executions, methods=response['MethodCount'], headerBytes=header.stat().st_size, profile=response['profile'])


def rejections(h):
    d = h.OUT / 'native-rejections'; d.mkdir(parents=True, exist_ok=True)
    sources = {
        'entry':'public static class K { public static void Main() {} }',
        'objects':'public static class K { public static object Echo(object a) => a; }',
        'arrays':'public static class K { public static int Read(int[] a) => a[0]; }',
        'byref':'public static class K { public static int Read(ref int a) => a; }',
        'float':'public static class K { public static double Add(double a,double b) => a+b; }',
        'handlers':'public static class K { public static int Div(int a,int b) { try { return a/b; } catch { return 0; } } }',
        'initialization':'public static class K { static int v; static K(){v=7;} public static int Read() => v; }',
        'intrinsics':'public static class K { public static void Write(int n) => System.Console.WriteLine(n); }',
        'dead-allocation':'public static class K { public static void Unused(){ _ = new int[3]; } }',
        'instance':'public class K { public int Read() => 7; public static int Use() => new K().Read(); }',
    }
    diagnostics = {}
    for name, source in sources.items():
        cs = d / (name + '.cs'); cs.write_text(source)
        output, diagnostic = d / (name + '.hpp'), d / (name + '.json'); output.unlink(missing_ok=True)
        r = h.cli('compile', cs, '--target','native-std','--out',output,'--diagnostics',diagnostic,
                  *([] if name=='entry' else ['--library']), expected=None)
        assert r.returncode == 1 and not output.exists(), r.stderr
        codes = {e['code'] for e in json.loads(diagnostic.read_text())}; assert 'TR2300' in codes, r.stderr
        diagnostics[name] = sorted(codes)
    return dict(diagnostics=diagnostics)


def linked(h):
    d = h.OUT / 'native-linked'; d.mkdir(parents=True, exist_ok=True)
    (d/'NativeDependency.cs').write_text('public static class Dependency { public static long Double(long a) => unchecked(a+a); }')
    (d/'NativeRoot.cs').write_text('public static class Root { public static long Transform(long a) => Dependency.Double(a)+1; }')
    dependency, root = d/'NativeDependency.dll', d/'NativeRoot.dll'
    h.cli('emit-pe',d/'NativeDependency.cs','--library','--out',dependency)
    h.cli('emit-pe',d/'NativeRoot.cs','--reference',dependency,'--library','--out',root)
    h.cli('compile',root,'--reference',dependency,'--target','native-std','--out',d/'linked.hpp','--manifest',d/'manifest.json')
    manifest = json.loads((d/'manifest.json').read_text()); exports=manifest['nativeExports']; assert len(exports)==1
    (d/'driver.cpp').write_text('#include "linked.hpp"\n#include <iostream>\nint main(){std::cout << '+exports[0]['symbol']+'(9223372036854775807LL) << "\\n";}')
    h.run('g++','-std=c++20','-fsanitize=undefined','-fno-sanitize-recover=all',d/'driver.cpp','-o',d/'driver')
    assert h.run(d/'driver').stdout=='-1\n'
    return dict(exports=exports, assemblies=len(manifest['assemblies']))


def raw(h):
    d = h.OUT / 'native-raw'; d.mkdir(parents=True, exist_ok=True)
    checks = h.ROOT / 'tests/CompilerChecks/bin/Release/net10.0/CompilerChecks.dll'
    pe = d/'SsaFixture.dll'; h.run('dotnet',checks,'--emit-ssa-library',pe)
    h.cli('compile',pe,'--target','native-std','--out',d/'raw.hpp','--manifest',d/'manifest.json')
    exports=json.loads((d/'manifest.json').read_text())['nativeExports']
    cs, cpp = [], []
    for export in exports:
        name=export['method'].split('::')[-1].split('(')[0]
        if export['returnType']=='void':
            cs.append('try { type.GetMethod('+json.dumps(name)+')!.Invoke(null,null); System.Console.WriteLine("MISSING"); } catch(System.Reflection.TargetInvocationException e){System.Console.WriteLine(e.InnerException!.GetType().Name);}')
            cpp.append('try { '+export['symbol']+'(); std::cout << "MISSING\\n"; } catch(const std::overflow_error&) {std::cout << "OverflowException\\n";} catch(const std::domain_error&) {std::cout << "DivideByZeroException\\n";}')
        else:
            cs.append('System.Console.WriteLine(type.GetMethod('+json.dumps(name)+')!.Invoke(null,null));')
            cpp.append('std::cout << '+export['symbol']+'() << "\\n";')
    (d/'Oracle.cs').write_text('public static class Oracle { public static void Main(string[] args){ var type = System.Reflection.Assembly.LoadFrom(args[0]).GetType("Program")!;'+ '\n'.join(cs) +'}}')
    h.cli('emit-pe',d/'Oracle.cs','--out',d/'Oracle.dll')
    expected=h.run('dotnet',d/'Oracle.dll',pe).stdout
    (d/'driver.cpp').write_text('#include "raw.hpp"\n#include <iostream>\nint main(){'+ '\n'.join(cpp) +'}')
    for compiler in ('g++','clang++'):
        h.run(compiler,'-std=c++20','-O2','-Wall','-Wextra','-Werror','-fsanitize=undefined','-fno-sanitize-recover=all',d/'driver.cpp','-o',d/'driver')
        result=h.run(d/'driver');assert result.stdout==expected and not result.stderr
    return dict(methods=len(exports),randomMethods=128,stackCarryingLoop=True,unusedThrowingResults=3,compilers=2)
