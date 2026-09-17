"""Public compiler gates for conservative managed-address and exception-local verification."""

def register(h):
    h.record('compiler/byref-exception-safety',lambda:run(h))
    h.record('exceptions/persisted-fault-il',lambda:fault(h))

def run(h):
    output=h.run('dotnet',h.ROOT/'tests/CompilerChecks/bin/Release/net10.0/CompilerChecks.dll','--safety').stdout
    assert output=='Safety checks: 22 passed\n', output
    return dict(compilerAssertions=22)


def fault(h):
    import sys
    d=h.OUT/'persisted-fault'; d.mkdir(parents=True,exist_ok=True)
    pe=d/'FaultFixture.dll'
    h.run('dotnet',h.ROOT/'tests/CompilerChecks/bin/Release/net10.0/CompilerChecks.dll','--emit-fault',pe)
    expected=h.run('dotnet',pe).stdout
    assert expected=='normal path\nfilter before fault\nfault during unwind\nfiltered catch\n42\n',expected
    for mode in ('instruction','block'):
        for target,ext,host in [('js','mjs','node'),('py','py',sys.executable)]:
            output=d/(mode+'.'+ext)
            h.cli('compile',pe,'--target',target,'--out',output,'--dispatch',mode)
            assert h.run(host,output).stdout==expected
    return dict(coreClrStdout=expected,targetExecutions=4,nonZeroedExceptionLocals=True)
